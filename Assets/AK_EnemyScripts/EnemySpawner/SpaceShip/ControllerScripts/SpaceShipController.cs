using System.Collections.Generic;
using UnityEngine;

public class SpaceshipController : MonoBehaviour
{
    private enum ShipState { Idle, FlyingIn, DroppingPayload, FlyingOut }
    private ShipState currentState = ShipState.Idle;

    private SpaceshipData shipData;
    private Vector3 dropTargetPosition;
    private Vector3 exitPosition;

    private System.Action<GameObject> onEnemyDroppedCallback;
    private System.Action<SpaceshipController> onShipMissionCompleteCallback;

    private float dropTimer = 0f;
    private int enemiesDroppedCount = 0;

    [Header("Passenger Mount Points")]
    [Tooltip("Assign empty Transforms located on the ship (e.g., under wings or along side rails) where enemies will hang.")]
    [SerializeField] private Transform[] passengerMountPoints;
    
    // Tracks the specific enemies hanging on this ship
    private List<EnemyShipPassenger> loadedPassengers = new List<EnemyShipPassenger>();

    /// <summary>
    /// Initializes and launches the spaceship mission.
    /// </summary>
    public void InitializeMission(
        SpaceshipData data, 
        Vector3 startPos, 
        Vector3 targetPos, 
        Vector3 endPos,
        System.Action<GameObject> enemyDroppedCallback,
        System.Action<SpaceshipController> missionCompleteCallback)
    {
        shipData = data;
        dropTargetPosition = targetPos;
        exitPosition = endPos;
        onEnemyDroppedCallback = enemyDroppedCallback;
        onShipMissionCompleteCallback = missionCompleteCallback;

        // Position the ship at the start waypoint
        transform.position = startPos;
        
        // Reset counters and clear any old passengers
        enemiesDroppedCount = 0;
        dropTimer = 0f;
        loadedPassengers.Clear();

        // Load all enemies onto the mount points before the ship starts moving
        LoadPassengers();

        gameObject.SetActive(true);
        currentState = ShipState.FlyingIn;
    }

    /// <summary>
    /// Pre-spawns the enemies from the pool and mounts them to the ship's empty points.
    /// </summary>
    private void LoadPassengers()
    {
        if (shipData.enemyPrefabs == null || shipData.enemyPrefabs.Length == 0) return;
        
        if (passengerMountPoints == null || passengerMountPoints.Length == 0)
        {
            Debug.LogWarning("[SpaceshipController] No passenger mount points assigned in the Inspector!");
            return;
        }

        // Spawn up to the ship data's capacity, capped by how many physical mount points exist
        int spawnCount = Mathf.Min(shipData.payloadCapacity, passengerMountPoints.Length);

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject randomPrefab = shipData.enemyPrefabs[Random.Range(0, shipData.enemyPrefabs.Length)];
            Transform mountPoint = passengerMountPoints[i];

            // Pull the enemy from the pool and place them at the mount point
            GameObject spawnedEnemy = EnemyObjectPool.Instance.GetPooledEnemy(randomPrefab, mountPoint.position, mountPoint.rotation);

            if (spawnedEnemy.TryGetComponent<EnemyShipPassenger>(out var passenger))
            {
                // Lock physics/AI and play hanging animation
                passenger.MountToShip(mountPoint);
                loadedPassengers.Add(passenger);
            }
            else
            {
                Debug.LogError($"[SpaceshipController] Enemy {randomPrefab.name} is missing the EnemyShipPassenger script!");
            }
        }
    }

    private void Update()
    {
        if (shipData == null || currentState == ShipState.Idle) return;

        switch (currentState)
        {
            case ShipState.FlyingIn:
                MoveTowardsTarget(dropTargetPosition, shipData.flyInSpeed, () => {
                    currentState = ShipState.DroppingPayload;
                });
                break;

            case ShipState.DroppingPayload:
                HandlePayloadDrop();
                break;

            case ShipState.FlyingOut:
                MoveTowardsTarget(exitPosition, shipData.flyOutSpeed, () => {
                    currentState = ShipState.Idle;
                    onShipMissionCompleteCallback?.Invoke(this);
                });
                break;
        }
    }

    private void MoveTowardsTarget(Vector3 target, float speed, System.Action onArrival)
    {
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        // Smoothly rotate ship toward movement direction
        Vector3 direction = (target - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
        }

        // Check if arrived at target
        if (Vector3.Distance(transform.position, target) <= 0.2f)
        {
            onArrival?.Invoke();
        }
    }

    /// <summary>
    /// Handles procedural dropping of the pre-loaded enemies with time intervals.
    /// </summary>
    private void HandlePayloadDrop()
    {
        dropTimer += Time.deltaTime;

        if (dropTimer >= shipData.dropInterval)
        {
            dropTimer = 0f;

            if (enemiesDroppedCount < loadedPassengers.Count)
            {
                EnemyShipPassenger passengerToDrop = loadedPassengers[enemiesDroppedCount];
                
                // Detach from ship, snap to ground, play landing animation, and activate AI
                passengerToDrop.DetachAndLand();
                
                // Notify the BeaconSpawnerManager that an enemy is officially in the fight
                onEnemyDroppedCallback?.Invoke(passengerToDrop.gameObject);
                
                enemiesDroppedCount++;
            }
            else
            {
                // Payload fully deployed, transition to fly out
                currentState = ShipState.FlyingOut;
            }
        }
    }
}