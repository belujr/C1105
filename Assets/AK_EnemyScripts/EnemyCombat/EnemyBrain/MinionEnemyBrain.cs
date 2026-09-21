using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class MinionEnemyBrain : BaseEnemyBrain, IDamageable, IHealable
{
    private MinionEnemyDataSO MinionData => enemyData as MinionEnemyDataSO;

    [Header("Health & Combat Settings")]
    public float maxHealth = 60f;
    private float currentHealth;
    private bool isDead = false;
    private CharacterController charController;
    private bool isAttacking = false;
    private Coroutine activeKnockbackRoutine;

    protected override void Awake()
    {
        base.Awake();
        charController = GetComponent<CharacterController>();
        currentHealth = maxHealth;
    }

    // Standard Unity callback matching UltraInstinctCapsule pattern
    protected void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        isAttacking = false;
        activeKnockbackRoutine = null;
        if (charController != null) charController.enabled = true;
    }

    protected override void Update()
    {
        if (isDead || target == null) return;

        base.Update(); // Keeps base enemy brain state machine ticking

        // If we are in the attack state, handle lunge and damage application
        if (currentState == AIState.Attack && !isAttacking)
        {
            StartCoroutine(ExecuteMinionAttackRoutine());
        }
    }

    protected override void HandleRequestTokenState(float distanceToTarget)
    {
        if (MinionData == null || MinionData.AvailableAttacks == null || MinionData.AvailableAttacks.Count == 0) 
            return;

        // Select a random attack from the minion's arsenal
        selectedAttack = MinionData.AvailableAttacks[Random.Range(0, MinionData.AvailableAttacks.Count)];

        if (GlobalTokenManager.Instance != null)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
            if (hasToken)
            {
                currentState = AIState.Attack;
                stateTimer = selectedAttack.StartupTime + selectedAttack.ActiveTime + selectedAttack.RecoveryTime;
            }
            else
            {
                // Tactical pack circling behavior when melee tokens are fully saturated
                Vector3 tangent = Vector3.Cross(Vector3.up, (target.position - transform.position)).normalized;
                if (charController != null && charController.enabled)
                {
                    charController.Move(tangent * (MinionData.MoveSpeed * 0.7f) * Time.deltaTime);
                }

                // Look toward target while circling
                Vector3 dir = (target.position - transform.position).normalized;
                dir.y = 0f;
                if (dir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, MinionData.RotationSpeed * Time.deltaTime);
                }
            }
        }
        else
        {
            currentState = AIState.Attack;
        }
    }

    private IEnumerator ExecuteMinionAttackRoutine()
    {
        isAttacking = true;
        
        if (selectedAttack == null) 
        {
            isAttacking = false;
            yield break;
        }

        // 1. Startup / Wind-up phase
        float startup = selectedAttack.StartupTime;
        float elapsed = 0f;
        while (elapsed < startup)
        {
            if (isDead || target == null) yield break;
            elapsed += Time.deltaTime;
            FaceTarget();
            yield return null;
        }

        // 2. Active phase: Lunge forward and deal damage if close enough
        float activeTime = selectedAttack.ActiveTime;
        elapsed = 0f;
        bool damageDealt = false;

        while (elapsed < activeTime)
        {
            if (isDead || target == null) yield break;
            elapsed += Time.deltaTime;

            FaceTarget();

            // Move forward during the lunge
            if (charController != null && charController.enabled)
            {
                Vector3 lungeMove = transform.forward * (MinionData.MoveSpeed * 1.5f) * Time.deltaTime;
                charController.Move(lungeMove);
            }

            // Check if close enough to hit the player during active frames
            if (!damageDealt && target != null)
            {
                float distToTarget = Vector3.Distance(transform.position, target.position);
                if (distToTarget <= 2.0f) // Attack contact range
                {
                    DealDamageToPlayer();
                    damageDealt = true;
                }
            }

            yield return null;
        }

        // 3. Recovery phase
        float recovery = selectedAttack.RecoveryTime;
        yield return new WaitForSeconds(recovery);

        // Release token if held
        if (GlobalTokenManager.Instance != null && hasToken && selectedAttack != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(transform, selectedAttack.RequiredTokenType);
            hasToken = false;
        }

        isAttacking = false;
        currentState = AIState.Chase;
    }

    private void FaceTarget()
    {
        if (target == null) return;
        Vector3 dir = (target.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, MinionData.RotationSpeed * Time.deltaTime);
        }
    }

    private void DealDamageToPlayer()
    {
        if (target != null && target.TryGetComponent<IDamageable>(out var playerDamageable))
        {
            Vector3 hitDir = (target.position - transform.position).normalized;
            hitDir.y = 0f;
            float dmgAmount = selectedAttack != null ? selectedAttack.DamageAmount : 15f;
            float knockback = selectedAttack != null ? selectedAttack.KnockbackForce : 2f;
            AudioClip sound = selectedAttack != null ? selectedAttack.HitSound : null;

            playerDamageable.TakeDamage(dmgAmount, target.position, hitDir, knockback, sound, -1, false);
        }
    }

    // --- IDAMAGEABLE IMPLEMENTATION (Getting hit by the player) ---
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        currentHealth -= damage;

        // Interrupt attack if hit
        if (isAttacking)
        {
            StopAllCoroutines();
            isAttacking = false;
        }

        // Apply knockback
        if (activeKnockbackRoutine != null)
        {
            StopCoroutine(activeKnockbackRoutine);
        }
        if (charController != null && charController.enabled)
        {
            activeKnockbackRoutine = StartCoroutine(ApplyKnockback(hitDirection, force));
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private IEnumerator ApplyKnockback(Vector3 hitDirection, float force)
    {
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -transform.forward;
        pushDir.Normalize();

        float elapsed = 0f;
        float duration = 0.12f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (charController != null && charController.enabled)
            {
                charController.Move(pushDir * (force * Time.deltaTime / duration));
            }
            yield return null;
        }
        activeKnockbackRoutine = null;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        StopAllCoroutines();

        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);
        }

        if (charController != null) charController.enabled = false;

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    // --- IHEALABLE IMPLEMENTATION (Directly communicating with HealerController) ---
    public bool NeedsHealing()
    {
        return currentHealth < maxHealth && !isDead;
    }

    public void ReceiveHeal(float amount)
    {
        if (isDead) return;
        // Float addition ensures fractional healing per frame accumulates smoothly
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public Transform GetTransform()
    {
        return transform;
    }
}