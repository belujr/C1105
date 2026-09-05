using UnityEngine;

[CreateAssetMenu(fileName = "StandardMeleeAttack", menuName = "Combat/Enemy Attack Data/Standard Melee Attack")]
public class StandardMeleeAttackSO : BaseAttackDataSO
{
    public override void ExecuteAttackPayload(Transform attacker, Transform target)
    {
        if (target == null) return;

        float distance = Vector3.Distance(attacker.position, target.position);
        if (distance <= AttackRange + 1.0f)
        {
            if (target.TryGetComponent<IDamageable>(out var damageable))
            {
                Vector3 hitDirection = (target.position - attacker.position).normalized;
                damageable.TakeDamage(DamageAmount, attacker.position, hitDirection, HitStopDuration, HitSound);
            }
        }
    }
}