using System.Collections;
using UnityEngine;

public class CombatHitboxController : MonoBehaviour
{
    [Header("Limb Transforms")]
    public Transform leftFist;
    public Transform rightFist;
    public Transform leftFoot;
    public Transform rightFoot;
    public Transform leftElbow;
    public Transform rightElbow;
    public Transform leftKnee;
    public Transform rightKnee;

    [Header("Default Hitbox Settings")]
    [Tooltip("Keep this tight (e.g. 0.3f to 0.35f) to prevent ghost hits at long range.")]
    public float hitboxRadius = 0.35f; 
    public LayerMask enemyLayer;

    private bool isHitStopping = false;
    private Transform currentActiveLimb;
    private bool isHitboxActive = false;
    private Collider[] hitResults = new Collider[10];

    public void TriggerHitbox(int limbIndex)
    {
        switch (limbIndex)
        {
            case 0: currentActiveLimb = leftFist; break;
            case 1: currentActiveLimb = rightFist; break;
            case 2: currentActiveLimb = rightFoot; break;
            case 3: currentActiveLimb = leftFoot; break;
            case 4: currentActiveLimb = leftElbow; break;
            case 5: currentActiveLimb = rightElbow; break;
            case 6: currentActiveLimb = leftKnee; break;
            case 7: currentActiveLimb = rightKnee; break;
            default: currentActiveLimb = rightFist; break;
        }
        isHitboxActive = true;
    }

    public void DisableHitbox()
    {
        isHitboxActive = false;
        currentActiveLimb = null;
    }

    private void Update()
    {
        if (isHitboxActive && currentActiveLimb != null)
        {
            CheckForHits();
        }
    }

    private void CheckForHits()
    {
        PlayerController player = GetComponentInParent<PlayerController>();
        AttackData currentHit = null;

        if (player == null) return;

        if (player.CurrentState == player.AOEAttackState)
        {
            currentHit = player.specialAttackY;
        }
        else if (player.CurrentState == player.PowerPunchState)
        {
            if (player.equippedStyle != null)
                currentHit = player.equippedStyle.GetActiveChargeAttack();
        }
        else
        {
            if (player.equippedStyle != null && player.equippedStyle.lightComboSequence.Length > player.CurrentComboIndex)
            {
                currentHit = player.equippedStyle.lightComboSequence[player.CurrentComboIndex];
            }
        }

        if (currentHit == null) return;

        int finalDamage = Mathf.RoundToInt(currentHit.damage * player.CurrentChargeMultiplier);
        float finalKnockback = currentHit.knockbackForce * player.CurrentChargeMultiplier;

        if (currentHit.isAOE)
        {
            int hits = Physics.OverlapSphereNonAlloc(player.transform.position, currentHit.aoeRadius, hitResults, enemyLayer);
            int validHitCount = 0;

            for (int i = 0; i < hits; i++)
            {
                if (validHitCount >= currentHit.maxEnemiesHit) break;

                Collider enemyCol = hitResults[i];
                if (enemyCol.transform == player.transform) continue;

                Vector3 toEnemy = enemyCol.transform.position - player.transform.position;
                toEnemy.y = 0;
                Vector3 dirToEnemy = toEnemy.normalized;

                if (Vector3.Angle(player.transform.forward, dirToEnemy) <= currentHit.coneAngle / 2f)
                {
                    IDamageable damageable = enemyCol.GetComponent<IDamageable>();
                    if (damageable != null)
                    {
                        Vector3 hitPoint = enemyCol.ClosestPoint(player.transform.position);
                        Vector3 forceVector = (dirToEnemy * finalKnockback) + (Vector3.up * currentHit.verticalLift);
                        float finalForce = forceVector.magnitude > 0 ? forceVector.magnitude : finalKnockback;

                        Vector3 hitDirection = forceVector.normalized;
                        damageable.TakeDamage(finalDamage, hitPoint, hitDirection, finalForce, currentHit.customHitSound, currentHit.attackID, true);
                        validHitCount++;
                    }
                }
            }

            if (currentHit.customVFX != null) 
            {
                Instantiate(currentHit.customVFX, player.transform.position, Quaternion.identity);
            }
            
            if (validHitCount > 0) TriggerJuice(currentHit);
            DisableHitbox();
        } // <-- The missing bracket was re-added here.
        else
        {
            int hits = Physics.OverlapSphereNonAlloc(currentActiveLimb.position, hitboxRadius, hitResults, enemyLayer);

            for (int i = 0; i < hits; i++)
            {
                Collider enemyCol = hitResults[i];
                if (enemyCol.transform == transform) continue;

                IDamageable damageable = enemyCol.GetComponent<IDamageable>();

                if (damageable != null)
                {
                    Vector3 hitDirection = (enemyCol.transform.position - transform.position).normalized;
                    hitDirection.y = 0;

                    damageable.TakeDamage(finalDamage, currentActiveLimb.position, hitDirection, finalKnockback, currentHit.customHitSound, currentHit.attackID, false);

                    TriggerJuice(currentHit);
                    DisableHitbox();
                    break;
                }
            }
        }
    }

    private void TriggerJuice(AttackData hitData)
    {
        if (!isHitStopping)
        {
            StartCoroutine(HitStopRoutine(hitData.hitStopDuration));
        }

        IsoCameraRig camRig = Camera.main.GetComponent<IsoCameraRig>();
        if (camRig != null)
        {
            camRig.TriggerShake(hitData.cameraShakeDuration, hitData.cameraShakeIntensity);
        }
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        isHitStopping = true;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        isHitStopping = false;
    }

    private void OnDrawGizmos()
    {
        if (isHitboxActive && currentActiveLimb != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(currentActiveLimb.position, hitboxRadius);
        }
    }
}