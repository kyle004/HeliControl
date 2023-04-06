using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Configuration;
using Newtonsoft.Json;
using UnityEngine;
using ProtoBuf;
using UnityEngine.AI;
using Facepunch;
using Network;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust;
using Rust;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.IO;

namespace Oxide.Plugins
{
    [Info("HeliControl", "TooTwisted", "1.0.0")]
    [Description("My custom plugin description")]
    public class HeliControl : RustPlugin
    {
        #region Fields
        private const string MINI_PREFAB = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private const string CargoShip = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";
        private float flightHeight = 20f;
        private float flightSpeed = 10f;
        public float smoothAltitudeAdjustment = 0.0f;
        #endregion

        protected override void LoadDefaultConfig()
        {
            Config["FlightHeight"] = flightHeight;
            Config["FlightSpeed"] = flightSpeed;
            SaveConfig();
        }

        private void Init()
        {
            flightHeight = Convert.ToSingle(Config["FlightHeight"]);
            flightSpeed = Convert.ToSingle(Config["FlightSpeed"]);
        }



        [ChatCommand("spawnheli")]
        private void SpawnMiniCopterCommand(BasePlayer player, string command, string[] args)
        {
            // Get the MiniCopter prefab
            var miniCopterPrefab = GameManager.server.FindPrefab(MINI_PREFAB);

            if (miniCopterPrefab == null)
            {
                Puts("Failed to find the MiniCopter prefab!");
                return;
            }

            // Get the player's position and rotation
            var position = player.transform.position + (player.transform.forward * 3f);
            var rotation = player.transform.rotation;

            // Spawn the MiniCopter
            var miniCopterEntity = (BaseEntity)GameManager.server.CreateEntity(MINI_PREFAB, position, rotation);
            if (miniCopterEntity == null)
            {
                Puts("Failed to create the MiniCopter entity!");
                return;
            }

            miniCopterEntity.Spawn();

            Puts("Spawned MiniCopter at " + position);
        }

private void FlyHelicopterUp(BaseEntity miniCopterEntity, Vector3 targetPositionAltitude, Action onComplete)
{
    if (miniCopterEntity == null)
    {
        Puts("MiniCopter is null!");
        return;
    }

    Rigidbody helicopterRigidbody = miniCopterEntity.GetComponent<Rigidbody>();
    if (helicopterRigidbody == null)
    {
        Puts("MiniCopter does not have a Rigidbody!");
        return;
    }

    Timer flyTimer = null;
    flyTimer = timer.Repeat(0.1f, -1, () =>
    {
        if (Vector3.Distance(miniCopterEntity.transform.position, targetPositionAltitude) > 0.1f)
        {
            Vector3 forceDirection = (targetPositionAltitude - miniCopterEntity.transform.position).normalized;
            helicopterRigidbody.AddForce(forceDirection * 10f, ForceMode.Acceleration);
        }
        else
        {
            timer.Destroy(ref flyTimer);
            onComplete?.Invoke();
        }
    });
}


[ChatCommand("fly")]
private void FlyCommand(BasePlayer player, string command, string[] args)
{
    var mountedEntity = player.GetMounted();
    if (mountedEntity == null)
    {
        SendReply(player, "You are not currently mounted on any entity");
        return;
    }

    var miniCopterEntity = mountedEntity.GetComponentInParent<BaseHelicopterVehicle>();
    if (miniCopterEntity == null || !miniCopterEntity.gameObject.name.Contains("minicopter.entity"))
    {
        SendReply(player, "You are not currently mounted on a MiniCopter");
        return;
    }

    // Check if the player provided a target position
    if (args.Length < 3)
    {
        SendReply(player, "Please specify a target position in the format /fly x y z");
        return;
    }

    // Parse the x, y, and z coordinates from the command arguments
    float x, y, z;
    if (!float.TryParse(args[0], out x) || !float.TryParse(args[1], out y) || !float.TryParse(args[2], out z))
    {
        SendReply(player, "Invalid target position specified. Please use numbers only.");
        return;
    }

    // Check the helicopter's height relative to the ground
    RaycastHit hit;
    float groundHeight;
    if (Physics.Raycast(miniCopterEntity.transform.position, Vector3.down, out hit, 100f, LayerMask.GetMask("Terrain", "World", "Construction")))
    {
        groundHeight = hit.point.y;
    }
    else
    {
        SendReply(player, "Unable to determine the ground height below the MiniCopter");
        return;
    }

    float currentHeight = miniCopterEntity.transform.position.y - groundHeight;
    if (currentHeight < 1f)
    {
        SendReply(player, "The MiniCopter must be at least 1 meters above the ground before flying");
        return;
    }

    // Check if the helicopter is facing the destination
    Vector3 destination = new Vector3(x, y, z);
    Vector3 directionToDestination = (destination - miniCopterEntity.transform.position).normalized;
    directionToDestination.y = 0; // Ignore the Y axis to prevent the helicopter from tilting up or down

    // float dotProduct = Vector3.Dot(miniCopterEntity.transform.forward, directionToDestination);
    // if (!Mathf.Approximately(dotProduct, 1.0f))
    // {
    //     SendReply(player, "The MiniCopter must be facing the destination before flying");
    //     return;
    // }

    // Fly the helicopter to the target position
    FlyHelicopterTo(miniCopterEntity.GetComponent<BaseEntity>(), destination, flightHeight, player);
}


private void FlyHelicopterTo(BaseEntity miniCopterEntity, Vector3 targetPosition, float targetAltitude, BasePlayer player, float gainAltitude = 10f)
{
    if (miniCopterEntity == null)
    {
        Puts("MiniCopter is null!");
        return;
    }

    var helicopterRigidbody = miniCopterEntity.GetComponent<Rigidbody>();
    if (helicopterRigidbody == null)
    {
        Puts("MiniCopter does not have a Rigidbody!");
        return;
    }

    const float rotationSpeed = 5f;
    int numberOfRepetitions = Mathf.CeilToInt(Vector3.Distance(miniCopterEntity.transform.position, targetPosition) / (flightSpeed * 0.1f));

    // Gain altitude and rotate the helicopter towards the target position
    GainAltitude(helicopterRigidbody, gainAltitude);
    RotateHelicopterTowards(miniCopterEntity, targetPosition);

    // Add a short delay before flying towards the target
    timer.Once(2.0f, () =>
    {
        Timer flyTimer = null;
        flyTimer = timer.Repeat(0.1f, numberOfRepetitions, () =>
        {
          
          
            var currentPosition = miniCopterEntity.transform.position;

              // Gain altitude and rotate the helicopter towards the target position
            GainAltitude(helicopterRigidbody, gainAltitude);

            // Rotate the helicopter towards the target position
            RotateHelicopterTowards(miniCopterEntity, targetPosition);

            RaycastHit hit;
            Vector3 raycastDirection = (targetPosition - currentPosition).normalized;
            if (Physics.Raycast(currentPosition, raycastDirection, out hit, 100f, LayerMask.GetMask("Terrain", "World", "Construction")))
            {
                float obstacleHeight = hit.point.y;
                if (obstacleHeight + flightHeight > targetAltitude)
                {
                    targetAltitude = obstacleHeight + flightHeight;
                }
            }

            var targetPositionWithAltitude = new Vector3(targetPosition.x, targetAltitude, targetPosition.z);

            var distanceToTarget = Vector3.Distance(currentPosition, targetPositionWithAltitude);
            if (distanceToTarget > 0.1f)
            {
                var direction = (targetPositionWithAltitude - currentPosition).normalized;
                var speed = Mathf.Min(flightSpeed, distanceToTarget);

                var targetVelocity = direction * speed;
                targetVelocity.y = helicopterRigidbody.velocity.y;

                helicopterRigidbody.velocity = targetVelocity;

                // Adjust the altitude to maintain the target altitude
                var currentAltitude = miniCopterEntity.transform.position.y;
                if (currentAltitude < targetAltitude)
                {
                    var forceDirection = Vector3.up;
                    var forceMagnitude = Mathf.Min(2f, targetAltitude - currentAltitude) * 2f;
                    helicopterRigidbody.AddForce(forceDirection * forceMagnitude, ForceMode.Acceleration);
                }
                else if (currentAltitude > targetAltitude)
                {
                    var forceDirection = Vector3.down;
                    var forceMagnitude = Mathf.Min(20f, currentAltitude - targetAltitude) * 10f;
                    helicopterRigidbody.AddForce(forceDirection * forceMagnitude, ForceMode.Acceleration);
                }
                else if (currentAltitude - currentPosition.y < 1.0f)
                {
                    helicopterRigidbody.AddForce(Vector3.up * 20f, ForceMode.Acceleration);
                }
            }
            else
            {
                timer.Destroy(ref flyTimer);
                SendReply(player, "Landing sequence initiated");
                LandHelicopter(miniCopterEntity, player);
            }
        });
    });
}

        private void GainAltitude(Rigidbody helicopterRigidbody, float gainAltitude)
        {
            helicopterRigidbody.AddForce(Vector3.up * gainAltitude, ForceMode.VelocityChange);
        }


        private void RotateHelicopterTowards(BaseEntity miniCopterEntity, Vector3 targetPosition)
        {
            if (miniCopterEntity == null)
            {
                Puts("MiniCopter is null!");
                return;
            }

            var direction = (targetPosition - miniCopterEntity.transform.position).normalized;
            direction.y = 0; // Ignore the Y axis to prevent the helicopter from tilting up or down
            var lookRotation = Quaternion.LookRotation(direction);

            miniCopterEntity.transform.rotation = Quaternion.Slerp(
                miniCopterEntity.transform.rotation,
                lookRotation,
                Time.deltaTime * 2f
            );
        }

        [ChatCommand("land")]
        private void LandCommand(BasePlayer player, string command, string[] args)
        {
            var mountedEntity = player.GetMounted();
            if (mountedEntity == null)
            {
                SendReply(player, "You are not currently mounted on any entity");
                return;
            }

            var miniCopterEntity = mountedEntity.GetComponentInParent<BaseHelicopterVehicle>();
            if (miniCopterEntity == null || !miniCopterEntity.gameObject.name.Contains("minicopter.entity"))
            {
                SendReply(player, "You are not currently mounted on a MiniCopter");
                return;
            }

            LandHelicopter(miniCopterEntity.GetComponent<BaseEntity>(), player);
        }



        private void LandHelicopter(BaseEntity miniCopterEntity, BasePlayer player)
        {
            if (miniCopterEntity == null)
            {
                Puts("MiniCopter is null!");
                return;
            }

            var helicopterRigidbody = miniCopterEntity.GetComponent<Rigidbody>();
            if (helicopterRigidbody == null)
            {
                Puts("MiniCopter does not have a Rigidbody!");
                return;
            }

            Timer landTimer = null;
            landTimer = timer.Repeat(0.1f, -1, () =>
            {
                var currentPosition = miniCopterEntity.transform.position;
                RaycastHit hit;

                if (Physics.Raycast(currentPosition, Vector3.down, out hit, 10f, LayerMask.GetMask("Terrain", "World", "Construction")))
                {
                    float groundHeight = hit.point.y;
                    float distanceToGround = currentPosition.y - groundHeight;

                    if (distanceToGround > 1.0f)
                    {
                        var forceDirection = Vector3.down;
                        var forceMagnitude = Mathf.Min(20f, distanceToGround) * 10f;
                        helicopterRigidbody.AddForce(forceDirection * forceMagnitude, ForceMode.Acceleration);
                    }
                    else
                    {
                        helicopterRigidbody.velocity = Vector3.zero;
                        timer.Destroy(ref landTimer);
                    }
                }
            });
        }
        

        void Start()
        {

            // Set the smoothAltitudeAdjustment to zero initially.
            smoothAltitudeAdjustment = 0.0f;
        }

public class HelicopterController : MonoBehaviour
{
    public Transform target;
    public float speed = 5f;
    public float rotationSpeed = 1f;

    private bool isFlying = false;
    private bool isRotated = false;

    void Update()
    {
        if (isFlying)
        {
            if (!isRotated)
            {
                RotateTowardsTarget();
            }
            else
            {
                FlyTowardsTarget();
            }
        }
    }

    void RotateTowardsTarget()
    {
        Vector3 targetDirection = (target.position - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);

        if (Quaternion.Angle(transform.rotation, targetRotation) < 1f)
        {
            isRotated = true;
        }
    }

    void FlyTowardsTarget()
    {
        transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, target.position) < 0.1f)
        {
            isFlying = false;
            isRotated = false;
        }
    }

    public void StartFlying()
    {
        isFlying = true;
    }
}

// public class FlyCommandHandler : MonoBehaviour
// {
//     public HelicopterController helicopterController;

//     void Update()
//     {
//         if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
//         {
//             if (helicopterController != null)
//             {
//                 helicopterController.StartFlying();
//             }
//         }
//     }
// }

        


    }
}
        // public class HelicopterController : MonoBehaviour
        // {
        //     private Vector3 Lerp(Vector3 a, Vector3 b, float t)
        //     {
        //         return a + (b - a) * t;
        //     }

        // private IEnumerator SmootherHelicopterFlight(Vector3 startCoords, Vector3 endCoords, float[] objectHeights, float[] terrainHeights)
        // {
        //     float totalTime = 30f;
        //     float totalDistance = Vector3.Distance(endCoords, startCoords);
        //     float speed = totalDistance / totalTime;
        //     Vector3 currentPos = startCoords;

        //     float altitudeLerpSpeed = 0.3f; // Increase this value to make altitude adjustments smoother

        //     // Rotate the helicopter to face the target before starting the flight
        //     Vector3 targetDirection = (endCoords - currentPos).normalized;
        //     float rotationSpeed = 2f; // Adjust this value to control rotation speed
        //     Quaternion targetRotation = Quaternion.LookRotation(targetDirection);
        //     while (Quaternion.Angle(transform.rotation, targetRotation) > 0.1f)
        //     {
        //         transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        //         yield return null;
        //     }

        //     int totalFrames = (int)(totalTime * 30);
        //     for (int i = 0; i < totalFrames; i++)
        //     {
        //         Vector3 direction = (endCoords - currentPos).normalized;
        //         currentPos += direction * speed / 30;

        //         // Calculate new target altitude based on terrain and object heights
        //         float targetAltitude = GetTargetAltitude(currentPos, objectHeights, terrainHeights);

        //         // Smoothly adjust altitude using Mathf.Lerp
        //         currentPos.y = Mathf.Lerp(currentPos.y, targetAltitude, altitudeLerpSpeed);

        //         // Update helicopter position
        //         transform.position = currentPos;

        //         yield return new WaitForSeconds(1f / 30);
        //     }
        // }


        //     private float GetTargetAltitude(Vector3 currentPosition, float[] objectHeights, float[] terrainHeights)
        //     {
        //         // Your implementation to find the target altitude based on terrain and object heights
        //         // This may involve raycasting, querying heightmaps or other methods you are using
        //         // The following is a placeholder for the actual implementation
        //         float targetAltitude = 0f;

        //         return targetAltitude;
        //     }

        //     // Start is called before the first frame update
        //     void Start()
        //     {
        //         Vector3 startCoords = new Vector3(0, 0, 0);
        //         Vector3 endCoords = new Vector3(10, 0, 10);
        //         float[] objectHeights = new float[0];
        //         float[] terrainHeights = new float[0];

        //         StartCoroutine(SmootherHelicopterFlight(startCoords, endCoords, objectHeights, terrainHeights));
        //     }
        // }
