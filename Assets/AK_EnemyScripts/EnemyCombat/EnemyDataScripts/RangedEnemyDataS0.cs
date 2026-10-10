using UnityEngine;
using CombatSystem.Data;

[CreateAssetMenu(fileName = "RangedEnemyData", menuName = "Combat/Enemy/Ranged Enemy Data SO")]
public class RangedEnemyDataSO : MinionEnemyDataSO
{
    [Header("Ranged Distance Bands")]
    [Tooltip("Distance where minion enters panic/retreat mode")]
    public float minKiteDistance = 5.5f;

    [Tooltip("Target sweet-spot orbit distance")]
    public float preferredDistance = 11.0f;

    [Tooltip("Distance where minion advances closer")]
    public float maxKiteDistance = 16.5f;

    [Tooltip("Hysteresis margin to prevent band flicking")]
    public float bandMargin = 1.2f;

    [Header("Kiting Locomotion Speeds")]
    public float retreatSpeed = 3.6f;
    public float strafeSpeed = 2.4f;
    public float advanceSpeed = 4.0f;

    [Header("Telegraph & Aim Mechanics (Fairness)")]
    public float telegraphDuration = 0.75f;
    [Range(0.4f, 0.9f)] public float aimLockPercentage = 0.72f;
    [Range(0f, 1.5f)] public float leadAimFactor = 0.85f;

    [Header("Attacks & Projectiles")]
    public BulletPattern defaultPatternPrefab;
    public Vector2 attackCooldown = new Vector2(2.5f, 4.5f);
    public Vector3 fireSocketOffset = new Vector3(0f, 1.3f, 0.6f);
    public LayerMask lineOfSightMask;
}