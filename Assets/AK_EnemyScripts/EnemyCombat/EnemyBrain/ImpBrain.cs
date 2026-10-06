using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random; 
using CombatSystem.Animation;
using CombatSystem.Data;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class ImpBrain : MonoBehaviour, IDamageable, IHealable
{
    public enum ImpState { Strafing, Charging, Firing, PanicLeap, Reacting }

    [Header("Imp Stats & Combat")]
    public float maxHealth = 40f; 
    public float deathDisappearDelay = 2.0f;
    private float currentHealth;
    private bool isDead = false;
    private CharacterController charController;
    private Collider capsuleCollider; 

    [Header("Core References & Animation Profiles")]
    public PlayerController player;
    public EnemyAnimProfile animProfile;
    private EnemyAnimationEngine animationEngine;
    private Transform cachedTransform;
    private Transform playerTransform;

    [Header("Enemy Type & Token Settings")]
    public TokenType tokenType = TokenType.Ranged;

    [Header("Aggression & Cooldowns")]
    public float baseAttackCooldown = 2.5f;
    private float currentAttackCooldown = 0f;

    [Header("Separation & Spread Tuning")]
    [Tooltip("Minimum personal space distance Imps try to maintain from each other.")]
    public float separationRadius = 2.0f;
    [Tooltip("How strongly they push away from crowded allies.")]
    public float separationWeight = 2.5f;
    private static readonly Collider[] overlapBuffer = new Collider[16];

    [Header("Visual Hump (Back Sphere) References")]
    public Transform humpSphereTransform;
    private Material humpMaterial;
    private float currentChargeProgress = 0f;

    [Header("Attack & Charging Tuning")]
    public GameObject fireballPrefab;
    public Transform firePoint;
    public AnimationClip rangedAttackClip; 
    public float preferredRange = 10f;
    public float minimumSafeDistance = 6.0f;
    public float chargeDuration = 2.0f; 
    public float fireballDamage = 20f;
    public float rotationSpeed = 20f;

    [Header("Player Reaction Hook")]
    public string playerReactionAnimName = "Hit_Light";
    public float stunDuration = 0.2f;

    [Header("Debug")]
    public bool debugFiring = true;
    private float nextDebugTime;
    private Collider[] ownColliders;

    private void FireLog(string msg)
        {
            if (!debugFiring || Time.time < nextDebugTime) return;
            nextDebugTime = Time.time + 2f;
            Debug.Log($"[Imp:{name}] {msg}", this);
        }

    [Header("Animation Controls")]
    public float attackAnimationSpeed = 1.0f;
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
    private Coroutine activeKnockbackRoutine;
    
    private string lastPlayedLocomotionState = "";
    private bool isReacting = false;
    private float reactionTimer = 0f;
    private float fallVelocity = 0f;

    public event Action OnDeath;
    public event Action OnRevive;

    public void ForceDisableUntilGrounded() { enabled = false; }

    public void AuthorizeGroundContactAndEnable()
    {
        enabled = true;
        lastPlayedLocomotionState = "";
    }

    private void Awake()
    {

        ownColliders = GetComponentsInChildren<Collider>(true);
        cachedTransform = transform;
        charController = GetComponent<CharacterController>();
        capsuleCollider = GetComponent<Collider>(); 
        animationEngine = GetComponent<EnemyAnimationEngine>();

        if (animProfile != null) animProfile.InitializeDictionary();

        if (humpSphereTransform != null)
        {
            Renderer rend = humpSphereTransform.GetComponent<Renderer>();
            if (rend != null) humpMaterial = rend.material; 
        }
        
        FindPlayerReference();
    }

    private void FindPlayerReference()
    {
        if (player == null) player = FindObjectOfType<PlayerController>();
        if (player != null) playerTransform = player.transform;
    }

    private void Start() => FindPlayerReference();

    private void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        holdsToken = false;
        isReacting = false;
        impState = ImpState.Strafing;
        currentChargeProgress = 0f;
        lastPlayedLocomotionState = "";
        activeActionRoutine = null;
        activeKnockbackRoutine = null;
        fallVelocity = -2f; 

        currentAttackCooldown = Random.Range(0.5f, 2.0f);
        if (capsuleCollider != null) capsuleCollider.enabled = true;

        UpdateHumpVisuals(0f);
        FindPlayerReference();
        OnRevive?.Invoke();
    }

    private void OnDisable()
    {
        currentHealth = maxHealth;
        isDead = false;
        isReacting = false;
        ReleaseTokenSafely();
        if (charController != null) charController.enabled = true;
    }

    private void OnDestroy() => ReleaseTokenSafely();

    private void Update()
    {
        if (player == null || playerTransform == null)
        {
            FindPlayerReference();
            if (player == null) return;
        }

        if (isDead) return;

        if (currentAttackCooldown > 0f) currentAttackCooldown -= Time.deltaTime;

        if (charController != null && charController.enabled)
        {
            if (charController.isGrounded) fallVelocity = -0.5f;
            else fallVelocity -= 20f * Time.deltaTime;
        }

        if (isReacting)
        {
            reactionTimer -= Time.deltaTime;
            if (charController != null && charController.enabled)
            {
                charController.Move(new Vector3(0, fallVelocity, 0) * Time.deltaTime);
            }
            if (reactionTimer <= 0f) isReacting = false;
            return;
        }

        float distToTarget = Vector3.Distance(cachedTransform.position, playerTransform.position);

        if (impState != ImpState.PanicLeap && impState != ImpState.Reacting && activeKnockbackRoutine == null)
        {
            FaceTarget();
        }

        switch (impState)
        {
            case ImpState.Strafing:
                if (activeKnockbackRoutine == null) HandlePureRangedMovement(distToTarget);
                TryInitiateAttackToken(distToTarget);
                break;
            case ImpState.Charging:
                if (charController != null && charController.enabled)
                {
                    charController.Move(new Vector3(0, fallVelocity, 0) * Time.deltaTime);
                }
                UpdateLocomotionAnimation("Charging", animProfile != null ? animProfile.idleClip : null, 0.1f);
                break;
                
                case ImpState.Firing:
    if (charController != null && charController.enabled)
    {
        charController.Move(new Vector3(0, fallVelocity, 0) * Time.deltaTime);
    }

    break;

        }
    }

   

    private Vector3 CalculateSeparationForce()
    {
        Vector3 separationMove = Vector3.zero;
        Vector3 currentPos = cachedTransform.position;
        int hitCount = Physics.OverlapSphereNonAlloc(currentPos, separationRadius, overlapBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit != null && hit.gameObject != gameObject && hit.TryGetComponent<ImpBrain>(out _))
            {
                Vector3 awayDir = currentPos - hit.transform.position;
                awayDir.y = 0f;
                float distanceSqr = awayDir.sqrMagnitude;

                if (distanceSqr > 0.0001f)
                {
                    separationMove += (awayDir.normalized / Mathf.Sqrt(distanceSqr)) * separationWeight;
                }
            }
        }
        return separationMove;
    }

    private void UpdateLocomotionAnimation(string stateKey, AnimationClip clip, float duration, float speedMultiplier = 1.0f)
    {
        if (animationEngine == null || animProfile == null || isDead || isReacting) return;
        
        if (lastPlayedLocomotionState != stateKey)
        {
            lastPlayedLocomotionState = stateKey;
            if (clip != null) animationEngine.PlayAnimation(clip, duration, speedMultiplier);
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

        Vector3 dirToTarget = (playerTransform.position - cachedTransform.position).normalized;
        dirToTarget.y = 0f;
        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToTarget).normalized;
        Vector3 separationForce = CalculateSeparationForce();
        Vector3 moveDir = Vector3.zero;

        if (distToTarget < minimumSafeDistance) moveDir = -dirToTarget + separationForce;
        else if (distToTarget > preferredRange) moveDir = dirToTarget + separationForce;
        else moveDir = (rightDir * strafeDir) + (dirToTarget * 0.15f) + separationForce;

        moveDir.Normalize();

        if (charController != null && charController.enabled)
        {
            charController.Move((moveDir * impMoveSpeed + new Vector3(0, fallVelocity, 0)) * Time.deltaTime);
        }

        if (animProfile != null)
        {
            AnimationClip moveClip = distToTarget < minimumSafeDistance ? animProfile.walkClip : (strafeDir > 0 ? animProfile.strafeRightClip : animProfile.strafeLeftClip);
            if (moveClip == null) moveClip = animProfile.walkClip;
            
            string moveKey = distToTarget < minimumSafeDistance ? "Backpedal" : (strafeDir > 0 ? "StrafeRight" : "StrafeLeft");
            UpdateLocomotionAnimation(moveKey, moveClip, animProfile.walkTransitionDuration);
        }
    }

    private void TryInitiateAttackToken(float distToTarget)
    {
        if (holdsToken || currentAttackCooldown > 0f || distToTarget > preferredRange + 4f) return;

        if (GlobalTokenManager.Instance != null)
        {
            if (GlobalTokenManager.Instance.RequestToken(cachedTransform, tokenType))
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

    private void EndAttack()
{
    currentChargeProgress = 0f;
    UpdateHumpVisuals(0f);
    ReleaseTokenSafely();
    activeActionRoutine = null;
    if (!isDead) { impState = ImpState.Strafing; strafeTimer = 0f; }
}

    private IEnumerator ChargeAndFireRoutine()
{
    float elapsed = 0f;
    currentChargeProgress = 0f;

    while (elapsed < chargeDuration)
    {
        if (isDead || playerTransform == null || isReacting) { EndAttack(); yield break; }
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

    yield return new WaitForSeconds(clipDuration * 0.4f);
    if (!isDead && !isReacting) SpawnFireball();

    currentChargeProgress = 0f;
    UpdateHumpVisuals(0f);

    yield return new WaitForSeconds(clipDuration * 0.6f);

    EndAttack();
    currentAttackCooldown = baseAttackCooldown + Random.Range(-0.5f, 0.5f);
}
    private void SpawnFireball()
{
    if (fireballPrefab == null)
    {
        Debug.LogError($"[Imp:{name}] fireballPrefab is not assigned on the Imp prefab.", this);
        return;
    }
    if (playerTransform == null) return;

    Vector3 spawnPos = firePoint != null ? firePoint.position : cachedTransform.position + Vector3.up * 1.5f + (cachedTransform.forward * 2.0f);
    Vector3 targetPos = playerTransform.position + Vector3.up * 1.0f;
    Vector3 dirToTarget = (targetPos - spawnPos).normalized;

    GameObject fbObj = Instantiate(fireballPrefab, spawnPos, Quaternion.LookRotation(dirToTarget));

    foreach (var projCol in fbObj.GetComponentsInChildren<Collider>())
        foreach (var own in ownColliders)
            if (own != null) Physics.IgnoreCollision(projCol, own);

    if (fbObj.TryGetComponent<ImpFireball>(out var fireball))
        fireball.Initialize(cachedTransform, playerTransform, fireballDamage, playerReactionAnimName, stunDuration);
    else
        Debug.LogError($"[Imp:{name}] fireballPrefab has no ImpFireball component on its root.", fbObj);

    if (debugFiring) Debug.Log($"[Imp:{name}] fireball spawned", this);
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
        if (playerTransform == null) return;
        Vector3 dir = (playerTransform.position - cachedTransform.position);
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

        if (activeActionRoutine != null) { StopCoroutine(activeActionRoutine); activeActionRoutine = null; }
        if (activeKnockbackRoutine != null) StopCoroutine(activeKnockbackRoutine);

        ReleaseTokenSafely();
        currentChargeProgress = 0f;
        UpdateHumpVisuals(0f);
        PlayHitReaction(attackID, hitDirection);

        if (currentHealth <= 0) Die();
        else
        {
            impState = ImpState.PanicLeap;
            activeKnockbackRoutine = StartCoroutine(PanicLeapRoutine(hitDirection));
        }
    }

    private void PlayHitReaction(int attackID, Vector3 hitDirection)
    {
        if (animationEngine == null) return;

        if (hitReactionClip != null)
        {
            animationEngine.PlayAnimation(hitReactionClip, 0.05f, 1.0f);
            isReacting = true;
            reactionTimer = hitReactionClip.length;
            lastPlayedLocomotionState = "Reaction";
            return;
        }

        if (animProfile == null) return;
        AttackReactionData reactionData = animProfile.GetReaction(attackID);
        if (hitDirection == Vector3.zero) hitDirection = -cachedTransform.forward;
        Vector3 localDir = cachedTransform.InverseTransformDirection(hitDirection.normalized);
        bool isZAxis = Mathf.Abs(localDir.z) > Mathf.Abs(localDir.x);

        HitAnimationData hitAnim = null;
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
            isReacting = true;
            reactionTimer = hitAnim.clip.length / Mathf.Max(0.01f, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction";
        }
    }

    private IEnumerator PanicLeapRoutine(Vector3 hitDirection)
    {
        Vector3 leapDir = -hitDirection;
        leapDir.y = 0f;
        if (leapDir == Vector3.zero) leapDir = -cachedTransform.forward;
        leapDir.Normalize();

        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            if (isDead) yield break;
            elapsed += Time.deltaTime;
            if (charController != null && charController.enabled)
            {
                charController.Move((leapDir * panicLeapSpeed + new Vector3(0, fallVelocity, 0)) * Time.deltaTime);
            }
            yield return null;
        }

        impState = ImpState.Strafing;
        strafeTimer = 0f;
        activeKnockbackRoutine = null;
    }

    private void ReleaseTokenSafely()
    {
        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        OnDeath?.Invoke();

        if (activeActionRoutine != null) StopCoroutine(activeActionRoutine);
        if (activeKnockbackRoutine != null) StopCoroutine(activeKnockbackRoutine);
        ReleaseTokenSafely();
        isReacting = false;

        if (charController != null) charController.enabled = false;
        if (capsuleCollider != null) capsuleCollider.enabled = false;

        if (animProfile != null && animProfile.deathClip != null && animationEngine != null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(deathDisappearDelay);

        BeaconSpawnerManager spawnerManager = FindObjectOfType<BeaconSpawnerManager>();
        if (spawnerManager != null) spawnerManager.RegisterEnemyDefeated(cachedTransform.gameObject);
        else if (EnemyObjectPool.Instance != null) EnemyObjectPool.Instance.ReturnToPool(cachedTransform.gameObject);
        else gameObject.SetActive(false);
    }

    public bool NeedsHealing() => currentHealth < maxHealth && !isDead;
    public void ReceiveHeal(float amount) { currentHealth = Mathf.Min(currentHealth + amount, maxHealth); }
    public Transform GetTransform() => cachedTransform;
}