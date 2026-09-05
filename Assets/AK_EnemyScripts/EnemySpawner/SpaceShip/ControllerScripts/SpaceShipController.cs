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

    [Header("Drop Bay Configuration")]
    [Tooltip("Child Transform representing the cargo bay / hatch under the ship where enemies drop from. If left empty, defaults to ship center.")]
    [SerializeField] private Transform dropBayPoint;

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
        
        // Reset counters
        enemiesDroppedCount = 0;
        dropTimer = 0f;

        // Ensure GameObject is active so Update() runs immediately
        gameObject.SetActive(true);

        // Begin flight mission
        currentState = ShipState.FlyingIn;
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

    /// <summary>
    /// Moves the spaceship smoothly toward a target position and rotates to face movement direction.
    /// </summary>
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
    /// Handles procedural dropping of enemies with time intervals.
    /// </summary>
    private void HandlePayloadDrop()
    {
        dropTimer += Time.deltaTime;

        if (dropTimer >= shipData.dropInterval)
        {
            dropTimer = 0f;

            if (enemiesDroppedCount < shipData.payloadCapacity)
            {
                DropSingleEnemy();
                enemiesDroppedCount++;
            }
            else
            {
                // Payload fully deployed, transition to fly out
                currentState = ShipState.FlyingOut;
            }
        }
    }

    /// <summary>
    /// Borrows an enemy from the pool, drops them from the cargo bay point, and deploys them.
    /// </summary>
    private void DropSingleEnemy()
    {
        if (shipData.enemyPrefabs == null || shipData.enemyPrefabs.Length == 0) return;

        // Pick a random enemy prefab from the ship's payload options
        GameObject randomPrefab = shipData.enemyPrefabs[Random.Range(0, shipData.enemyPrefabs.Length)];

        // Determine drop origin: Use dropBayPoint if assigned, otherwise fallback to ship center transform
        Vector3 dropOrigin = dropBayPoint != null ? dropBayPoint.position : transform.position;

        // Calculate a randomized scatter offset on the ground plane
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * shipData.dropScatterRadius;
        Vector3 finalSpawnPos = new Vector3(dropOrigin.x + randomCircle.x, dropOrigin.y, dropOrigin.z + randomCircle.y);

        // Borrow enemy from our Object Pool (Zero GC allocation)
        GameObject spawnedEnemy = EnemyObjectPool.Instance.GetPooledEnemy(randomPrefab, finalSpawnPos, Quaternion.identity);

        // Notify Spawner Manager using the correct field name
        onEnemyDroppedCallback?.Invoke(spawnedEnemy);
    }
}