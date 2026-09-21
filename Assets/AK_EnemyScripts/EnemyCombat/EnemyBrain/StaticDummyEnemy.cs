using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Animation;
using CombatSystem.Data;

[RequireComponent(typeof(EnemyAnimationEngine))]
public class StaticDummyEnemy : MonoBehaviour, IDamageable, IHealable
{
    private enum HitDirection { Front, Back, Left, Right }

    [Header("Core References & Animation Profiles")]
    public EnemyAnimProfile animProfile;
    private EnemyAnimationEngine animationEngine;
    private Collider capsuleCollider;
    private Transform cachedTransform;

    [Header("Health & Vulnerability")]
    public int maxHealth = 100;
    [Tooltip("Time in seconds to wait after death before vanishing or returning to pool.")]
    public float deathDisappearDelay = 2.0f;
    private int currentHealth;

    private Coroutine activeKnockbackRoutine;
    private float groundYCoord;
    private bool isDead = false;
    private string lastPlayedLocomotionState = "";

    private void Awake()
    {
        cachedTransform = transform;
        animationEngine = GetComponent<EnemyAnimationEngine>();
        capsuleCollider = GetComponent<Collider>();
        groundYCoord = cachedTransform.position.y;

        if (animProfile != null) animProfile.InitializeDictionary();
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        activeKnockbackRoutine = null;
        lastPlayedLocomotionState = "";

        if (cachedTransform == null) cachedTransform = transform;
        if (capsuleCollider != null) capsuleCollider.enabled = true;

        // Ensure they start in their idle animation
        if (animationEngine != null && animProfile != null && animProfile.idleClip != null)
        {
            animationEngine.PlayAnimation(animProfile.idleClip, 0.1f);
            lastPlayedLocomotionState = "Idle";
        }
    }

    private void Update()
    {
        // This enemy does absolutely nothing on its own.
        // It waits to receive events via the IDamageable interface.
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        // Briefly reset time scale just in case hit-stop was active
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        int finalDamage = Mathf.RoundToInt(damage);
        currentHealth -= finalDamage;

        PlayHitReaction(attackID, hitDirection, finalDamage);

        if (activeKnockbackRoutine != null)
        {
            StopCoroutine(activeKnockbackRoutine);
        }
        activeKnockbackRoutine = StartCoroutine(PerformKnockbackRoutine(hitDirection, force));

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void PlayHitReaction(int attackID, Vector3 hitDirection, int damageValue)
    {
        if (animProfile == null || animationEngine == null) return;

        AttackReactionData reactionData = animProfile.GetReaction(attackID);
        if (reactionData == null) reactionData = animProfile.GetReaction(damageValue);
        if (reactionData == null && animProfile.attackReactions != null && animProfile.attackReactions.Count > 0)
        {
            reactionData = animProfile.attackReactions[0];
        }

        HitAnimationData hitAnim = null;

        if (hitDirection == Vector3.zero) hitDirection = -cachedTransform.forward;
        Vector3 localDir = cachedTransform.InverseTransformDirection(hitDirection.normalized);

        HitDirection direction = HitDirection.Front;
        if (Mathf.Abs(localDir.z) > Mathf.Abs(localDir.x))
        {
            direction = localDir.z > 0 ? HitDirection.Front : HitDirection.Back;
        }
        else
        {
            direction = localDir.x > 0 ? HitDirection.Right : HitDirection.Left;
        }

        if (reactionData != null)
        {
            switch (direction)
            {
                case HitDirection.Front: hitAnim = reactionData.reactionFront; break;
                case HitDirection.Back: hitAnim = reactionData.reactionBack; break;
                case HitDirection.Left: hitAnim = reactionData.reactionLeft; break;
                case HitDirection.Right: hitAnim = reactionData.reactionRight; break;
            }
        }

        if (hitAnim == null || hitAnim.clip == null)
        {
            switch (direction)
            {
                case HitDirection.Front: hitAnim = animProfile.defaultHitFront; break;
                case HitDirection.Back: hitAnim = animProfile.defaultHitBack; break;
                case HitDirection.Left: hitAnim = animProfile.defaultHitLeft; break;
                case HitDirection.Right: hitAnim = animProfile.defaultHitRight; break;
            }
        }

        if (hitAnim != null && hitAnim.clip != null)
        {
            animationEngine.PlayAnimation(hitAnim.clip, hitAnim.transitionDuration, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction";
        }
    }

    private IEnumerator PerformKnockbackRoutine(Vector3 hitDirection, float knockbackForce)
    {
        Vector3 startPos = cachedTransform.position;
        float slideDistance = knockbackForce > 0f ? knockbackForce : 1.5f;

        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -cachedTransform.forward;
        pushDir.Normalize();

        Vector3 targetPos = startPos + (pushDir * slideDistance);

        float duration = 0.12f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            // Smooth slide
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);
            cachedTransform.position = Vector3.Lerp(startPos, targetPos, smoothProgress);

            yield return null;
        }

        cachedTransform.position = targetPos;
        activeKnockbackRoutine = null;

        // Return to idle animation after knockback finishes if not dead
        if (!isDead && animationEngine != null && animProfile != null && animProfile.idleClip != null)
        {
            animationEngine.PlayAnimation(animProfile.idleClip, 0.2f);
            lastPlayedLocomotionState = "Idle";
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (activeKnockbackRoutine != null)
        {
            StopCoroutine(activeKnockbackRoutine);
            activeKnockbackRoutine = null;
        }

        if (capsuleCollider != null) capsuleCollider.enabled = false;

        if (animProfile != null && animProfile.deathClip != null && animationEngine != null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        Vector3 startPos = cachedTransform.position;
        Vector3 groundPos = startPos;

        if (Physics.Raycast(startPos + (Vector3.up * 0.5f), Vector3.down, out RaycastHit hit, 10f))
        {
            groundPos.y = hit.point.y;
        }
        else
        {
            groundPos.y = groundYCoord;
        }

        float dropElapsed = 0f;
        float dropDuration = 0.2f;

        while (dropElapsed < dropDuration)
        {
            dropElapsed += Time.unscaledDeltaTime;
            cachedTransform.position = Vector3.Lerp(startPos, groundPos, dropElapsed / dropDuration);
            yield return null;
        }
        cachedTransform.position = groundPos;

        yield return new WaitForSecondsRealtime(deathDisappearDelay);

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(cachedTransform.gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    // --- IHEALABLE IMPLEMENTATION ---
    public bool NeedsHealing()
    {
        return currentHealth < maxHealth && !isDead;
    }

    public void ReceiveHeal(float amount)
    {
        if (isDead) return;
        currentHealth += Mathf.RoundToInt(amount);
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }

    public Transform GetTransform()
    {
        return cachedTransform;
    }
}