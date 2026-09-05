using System.Collections.Generic;
using UnityEngine;

public class BeaconSpawnerManager : MonoBehaviour
{
    [Header("Core References")]
    [Tooltip("Reference to the BeaconHealth component in the scene.")]
    [SerializeField] private BeaconHealth beaconHealth;

    [Header("Spaceship Configuration")]
    [Tooltip("Prefab of the spaceship (must have SpaceshipController attached).")]
    [SerializeField] private GameObject spaceshipPrefab;

    [Tooltip("Available spaceship data ScriptableObject assets (Medium, Large, etc.).")]
    [SerializeField] private SpaceshipData[] availableShipTypes;

    [Header("Flight Waypoints")]
    [Tooltip("Off-screen points where spaceships originate.")]
    [SerializeField] private Transform[] spawnWaypoints;

    [Tooltip("In-camera drop target locations around the beacon.")]
    [SerializeField] private Transform[] dropTargetWaypoints;

    [Tooltip("Off-screen points where spaceships exit.")]
    [SerializeField] private Transform[] exitWaypoints;

    [Header("Wave Tuning")]
    [Tooltip("Number of initial spaceships sent when the beacon first activates.")]
    [SerializeField] private int initialShipCount = 1;

    [Tooltip("If checked, reinforcement ships will arrive when active enemies run low. Uncheck if you want ONLY the initial ship(s) to spawn.")]
    [SerializeField] private bool allowReinforcements = false;

    [Tooltip("Threshold of active enemies that triggers a new reinforcement wave.")]
    [SerializeField] private int reinforcementThreshold = 2;

    [Header("Escalation Intensity Settings (Post-Shield)")]
    [Tooltip("Initial delay in seconds before the first post-shield reinforcement ship arrives.")]
    [SerializeField] private float initialEscalationDelay = 5.0f;

    [Tooltip("Multiplier by which the delay shrinks after each ship delivery.")]
    [Range(0.5f, 0.98f)]
    [SerializeField] private float intensityAccelerationFactor = 0.85f;

    [Tooltip("Absolute minimum delay limit in seconds between ship arrivals.")]
    [SerializeField] private float minimumSpawnDelay = 1.0f;

    private List<GameObject> activeEnemies = new List<GameObject>();
    private bool isEncounterRunning = false;
    private bool isWaitingForReinforcements = false;
    private int activeShipsInTransit = 0;
    private Coroutine escalationRoutine = null;

    private void OnEnable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnBeaconActivated.AddListener(OnBeaconActivated);
            beaconHealth.OnShieldDropped.AddListener(OnShieldDropped);
            beaconHealth.OnBeaconDestroyed.AddListener(OnBeaconDestroyed);
        }
    }

    private void OnDisable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnBeaconActivated.RemoveListener(OnBeaconActivated);
            beaconHealth.OnShieldDropped.RemoveListener(OnShieldDropped);
            beaconHealth.OnBeaconDestroyed.RemoveListener(OnBeaconDestroyed);
        }
    }

    private void Update()
    {
        if (!isEncounterRunning) return;

        activeEnemies.RemoveAll(enemy => enemy == null || !enemy.activeInHierarchy);

        if (beaconHealth != null && beaconHealth.IsShieldActive && 
            allowReinforcements && 
            activeEnemies.Count <= reinforcementThreshold && 
            !isWaitingForReinforcements && 
            activeShipsInTransit == 0)
        {
            StartCoroutine(SpawnReinforcementWaveRoutine());
        }
    }

    private void OnBeaconActivated()
    {
        if (availableShipTypes == null || availableShipTypes.Length == 0 || availableShipTypes[0] == null)
        {
            Debug.LogError("[BeaconSpawnerManager] FATAL: No SpaceshipData ScriptableObjects assigned in 'Available Ship Types'!");
            return;
        }

        isEncounterRunning = true;
        activeEnemies.Clear();
        activeShipsInTransit = 0;

        for (int i = 0; i < initialShipCount; i++)
        {
            DispatchRandomSpaceship();
        }
    }

    private System.Collections.IEnumerator SpawnReinforcementWaveRoutine()
    {
        isWaitingForReinforcements = true;
        yield return new WaitForSeconds(2.0f);

        if (beaconHealth != null && beaconHealth.IsShieldActive && allowReinforcements && activeShipsInTransit == 0)
        {
            DispatchRandomSpaceship();
        }

        isWaitingForReinforcements = false;
    }

    private void DispatchRandomSpaceship()
    {
        if (!isEncounterRunning || spaceshipPrefab == null || availableShipTypes == null || availableShipTypes.Length == 0 || availableShipTypes[0] == null)
        {
            return;
        }

        SpaceshipData selectedData = availableShipTypes[Random.Range(0, availableShipTypes.Length)];

        Vector3 spawnPos = spawnWaypoints.Length > 0 ? spawnWaypoints[Random.Range(0, spawnWaypoints.Length)].position : transform.position + Vector3.back * 30f;
        Vector3 targetPos = dropTargetWaypoints.Length > 0 ? dropTargetWaypoints[Random.Range(0, dropTargetWaypoints.Length)].position : transform.position;
        Vector3 exitPos = exitWaypoints.Length > 0 ? exitWaypoints[Random.Range(0, exitWaypoints.Length)].position : transform.position + Vector3.forward * 30f;

        GameObject shipInstance = Instantiate(spaceshipPrefab, spawnPos, Quaternion.identity);
        SpaceshipController controller = shipInstance.GetComponent<SpaceshipController>();

        if (controller != null)
        {
            activeShipsInTransit++;
            controller.InitializeMission(
                selectedData,
                spawnPos,
                targetPos,
                exitPos,
                OnEnemyDroppedFromShip,
                OnShipMissionComplete
            );
        }
        else
        {
            Destroy(shipInstance);
        }
    }

    private void OnEnemyDroppedFromShip(GameObject enemy)
    {
        if (enemy != null && !activeEnemies.Contains(enemy))
        {
            activeEnemies.Add(enemy);
        }
    }

    private void OnShipMissionComplete(SpaceshipController ship)
    {
        activeShipsInTransit = Mathf.Max(0, activeShipsInTransit - 1);

        if (ship != null)
        {
            Destroy(ship.gameObject);
        }
    }

    public void KillAllActiveEnemies()
    {
        foreach (var enemy in new List<GameObject>(activeEnemies))
        {
            if (enemy != null && enemy.activeInHierarchy)
            {
                DummyHealth health = enemy.GetComponent<DummyHealth>();
                if (health != null)
                {
                    health.TakeDamage(99999f);
                }
                else
                {
                    EnemyObjectPool.Instance?.ReturnToPool(enemy);
                }
            }
        }
        activeEnemies.Clear();
    }

    public void RegisterOnlyKill(GameObject enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
        }

        if (beaconHealth != null)
        {
            beaconHealth.RegisterEnemyKilled();
        }
    }

    public void RegisterEnemyDefeated(GameObject enemy)
    {
        RegisterOnlyKill(enemy);
        EnemyObjectPool.Instance.ReturnToPool(enemy);
    }

    private void OnShieldDropped()
    {
        if (!isEncounterRunning) return;
        if (escalationRoutine != null) StopCoroutine(escalationRoutine);
        escalationRoutine = StartCoroutine(EscalationSpawnRoutine());
    }

    private System.Collections.IEnumerator EscalationSpawnRoutine()
    {
        float currentDelay = initialEscalationDelay;
        yield return new WaitForSeconds(currentDelay);

        while (isEncounterRunning && beaconHealth != null && !beaconHealth.IsShieldActive)
        {
            DispatchRandomSpaceship();
            currentDelay = Mathf.Max(minimumSpawnDelay, currentDelay * intensityAccelerationFactor);
            yield return new WaitForSeconds(currentDelay);
        }
    }

    

    private void OnBeaconDestroyed()
    {
        isEncounterRunning = false;
        allowReinforcements = false;

        if (escalationRoutine != null)
        {
            StopCoroutine(escalationRoutine);
            escalationRoutine = null;
        }

        StopAllCoroutines();
        activeEnemies.Clear();
        activeShipsInTransit = 0;
    }
}