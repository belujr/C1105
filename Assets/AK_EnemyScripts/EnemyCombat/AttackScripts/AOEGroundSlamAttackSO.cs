using UnityEngine;

[CreateAssetMenu(fileName = "AOEGroundSlamAttack", menuName = "Combat/Enemy Attack Data/AOE Ground Slam Attack")]
public class AOEGroundSlamAttackSO : BaseAttackDataSO
{
    [Header("Area of Effect Specifics")]
    [SerializeField] private float slamRadius = 4.0f;
    [SerializeField] private LayerMask targetLayerMask;

    public override void ExecuteAttackPayload(Transform attacker, Transform target)
    {
        Collider[] hits = Physics.OverlapSphere(attacker.position, slamRadius, targetLayerMask);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].TryGetComponent<IDamageable>(out var damageable))
            {
                Vector3 hitDirection = (hits[i].transform.position - attacker.position).normalized;
                hitDirection.y = 0.3f; 
                damageable.TakeDamage(DamageAmount, attacker.position, hitDirection.normalized, HitStopDuration, HitSound);
            }
        }
    }
}