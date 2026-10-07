using System.Collections.Generic;
using UnityEngine;

public class SpaceshipController : MonoBehaviour
{
    private enum ShipState { Init, Approach, Stabilize, Hover, Anticipation, Exit }
    private ShipState currentState = ShipState.Init;

    [Header("Hierarchy Setup")]
    [Tooltip("Child transform containing the mesh and mount points. Used for bank, pitch, and bob offsets.")]
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform[] passengerMountPoints;

    public bool IsDroppingOrHovering => currentState == ShipState.Stabilize || 
                                       currentState == ShipState.Hover || 
                                       currentState == ShipState.Anticipation;

    public float CurrentThrust { get; private set; } = 0f;
    public float CurrentSpeed { get; private set; } = 0f;

    private SpaceshipData shipData;
    private Vector3 spawnPos, dropTargetPos, exitPos;
    private Vector3 approachControlPos, exitControlPos;
    private Vector3 currentScatterTarget;

    private System.Action<GameObject> onEnemyDroppedCallback;
    private System.Action<SpaceshipController> onShipMissionCompleteCallback;

    private float stateTimer = 0f;
    private int enemiesDroppedCount = 0;
    private float dropTimer = 0f;

    // Visual State
    private float currentHeightOffset = 0f;
    private float heightVelocity = 0f;
    private float currentPitchOffset = 0f;
    private float pitchVelocity = 0f;
    private float currentBankAngle = 0f;
    private float noiseSeed;
    private Vector3 lastLogicalPosition;

    private List<EnemyShipPassenger> loadedPassengers = new List<EnemyShipPassenger>();

    public void InitializeMission(
        SpaceshipData data, 
        Vector3 startPos, 
        Vector3 targetPos, 
        Vector3 endPos,
        System.Action<GameObject> enemyDroppedCallback,
        System.Action<SpaceshipController> missionCompleteCallback)
    {
        shipData = data;
        onEnemyDroppedCallback = enemyDroppedCallback;
        onShipMissionCompleteCallback = missionCompleteCallback;

        // Force spawn high and off-frustum
        spawnPos = targetPos + (startPos - targetPos).normalized * 40f + Vector3.up * 30f;
        dropTargetPos = targetPos + Vector3.up * shipData.hoverAltitude;
        
        // Exit steeply upward and away
        Vector3 exitDir = (endPos - targetPos).normalized;
        if (exitDir.sqrMagnitude < 0.1f) exitDir = transform.forward;
        exitPos = targetPos + exitDir * 50f + Vector3.up * 40f;

        // Bezier control points for curved arcs
        approachControlPos = spawnPos + (dropTargetPos - spawnPos) * 0.5f - Vector3.up * 10f;
        exitControlPos = dropTargetPos + (exitPos - dropTargetPos) * 0.2f - Vector3.up * 5f;
        
        transform.position = spawnPos;
        transform.rotation = Quaternion.LookRotation((dropTargetPos - spawnPos).normalized);
        lastLogicalPosition = transform.position;
        
        noiseSeed = Random.Range(0f, 1000f);
        currentScatterTarget = dropTargetPos;

        // Tell the camera to look at this ship
        if (IsoCameraRig.Instance != null)
        {
            IsoCameraRig.Instance.RegisterPOI(transform);
        }

        ResetState();
        LoadPassengers();

        gameObject.SetActive(true);
        SwitchState(ShipState.Approach);
    }

    private void OnDisable()
    {
        // Failsafe cleanup for object pooling
        if (IsoCameraRig.Instance != null)
        {
            IsoCameraRig.Instance.UnregisterPOI(transform);
        }
    }

    private void ResetState()
    {
        currentState = ShipState.Init;
        enemiesDroppedCount = 0;
        dropTimer = 0f;
        stateTimer = 0f;
        currentHeightOffset = 0f;
        heightVelocity = 0f;
        currentPitchOffset = 0f;
        pitchVelocity = 0f;
        currentBankAngle = 0f;
        loadedPassengers.Clear();
        
        if (visualRoot != null)
        {
            visualRoot.localPosition = Vector3.zero;
            visualRoot.localRotation = Quaternion.identity;
        }
    }

    private void LoadPassengers()
    {
        if (shipData.enemyPrefabs == null || shipData.enemyPrefabs.Length == 0) return;
        int spawnCount = Mathf.Min(shipData.payloadCapacity, passengerMountPoints.Length);

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject prefab = shipData.enemyPrefabs[Random.Range(0, shipData.enemyPrefabs.Length)];
            Transform mount = passengerMountPoints[i];
            GameObject enemy = EnemyObjectPool.Instance.GetPooledEnemy(prefab, mount.position, mount.rotation);

            if (enemy.TryGetComponent<EnemyShipPassenger>(out var passenger))
            {
                passenger.MountToShip(mount);
                loadedPassengers.Add(passenger);
            }
        }
    }

   private void Update()
    {
        if (shipData == null || currentState == ShipState.Init || visualRoot == null) return;
        
        float dt = Time.unscaledDeltaTime;
        stateTimer += dt;

        Vector3 nextPos = transform.position;

        switch (currentState)
        {
            case ShipState.Approach:
                float tApp = Mathf.Clamp01(stateTimer / shipData.approachDuration);
                nextPos = CalculateBezierPoint(shipData.approachCurve.Evaluate(tApp), spawnPos, approachControlPos, dropTargetPos);
                CurrentThrust = Mathf.Lerp(1.2f, 0.4f, tApp); // High thrust coming in
                if (tApp >= 1f)
                {
                    heightVelocity = -15f; 
                    pitchVelocity = 30f;
                    SwitchState(ShipState.Stabilize);
                }
                break;

            case ShipState.Stabilize:
                UpdateSpringOffsets(dt);
                // Thrust flares up based on the spring's effort to stop the fall
                CurrentThrust = Mathf.Clamp01(0.3f + Mathf.Abs(heightVelocity) / 20f);
                if (Mathf.Abs(heightVelocity) < shipData.settleVelocityThreshold && Mathf.Abs(pitchVelocity) < shipData.settleVelocityThreshold)
                {
                    SwitchState(ShipState.Hover);
                }
                break;

            case ShipState.Hover:
                UpdateSpringOffsets(dt); 
                HandlePayloadDrop(dt);
                CurrentThrust = 0.3f; // Steady idle thrust
                
                nextPos = Vector3.MoveTowards(transform.position, currentScatterTarget, 2f * dt);
                if (Vector3.Distance(transform.position, currentScatterTarget) < 0.2f)
                {
                    Vector2 rand = Random.insideUnitCircle * shipData.dropScatterRadius;
                    currentScatterTarget = dropTargetPos + new Vector3(rand.x, 0, rand.y);
                }
                break;

            case ShipState.Anticipation:
                UpdateSpringOffsets(dt);
                CurrentThrust = Mathf.Lerp(0.3f, 0.05f, stateTimer / shipData.anticipationDuration); // Engine cuts/dips
                if (stateTimer >= shipData.anticipationDuration)
                {
                    SwitchState(ShipState.Exit);
                }
                break;

            case ShipState.Exit:
                float tEx = Mathf.Clamp01(stateTimer / shipData.exitDuration);
                nextPos = CalculateBezierPoint(shipData.exitCurve.Evaluate(tEx), dropTargetPos, exitControlPos, exitPos);
                CurrentThrust = Mathf.Lerp(0.1f, 1.5f, tEx); // Overdrive thrust
                if (tEx >= 1f)
                {
                    currentState = ShipState.Init;
                    CurrentThrust = 0f;
                    onShipMissionCompleteCallback?.Invoke(this);
                }
                break;
        }

        UpdateMovementAndVisuals(nextPos, dt);
    }




    private void UpdateMovementAndVisuals(Vector3 nextPos, float dt)
    {
        Vector3 velocity = (nextPos - lastLogicalPosition) / Mathf.Max(dt, 0.0001f);
        CurrentSpeed = velocity.magnitude; // Track speed for VFX
        
        if (currentState == ShipState.Approach || currentState == ShipState.Exit)
        {
            if (nextPos != transform.position)
            {
                Vector3 dir = (nextPos - transform.position).normalized;
                if (dir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * dt);
                }
            }
        }
        
        transform.position = nextPos;
        lastLogicalPosition = transform.position;

        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        float targetBank = Mathf.Clamp(-localVelocity.x * 1.5f, -shipData.maxBankAngle, shipData.maxBankAngle);
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, 5f * dt);

        float targetPitch = currentPitchOffset + Mathf.Clamp(-localVelocity.y * 1.5f, -shipData.maxPitchAngle, shipData.maxPitchAngle);
        float noiseY = (Mathf.PerlinNoise(Time.unscaledTime * shipData.noiseBobSpeed, noiseSeed) - 0.5f) * shipData.noiseBobAmplitude;

        visualRoot.localPosition = new Vector3(0, currentHeightOffset + noiseY, 0);
        visualRoot.localRotation = Quaternion.Euler(targetPitch, 0, currentBankAngle);
    }

    private void UpdateSpringOffsets(float dt)
    {
        float targetHeight = (currentState == ShipState.Anticipation) ? -shipData.anticipationDipAmount : 0f;
        float targetPitch = (currentState == ShipState.Anticipation) ? shipData.anticipationPitch : 0f;

        float hForce = -shipData.springStiffness * (currentHeightOffset - targetHeight) - shipData.springDamping * heightVelocity;
        heightVelocity += hForce * dt;
        currentHeightOffset += heightVelocity * dt;

        float pForce = -shipData.springStiffness * (currentPitchOffset - targetPitch) - shipData.springDamping * pitchVelocity;
        pitchVelocity += pForce * dt;
        currentPitchOffset += pitchVelocity * dt;
    }

    private void HandlePayloadDrop(float dt)
    {
        dropTimer += dt;
        if (dropTimer >= shipData.dropInterval)
        {
            dropTimer = 0f;
            if (enemiesDroppedCount < loadedPassengers.Count)
            {
                EnemyShipPassenger passenger = loadedPassengers[enemiesDroppedCount];
                passenger.DetachAndLand();
                onEnemyDroppedCallback?.Invoke(passenger.gameObject);
                enemiesDroppedCount++;
            }
            else
            {
                // Release the camera back to the player immediately
                if (IsoCameraRig.Instance != null)
                {
                    IsoCameraRig.Instance.UnregisterPOI(transform);
                }
                SwitchState(ShipState.Anticipation);
            }
        }
    }

    private void SwitchState(ShipState newState)
    {
        currentState = newState;
        stateTimer = 0f;
    }

    private Vector3 CalculateBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        return (uu * p0) + (2 * u * t * p1) + (tt * p2);
    }
}