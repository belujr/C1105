using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Animation;
using CombatSystem.Data;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class ImpBrain : BaseEnemyBrain, IDamageable, IHealable
{
    public enum ImpState { Strafing, Charging, Firing, PanicLeap, Reacting }

    [Header("Imp Stats & Combat")]
    public float maxHealth = 40f; 
    private float currentHealth;
    private bool isDead = false;
    private CharacterController charController;

    [Header("Core References & Animation Profiles")]
    public EnemyAnimProfile animProfile;
    private EnemyAnimationEngine animationEngine;
    private Transform cachedTransform;

    [Header("Visual Hump (Back Sphere) References")]
    [Tooltip("Assign the transparent sphere GameObject attached to the Imp's back.")]
    public Transform humpSphereTransform;
    private Material humpMaterial;
    private float currentChargeProgress = 0f;

    [Header("Attack & Charging Tuning")]
    public GameObject fireballPrefab;
    public Transform firePoint;
    public AnimationClip rangedAttackClip; 
    public float preferredRange = 10f;
    [Tooltip("If the player steps closer than this distance, the Imp aggressively backpedals to stay ranged-only.")]
    public float minimumSafeDistance = 6.0f;
    public float chargeDuration = 2.0f; 
    public float fireballDamage = 20f;
    public float rotationSpeed = 20f;

    [Header("Player Reaction Hook")]
    [Tooltip("The exact name of the Player's Animator node to play when hit.")]
    public string playerReactionAnimName = "Hit_Light";
    public float stunDuration = 0.2f;

    [Header("Animation Controls")]
    [Tooltip("Multiplier for the attack animation speed.")]
    public float attackAnimationSpeed = 1.0f;
    [Tooltip("Direct slot for the Imp's hit reaction clip. Bypasses profile lookup if assigned.")]
    public AnimationClip hitReactionClip;

    [Header("Movement & Panic Leap")]
    public float impMoveSpeed = 5.5f; 
    public float panicLeapDistance = 4.0f;
    public float panicLeapSpeed = 9.0f;

    private ImpState impState = ImpState.Strafing;
    private float strafeTimer = 0f;
    private int strafeDir = 1;
    private bool holdsToken = false;
    private Coroutine activeActionRoutine;
    private string lastPlayedLocomotionState = "";

    protected override void Awake()
    {
        base.Awake();
        cachedTransform = transform;
        charController = GetComponent<CharacterController>();
        animationEngine = GetComponent<EnemyAnimationEngine>();
        currentHealth = maxHealth;

        if (animProfile != null) animProfile.InitializeDictionary();

        if (humpSphereTransform != null)
        {
            Renderer rend = humpSphereTransform.GetComponent<Renderer>();
            if (rend != null) humpMaterial = rend.material; 
        }
    }

    protected void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        holdsToken = false;
        impState = ImpState.Strafing;
        currentChargeProgress = 0f;
        lastPlayedLocomotionState = "";
        if (charController != null) charController.enabled = true;
        UpdateHumpVisuals(0f);
    }

    protected override void Update()
    {
        if (isDead || target == null) return;

        base.Update(); 

        float distToTarget = Vector3.Distance(cachedTransform.position, target.position);

        if (impState != ImpState.PanicLeap && impState != ImpState.Reacting)
        {
            FaceTarget();
        }

        switch (impState)
        {
            case ImpState.Strafing:
                HandlePureRangedMovement(distToTarget);
                TryInitiateAttackToken();
                break;

            case ImpState.Charging:
                PlayLocomotionAnimation("Charging", animProfile != null ? animProfile.idleClip : null, 0.1f);
                break;

            case ImpState.Firing:
                break;

            case ImpState.PanicLeap:
                break;

            case ImpState.Reacting:
                break;
        }
    }

    private void PlayLocomotionAnimation(string stateKey, AnimationClip clip, float duration, float speedMultiplier = 1.0f)
    {
        if (animationEngine == null || animProfile == null || isDead) return;
        
        if (lastPlayedLocomotionState != stateKey)
        {
            lastPlayedLocomotionState = stateKey;
            if (clip != null)
            {
                animationEngine.PlayAnimation(clip, duration, speedMultiplier);
            }
        }
    }

    private void HandlePureRangedMovement(float distToTarget)
    {
        strafeTimer -= Time.deltaTime;
        if (strafeTimer <= 0f)
        {
            strafeTimer = Random.Range(1.5f, 3.0f);
            strafeDir = Random.value > 0.5f ? 1 : -1;
        }

        Vector3 dirToTarget = (target.position - cachedTransform.position).normalized;
        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToTarget).normalized;
        Vector3 moveDir = Vector3.zero;

        if (distToTarget < minimumSafeDistance)
        {
            moveDir = -dirToTarget; 
        }
        else
        {
            moveDir = (rightDir * strafeDir);
            if (distToTarget < preferredRange - 2f) moveDir -= dirToTarget;
            else if (distToTarget > preferredRange + 2f) moveDir += dirToTarget;
        }

        moveDir.Normalize();

        if (charController != null && charController.enabled)
        {
            charController.Move(moveDir * impMoveSpeed * Time.deltaTime);
        }

        if (animProfile != null)
        {
            AnimationClip moveClip = distToTarget < minimumSafeDistance ? animProfile.walkClip : (strafeDir > 0 ? animProfile.strafeRightClip : animProfile.strafeLeftClip);
            if (moveClip == null) moveClip = animProfile.walkClip;
            string moveKey = distToTarget < minimumSafeDistance ? "Backpedal" : (strafeDir > 0 ? "StrafeRight" : "StrafeLeft");
            PlayLocomotionAnimation(moveKey, moveClip, animProfile.walkTransitionDuration);
        }
    }

    private void TryInitiateAttackToken()
    {
        if (holdsToken) return;

        if (GlobalTokenManager.Instance != null)
        {
            if (GlobalTokenManager.Instance.RequestToken(cachedTransform, TokenType.Ranged))
            {
                holdsToken = true;
                impState = ImpState.Charging;
                if (activeActionRoutine != null) StopCoroutine(activeActionRoutine);
                activeActionRoutine = StartCoroutine(ChargeAndFireRoutine());
            }
        }
        else
        {
            impState = ImpState.Charging;
            if (activeActionRoutine != null) StopCoroutine(activeActionRoutine);
            activeActionRoutine = StartCoroutine(ChargeAndFireRoutine());
        }
    }

    private IEnumerator ChargeAndFireRoutine()
    {
        float elapsed = 0f;
        currentChargeProgress = 0f;

        while (elapsed < chargeDuration)
        {
            if (isDead || target == null) yield break;

            elapsed += Time.deltaTime;
            currentChargeProgress = Mathf.Clamp01(elapsed / chargeDuration);
            UpdateHumpVisuals(currentChargeProgress);

            yield return null;
        }

        impState = ImpState.Firing;
        
        AnimationClip shootClip = rangedAttackClip != null ? rangedAttackClip : (animProfile != null ? animProfile.GetAnimationClip("RangedAttack") : null);
        float speedMultiplier = Mathf.Max(0.01f, attackAnimationSpeed);
        float clipDuration = shootClip != null ? (shootClip.length / speedMultiplier) : 1.0f;

        if (animationEngine != null && shootClip != null)
        {
            animationEngine.PlayAnimation(shootClip, 0.05f, speedMultiplier);
            lastPlayedLocomotionState = "RangedAttack";
        }

        float throwDelay = clipDuration * 0.4f;
        yield return new WaitForSeconds(throwDelay);

        SpawnFireball();
        
        currentChargeProgress = 0f;
        UpdateHumpVisuals(0f);

        yield return new WaitForSeconds(clipDuration - throwDelay); 

        ReleaseTokenSafely();
        impState = ImpState.Strafing;
        strafeTimer = 0f; 
    }

    private void SpawnFireball()
    {
        if (fireballPrefab == null || target == null) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : cachedTransform.position + Vector3.up + cachedTransform.forward;
        Vector3 dirToTarget = (target.position - spawnPos).normalized;

        GameObject fbObj = Instantiate(fireballPrefab, spawnPos, Quaternion.LookRotation(dirToTarget));
        if (fbObj.TryGetComponent<ImpFireball>(out var fireball))
        {
            // NEW: Pass the string and stun duration down into the fireball!
            fireball.Initialize(cachedTransform, target, fireballDamage, playerReactionAnimName, stunDuration);
        }
    }

    private void UpdateHumpVisuals(float progress)
    {
        if (humpSphereTransform == null) return;

        float scale = Mathf.Lerp(0.15f, 1.0f, progress);
        humpSphereTransform.localScale = new Vector3(scale, scale, scale);

        if (humpMaterial != null)
        {
            Color col = humpMaterial.color;
            col.a = Mathf.Lerp(0.2f, 0.9f, progress); 
            humpMaterial.color = col;
        }
    }

    private void FaceTarget()
    {
        if (target == null) return;
        Vector3 dir = (target.position - cachedTransform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        currentHealth -= damage;

        if (activeActionRoutine != null)
        {
            StopCoroutine(activeActionRoutine);
            activeActionRoutine = null;
        }

        ReleaseTokenSafely();
        currentChargeProgress = 0f;
        UpdateHumpVisuals(0f);

        PlayHitReaction(attackID, hitDirection);

        if (currentHealth <= 0)
        {
            Die();
        }
        else
        {
            impState = ImpState.PanicLeap;
            StartCoroutine(PanicLeapRoutine(hitDirection));
        }
    }

    private void PlayHitReaction(int attackID, Vector3 hitDirection)
    {
        if (animationEngine == null) return;

        if (hitReactionClip != null)
        {
            animationEngine.PlayAnimation(hitReactionClip, 0.05f, 1.0f);
            lastPlayedLocomotionState = "Reaction";
            return;
        }

        if (animProfile == null) return;

        AttackReactionData reactionData = animProfile.GetReaction(attackID);
        HitAnimationData hitAnim = null;

        if (hitDirection == Vector3.zero) hitDirection = -cachedTransform.forward;
        Vector3 localDir = cachedTransform.InverseTransformDirection(hitDirection.normalized);
        bool isZAxis = Mathf.Abs(localDir.z) > Mathf.Abs(localDir.x);

        if (reactionData != null)
        {
            hitAnim = isZAxis ? (localDir.z > 0 ? reactionData.reactionFront : reactionData.reactionBack)
                              : (localDir.x > 0 ? reactionData.reactionRight : reactionData.reactionLeft);
        }

        if (hitAnim == null || hitAnim.clip == null)
        {
            hitAnim = isZAxis ? (localDir.z > 0 ? animProfile.defaultHitFront : animProfile.defaultHitBack)
                              : (localDir.x > 0 ? animProfile.defaultHitRight : animProfile.defaultHitLeft);
        }

        if (hitAnim != null && hitAnim.clip != null)
        {
            animationEngine.PlayAnimation(hitAnim.clip, hitAnim.transitionDuration, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction";
        }
    }

    private IEnumerator PanicLeapRoutine(Vector3 hitDirection)
    {
        impState = ImpState.Reacting;

        Vector3 leapDir = -hitDirection;
        leapDir.y = 0f;
        if (leapDir == Vector3.zero) leapDir = -cachedTransform.forward;
        leapDir.Normalize();

        float leapDuration = 0.35f;
        float elapsed = 0f;

        while (elapsed < leapDuration)
        {
            if (isDead) yield break;
            elapsed += Time.deltaTime;

            if (charController != null && charController.enabled)
            {
                charController.Move(leapDir * panicLeapSpeed * Time.deltaTime);
            }
            yield return null;
        }

        impState = ImpState.Strafing;
        strafeTimer = 0f;
    }

    private void ReleaseTokenSafely()
    {
        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.Ranged);
            holdsToken = false;
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (activeActionRoutine != null) StopCoroutine(activeActionRoutine);
        ReleaseTokenSafely();

        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);
        }

        if (charController != null) charController.enabled = false;

        if (animProfile != null && animProfile.deathClip != null && animationEngine != null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(1.0f);

        // Fixed lookup avoiding BeaconSpawnerManager.Instance
        BeaconSpawnerManager spawnerManager = FindObjectOfType<BeaconSpawnerManager>();
        if (spawnerManager != null)
        {
            spawnerManager.RegisterEnemyDefeated(cachedTransform.gameObject);
        }
        else if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(cachedTransform.gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        ReleaseTokenSafely();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, preferredRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minimumSafeDistance);
    }

    // --- IHEALABLE IMPLEMENTATION ---
    public bool NeedsHealing()
    {
        return currentHealth < maxHealth && !isDead;
    }

    public void ReceiveHeal(float amount)
    {
        if (isDead) return;
        currentHealth += amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
    }

    public Transform GetTransform()
    {
        return cachedTransform;
    }
}