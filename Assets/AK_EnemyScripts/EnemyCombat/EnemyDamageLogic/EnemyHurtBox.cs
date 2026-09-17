using UnityEngine;
using CombatSystem.Data;
using CombatSystem.Controllers;

public enum HitDirection
{
    Front,
    Back,
    Left,
    Right
}

public class EnemyHurtbox : MonoBehaviour, IDamageable
{
    [Header("References")]
    [SerializeField] private EnemyDummyController dummyController;
    [SerializeField] private DummyHealth dummyHealth;

    private void Awake()
    {
        if (dummyController == null)
        {
            dummyController = GetComponentInParent<EnemyDummyController>();
        }
        if (dummyHealth == null)
        {
            dummyHealth = GetComponentInParent<DummyHealth>();
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, float knockbackForce = 1.5f, AudioClip hitSfx = null, int attackID = -1, bool isAOE = false)
    {
        // I-FRAMES CHECK: If the dummy is dead on the ground or actively waking up, ignore damage entirely!
        if (dummyController != null && (dummyController.IsGettingUp || (dummyHealth != null && dummyHealth.IsDead)))
        {
            return;
        }

        // 1. Apply damage to health system (passing attackID and isAOE to trigger the dodge bouncer)
        if (dummyHealth != null)
        {
            dummyHealth.TakeDamage(damage, hitPoint, hitNormal, knockbackForce, hitSfx, attackID, isAOE);
        }

        // 2. Pack parameters into HitData
        HitData hitData = new HitData(Mathf.RoundToInt(damage), hitPoint, hitNormal, knockbackForce, 0.0f, null);

        // 3. Calculate local hit direction
        HitDirection hitDirection = CalculateHitDirection(hitData);

        // 4. Forward to dummy controller using damage as the attackID
        if (dummyController != null)
        {
            dummyController.ProcessHit(hitData, hitDirection, Mathf.RoundToInt(damage), knockbackForce);
        }
    }
    private HitDirection CalculateHitDirection(HitData hitData)
    {
        Vector3 worldDirection = (hitData.hitPoint - transform.position);
        worldDirection.y = 0f;
        worldDirection.Normalize();

        Vector3 localDir = transform.InverseTransformDirection(worldDirection);
        float forwardDot = Vector3.Dot(localDir, Vector3.forward);
        float rightDot = Vector3.Dot(localDir, Vector3.right);

        if (Mathf.Abs(forwardDot) > Mathf.Abs(rightDot))
        {
            return forwardDot > 0f ? HitDirection.Front : HitDirection.Back;
        }
        else
        {
            return rightDot > 0f ? HitDirection.Right : HitDirection.Left;
        }
    }
}