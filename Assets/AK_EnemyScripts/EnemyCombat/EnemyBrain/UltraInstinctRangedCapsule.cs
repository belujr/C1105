using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Animation;
using CombatSystem.Data;

[RequireComponent(typeof(EnemyAnimationEngine))]
public class UltraInstinctRangedCapsule : MonoBehaviour, IDamageable
{
    public enum DodgeDirection { DodgeLeft, DodgeRight, Jump, Backstep }
    public enum RangedBehaviorState { SquareStrafing, WaitToShoot, Firing, WaitingForProjectileOutcome, PanicRetreat }

    [System.Serializable]
    public struct DodgeMapping
    {
        public string attackName; 
        public int attackID;
        public DodgeDirection direction;
        public float reactionDelay; 
        public float dodgeDuration; 
    }

    [Header("Core References & Animation Profiles")]
    public PlayerController player;
    public EnemyAnimProfile animProfile;
    public EnemyDodgeProfileSO dodgeProfile;
    private EnemyAnimationEngine animationEngine;
    public GameObject projectilePrefab;
    public Transform firePoint;
    private Collider capsuleCollider;
    private Transform cachedTransform;
    private Transform playerTransform;

    [Header("Health & Vulnerability")]
    public int maxHealth = 80;
    private int currentHealth;

    [Header("Enemy Type & Token Settings")]
    public TokenType tokenType = TokenType.Ranged;

    [Header("Reflex Settings")]
    public bool enableDodging = true;
    public float postDodgeBuffer = 0.2f;
    public float reflexRange = 4.0f;
    public List<DodgeMapping> dodgeMappings = new List<DodgeMapping>();

    [Header("Ranged Combat & Square Movement Tuning")]
    public float preferredRange = 12f;
    public float squareSideLength = 4f; 
    public float baseMoveSpeed = 7f;         
    public float jumpHeight = 1.8f;
    public float strafePhaseDuration = 3.5f; 
    public float rotationSpeed = 25f;
    [Tooltip("Scales animation playback speed with movement speed to eliminate foot skating.")]
    public float animationSpeedMultiplier = 1.0f;

    [Header("Separation Tuning")]
    public float separationRadius = 2.0f;
    public float separationWeight = 1.8f;

    [Header("Panic Retreat Tuning (Hit & Run)")]
    public float panicDistance = 18f;    
    public float panicSpeed = 10f;       

    [Header("Attack & Post-Shot Timing")]
    public float attackStartupTime = 0.3f;
    public float postShotExtraWait = 1.5f;

    public bool IsDodging { get; private set; } = false;

    private Coroutine activeDodgeRoutine;
    private Coroutine activeKnockbackRoutine;
    private float groundYCoord;
    private AttackData lastProcessedAttackData = null;

    private float randomizedMoveSpeed;
    private RangedBehaviorState currentState = RangedBehaviorState.SquareStrafing;
    private float strafePhaseTimer = 0f;
    private int squareSideIndex = 0;     
    private float sideTimer = 0f;
    private float attackTimer = 0f;
    private GameObject activeProjectileInstance = null;
    private float postShotTimer = 0f;
    private bool holdsToken = false;
    private bool isDead = false;
    private string lastPlayedLocomotionState = "";

    private static readonly Collider[] overlapBuffer = new Collider[16];

    private void Awake()
    {
        cachedTransform = transform;
        animationEngine = GetComponent<EnemyAnimationEngine>();
        currentHealth = maxHealth;
        capsuleCollider = GetComponent<Collider>();
        groundYCoord = cachedTransform.position.y;

        if (player != null)
        {
            playerTransform = player.transform;
        }

        if (animProfile != null) animProfile.InitializeDictionary();

        randomizedMoveSpeed = baseMoveSpeed + Random.Range(-1.2f, 1.2f);
        strafePhaseTimer = strafePhaseDuration + Random.Range(-1.0f, 1.5f);
        squareSideIndex = Random.Range(0, 4);
        sideTimer = squareSideLength / randomizedMoveSpeed;
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        IsDodging = false;
        holdsToken = false;
        isDead = false;
        currentState = RangedBehaviorState.SquareStrafing;
        activeKnockbackRoutine = null;
        activeDodgeRoutine = null;
        activeProjectileInstance = null;
        lastProcessedAttackData = null;
        lastPlayedLocomotionState = "";

        if (cachedTransform == null) cachedTransform = transform;
        if (capsuleCollider != null) capsuleCollider.enabled = true;

        randomizedMoveSpeed = baseMoveSpeed + Random.Range(-1.2f, 1.2f);
        strafePhaseTimer = strafePhaseDuration + Random.Range(-1.0f, 1.5f);
        squareSideIndex = Random.Range(0, 4);
        sideTimer = squareSideLength / randomizedMoveSpeed;
    }

    private void Start()
    {
        if (player != null && playerTransform == null)
        {
            playerTransform = player.transform;
        }

        if (playerTransform != null)
        {
            Vector3 dir = playerTransform.position - cachedTransform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
            {
                cachedTransform.rotation = Quaternion.LookRotation(dir);
            }
        }
    }

    private void OnDestroy()
    {
        if (activeProjectileInstance != null)
        {
            Destroy(activeProjectileInstance);
        }
        ReleaseTokenSafely();
        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(cachedTransform);
        }
    }

    private void Update()
    {
        if (isDead || player == null) return;
        if (playerTransform == null) playerTransform = player.transform;

        Vector3 currentPos = cachedTransform.position;
        Vector3 playerPos = playerTransform.position;
        float sqrDistToPlayer = (playerPos - currentPos).sqrMagnitude;
        float reflexRangeSqr = reflexRange * reflexRange;

        if (currentState == RangedBehaviorState.PanicRetreat)
        {
            FaceAwayFromPlayer(playerPos, currentPos);
        }
        else
        {
            FacePlayer(playerPos, currentPos);
        }

        if (!IsDodging && activeKnockbackRoutine == null)
        {
            HandleRangedBehavior(Mathf.Sqrt(sqrDistToPlayer), currentPos, playerPos);
        }

        if (!enableDodging || sqrDistToPlayer > reflexRangeSqr) 
        {
            lastProcessedAttackData = null;
            return;
        }

        PlayerState currentStateVal = player.CurrentState;
        bool isPlayerAttacking = (currentStateVal == player.AttackState ||
                                  currentStateVal == player.AOEAttackState ||
                                  currentStateVal == player.PowerPunchState);

        if (isPlayerAttacking)
        {
            AttackData currentAttack = GetActiveAttackData(player);

            if (currentAttack != null && currentAttack != lastProcessedAttackData)
            {
                lastProcessedAttackData = currentAttack;

                if (activeDodgeRoutine != null)
                {
                    StopCoroutine(activeDodgeRoutine);
                    ForceResetDodgeState();
                }

                ExecuteUltraInstinctReflex(currentAttack.attackID);
            }
        }
        else
        {
            lastProcessedAttackData = null;
        }
    }

    private void UpdateLocomotionAnimation(string stateKey, AnimationClip clip, float duration, float customSpeed = 1.0f)
    {
        if (animationEngine == null || animProfile == null || IsDodging || isDead) return;
        
        if (lastPlayedLocomotionState != stateKey)
        {
            lastPlayedLocomotionState = stateKey;
            if (clip != null)
            {
                float speedScale = (randomizedMoveSpeed / baseMoveSpeed) * animationSpeedMultiplier * customSpeed;
                animationEngine.PlayAnimation(clip, duration, speedScale);
            }
        }
    }

    private Vector3 CalculateSeparationForce(Vector3 currentPos)
    {
        Vector3 separationMove = Vector3.zero;
        int hitCount = Physics.OverlapSphereNonAlloc(currentPos, separationRadius, overlapBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapBuffer[i];
            if (hit != null && hit.gameObject != gameObject && hit.TryGetComponent<UltraInstinctRangedCapsule>(out _))
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

    private void HandleRangedBehavior(float distToPlayer, Vector3 currentPos, Vector3 playerPos)
    {
        Vector3 dirToPlayer = playerPos - currentPos;
        dirToPlayer.y = 0f;
        dirToPlayer.Normalize();
        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;
        Vector3 separation = CalculateSeparationForce(currentPos);
        float deltaTime = Time.deltaTime;

        switch (currentState)
        {
            case RangedBehaviorState.PanicRetreat:
                ReleaseTokenSafely();
                cachedTransform.position = currentPos + (cachedTransform.forward * panicSpeed + separation) * deltaTime;

                if (animProfile != null)
                {
                    UpdateLocomotionAnimation("PanicRetreat", animProfile.walkClip, animProfile.walkTransitionDuration, panicSpeed / baseMoveSpeed);
                }

                if (distToPlayer >= panicDistance)
                {
                    strafePhaseTimer = strafePhaseDuration + Random.Range(-0.5f, 0.5f);
                    currentState = RangedBehaviorState.SquareStrafing;
                }
                break;

            case RangedBehaviorState.SquareStrafing:
                strafePhaseTimer -= deltaTime;
                sideTimer -= deltaTime;

                if (sideTimer <= 0f)
                {
                    squareSideIndex = (squareSideIndex + 1) % 4;
                    sideTimer = squareSideLength / randomizedMoveSpeed;
                }

                Vector3 squareMoveDir = Vector3.zero;
                string strafeKey = "SquareStrafe_Walk";
                switch (squareSideIndex)
                {
                    case 0: 
                        squareMoveDir = rightDir; 
                        strafeKey = "SquareStrafe_Right";
                        break;               
                    case 1: 
                        squareMoveDir = -dirToPlayer; 
                        strafeKey = "SquareStrafe_Backward";
                        break;           
                    case 2: 
                        squareMoveDir = -rightDir; 
                        strafeKey = "SquareStrafe_Left";
                        break;              
                    case 3: 
                        squareMoveDir = dirToPlayer; 
                        strafeKey = "SquareStrafe_Forward";
                        break;            
                }

                Vector3 finalMove = (squareMoveDir * randomizedMoveSpeed + separation) * deltaTime;

                if (distToPlayer < preferredRange - 2f)
                {
                    finalMove -= dirToPlayer * (randomizedMoveSpeed * 0.5f) * deltaTime;
                }
                else if (distToPlayer > preferredRange + 3f)
                {
                    finalMove += dirToPlayer * (randomizedMoveSpeed * 0.5f) * deltaTime;
                }

                cachedTransform.position = currentPos + finalMove;

                if (animProfile != null)
                {
                    AnimationClip moveClip = (squareSideIndex == 0 && animProfile.strafeRightClip != null) ? animProfile.strafeRightClip :
                                             (squareSideIndex == 2 && animProfile.strafeLeftClip != null) ? animProfile.strafeLeftClip :
                                             animProfile.walkClip;
                    UpdateLocomotionAnimation(strafeKey, moveClip, animProfile.walkTransitionDuration);
                }

                if (strafePhaseTimer <= 0f)
                {
                    if (GlobalTokenManager.Instance != null)
                    {
                        if (GlobalTokenManager.Instance.RequestToken(cachedTransform, tokenType))
                        {
                            holdsToken = true;
                            currentState = RangedBehaviorState.WaitToShoot;
                        }
                    }
                    else
                    {
                        currentState = RangedBehaviorState.WaitToShoot;
                    }
                }
                break;

            case RangedBehaviorState.WaitToShoot:
                // Play ranged attack/shoot animation clip
                if (animProfile != null && animationEngine != null)
                {
                    AnimationClip shootClip = animProfile.GetAnimationClip("RangedAttack") ?? animProfile.idleClip;
                    animationEngine.PlayAnimation(shootClip, 0.05f, 1.0f);
                    lastPlayedLocomotionState = "WaitToShoot";
                }
                attackTimer = attackStartupTime;
                currentState = RangedBehaviorState.Firing;
                break;

            case RangedBehaviorState.Firing:
                attackTimer -= deltaTime;
                if (attackTimer <= 0f)
                {
                    FireProjectile(dirToPlayer);
                    currentState = RangedBehaviorState.WaitingForProjectileOutcome;
                }
                break;

            case RangedBehaviorState.WaitingForProjectileOutcome:
                if (animProfile != null)
                {
                    UpdateLocomotionAnimation("WaitToShoot_Idle", animProfile.idleClip, animProfile.idleTransitionDuration);
                }

                if (activeProjectileInstance == null)
                {
                    postShotTimer += deltaTime;
                    if (postShotTimer >= postShotExtraWait)
                    {
                        postShotTimer = 0f;
                        ReleaseTokenSafely();
                        strafePhaseTimer = strafePhaseDuration + Random.Range(-0.5f, 0.5f);
                        currentState = RangedBehaviorState.SquareStrafing;
                    }
                }
                else
                {
                    Vector3 hoverMove = (rightDir * (randomizedMoveSpeed * 0.6f * Mathf.Sin(Time.time * 5f)) + separation) * deltaTime;
                    cachedTransform.position = currentPos + hoverMove;
                }
                break;
        }
    }

    private void ReleaseTokenSafely()
    {
        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }
    }

    private void FireProjectile(Vector3 dirToPlayer)
    {
        if (projectilePrefab == null) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : cachedTransform.position + Vector3.up;
        Quaternion spawnRot = Quaternion.LookRotation(dirToPlayer);

        GameObject projObj = Instantiate(projectilePrefab, spawnPos, spawnRot);
        if (projObj.TryGetComponent<RangedProjectile>(out var projScript))
        {
            projScript.Initialize(player.transform);
        }
        activeProjectileInstance = projObj;
    }

    private void FacePlayer(Vector3 playerPos, Vector3 currentPos)
    {
        Vector3 dirToPlayer = playerPos - currentPos;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirToPlayer);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void FaceAwayFromPlayer(Vector3 playerPos, Vector3 currentPos)
    {
        Vector3 dirAwayFromPlayer = currentPos - playerPos;
        dirAwayFromPlayer.y = 0f;
        if (dirAwayFromPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirAwayFromPlayer);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRotation, (rotationSpeed * 4f) * Time.deltaTime);
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead || IsDodging) return;

        int finalDamage = Mathf.RoundToInt(damage);
        currentHealth -= finalDamage;

        PlayHitReaction(attackID, hitDirection);

        ReleaseTokenSafely();

        float knockbackDist = force > 0f ? force : 1.0f;
        if (activeKnockbackRoutine != null)
        {
            StopCoroutine(activeKnockbackRoutine);
        }
        activeKnockbackRoutine = StartCoroutine(PerformKnockbackRoutine(hitDirection, knockbackDist));

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void PlayHitReaction(int attackID, Vector3 hitDirection)
    {
        if (animProfile == null || animationEngine == null) return;

        AttackReactionData reactionData = animProfile.GetReaction(attackID);
        HitAnimationData hitAnim = null;

        Vector3 localDir = cachedTransform.InverseTransformDirection(hitDirection.normalized);
        if (Mathf.Abs(localDir.z) > Mathf.Abs(localDir.x))
        {
            hitAnim = localDir.z > 0 ? (reactionData != null ? reactionData.reactionFront : animProfile.defaultHitFront) 
                                     : (reactionData != null ? reactionData.reactionBack : animProfile.defaultHitBack);
        }
        else
        {
            hitAnim = localDir.x > 0 ? (reactionData != null ? reactionData.reactionRight : animProfile.defaultHitRight) 
                                     : (reactionData != null ? reactionData.reactionLeft : animProfile.defaultHitLeft);
        }

        if (hitAnim != null && hitAnim.clip != null)
        {
            animationEngine.PlayAnimation(hitAnim.clip, hitAnim.transitionDuration, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction";
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        ReleaseTokenSafely();
        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(cachedTransform);
        }
        if (activeProjectileInstance != null)
        {
            Destroy(activeProjectileInstance);
        }

        if (animProfile != null && animProfile.deathClip != null && animationEngine != null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(0.75f);

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(cachedTransform.gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void ExecuteUltraInstinctReflex(int attackID)
    {
        ReleaseTokenSafely();
        IsDodging = true;
        if (capsuleCollider != null) capsuleCollider.enabled = false;

        SpecificDodgeData specificDodge = dodgeProfile != null ? dodgeProfile.GetSpecificDodge(attackID) : null;
        AnimationClip dodgeClip = specificDodge != null ? specificDodge.dodgeClip : (dodgeProfile != null ? dodgeProfile.defaultDodgeClip : null);
        float transition = specificDodge != null ? specificDodge.transitionDuration : (dodgeProfile != null ? dodgeProfile.defaultTransitionDuration : 0.05f);
        float speed = specificDodge != null ? specificDodge.playbackSpeed : (dodgeProfile != null ? dodgeProfile.defaultPlaybackSpeed : 1.0f);
        float moveDist = specificDodge != null ? specificDodge.moveDistance : (dodgeProfile != null ? dodgeProfile.defaultMoveDistance : 0.8f);
        Vector3 moveDir = specificDodge != null ? specificDodge.moveDirection : (dodgeProfile != null ? dodgeProfile.defaultMoveDirection : -Vector3.forward);

        if (animationEngine != null && dodgeClip != null)
        {
            animationEngine.PlayAnimation(dodgeClip, transition, speed);
            lastPlayedLocomotionState = "Dodge";
        }

        activeDodgeRoutine = StartCoroutine(PerformDodgeRoutine(moveDist, moveDir));
    }

    private void ForceResetDodgeState()
    {
        if (capsuleCollider != null) capsuleCollider.enabled = true;
        IsDodging = false;
        
        Vector3 pos = cachedTransform.position;
        pos.y = groundYCoord;
        cachedTransform.position = pos;
    }

    private IEnumerator PerformKnockbackRoutine(Vector3 hitDirection, float knockbackForce)
    {
        Vector3 startPos = cachedTransform.position;
        float slideDistance = knockbackForce;
        
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -cachedTransform.forward;
        pushDir.Normalize();

        Vector3 targetPos = startPos + (pushDir * slideDistance);
        float duration = 0.1f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            if (playerTransform == null && player != null) playerTransform = player.transform;
            if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position);

            cachedTransform.position = Vector3.Lerp(startPos, targetPos, smoothProgress);
            yield return null;
        }

        cachedTransform.position = targetPos;

        yield return new WaitForSeconds(0.25f);

        if (!isDead)
        {
            currentState = RangedBehaviorState.PanicRetreat;
        }

        activeKnockbackRoutine = null;
    }

    private IEnumerator PerformDodgeRoutine(float distance, Vector3 localDirection)
    {
        Vector3 startPos = cachedTransform.position;
        groundYCoord = startPos.y;
        Vector3 worldMoveDir = cachedTransform.TransformDirection(localDirection).normalized;
        Vector3 targetPos = startPos + (worldMoveDir * distance);

        float duration = 0.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position);
            cachedTransform.position = Vector3.Lerp(startPos, targetPos, smoothProgress);
            yield return null;
        }

        cachedTransform.position = targetPos;

        if (postDodgeBuffer > 0f)
        {
            yield return new WaitForSeconds(postDodgeBuffer);
        }

        if (capsuleCollider != null) capsuleCollider.enabled = true;
        IsDodging = false;
        activeDodgeRoutine = null;
    }

    private AttackData GetActiveAttackData(PlayerController playerRef)
    {
        if (playerRef == null) return null;

        if (playerRef.CurrentState == playerRef.AOEAttackState && playerRef.specialAttackY != null)
            return playerRef.specialAttackY;

        if (playerRef.CurrentState == playerRef.PowerPunchState && playerRef.equippedStyle != null)
        {
            return playerRef.equippedStyle.GetActiveChargeAttack();
        }

        if (playerRef.equippedStyle != null && playerRef.equippedStyle.lightComboSequence != null && 
            playerRef.equippedStyle.lightComboSequence.Length > playerRef.CurrentComboIndex)
        {
            return playerRef.equippedStyle.lightComboSequence[playerRef.CurrentComboIndex];
        }

        return null;
    }
}