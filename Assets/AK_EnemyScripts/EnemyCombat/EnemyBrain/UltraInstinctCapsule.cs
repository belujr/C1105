using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Animation;
using CombatSystem.Data;

[RequireComponent(typeof(EnemyAnimationEngine))]
public class UltraInstinctCapsule : MonoBehaviour, IDamageable
{
    public enum DodgeDirection { DodgeLeft, DodgeRight, Jump, Backstep }
    public enum HitDirection { Front, Back, Left, Right }
    public enum MeleeBehaviorState { Approach, ArcStrafe, Wait, AttackLunge, Retreat }

    [System.Serializable]
    public struct DodgeMapping
    {
        public string attackName; 
        public int attackID;
        public DodgeDirection direction;
        
        [Tooltip("Delay in seconds after this attack is pressed before the capsule moves.")]
        public float reactionDelay; 
        
        [Tooltip("How long it takes to complete this specific dodge.")]
        public float dodgeDuration; 
    }

    [Header("Core References & Animation Profiles")]
    public PlayerController player;
    public EnemyAnimProfile animProfile;
    public EnemyDodgeProfileSO dodgeProfile;
    private EnemyAnimationEngine animationEngine;
    private Collider capsuleCollider;
    private Transform cachedTransform;
    private Transform playerTransform;

    [Header("Health & Vulnerability")]
    public int maxHealth = 100;
    [Tooltip("Time in seconds to wait after death before vanishing or returning to pool.")]
    public float deathDisappearDelay = 2.0f;
    private int currentHealth;

    [Header("Enemy Type & Token Settings")]
    [Tooltip("Check this for Melee behavior (Approach, Arc-Strafe, Token Attack, Retreat).")]
    public bool isMelee = true;
    [Tooltip("Check this for Ranged behavior.")]
    public bool isRanged = false;
    [Tooltip("The token category this enemy requests to execute melee attacks.")]
    public TokenType tokenType = TokenType.Melee;

    [Header("Melee Damage & Attack Settings")]
    public float meleeDamage = 25f;
    public float meleeKnockbackForce = 2f;
    public AudioClip meleeHitSound;

    [Header("Enemy Combo Sequence & Speed Scaling")]
    [Tooltip("Sequence of BaseAttackDataSO assets (Standard Melee or AOE Attack SOs) for the enemy's combo chain.")]
    public BaseAttackDataSO[] enemyComboSequence;

    [Tooltip("Overall speed multiplier for enemy attack actions and combo animations. Values above 1.0 make attacks faster.")]
    [SerializeField] private float enemyAttackSpeedMultiplier = 1.0f;

    [Header("Anticipation & Slow-Mo Telegraph Settings (First Attack Only)")]
    [Tooltip("Duration of the anticipation/wind-up warning phase before the first strike hits.")]
    public float anticipationDuration = 0.25f;
    [Tooltip("Game time scale during anticipation (e.g., 0.3 creates a dramatic slow-mo window).")]
    public float anticipationTimeScale = 0.3f;
    [Tooltip("Color the enemy flashes during the anticipation wind-up marker.")]
    public Color anticipationFlashColor = Color.yellow;
    public float anticipationFlashDuration = 0.1f;

    [Header("Reflex Settings")]
    [Tooltip("Master switch to turn the capsule's dodging abilities on or off.")]
    public bool enableDodging = true;
    public float postDodgeBuffer = 0.2f;
    public float reflexRange = 3.0f;
    
    [Tooltip("Map your Player's Attack IDs to specific Dodge Directions and timings here.")]
    public List<DodgeMapping> dodgeMappings = new List<DodgeMapping>();

    [Header("Movement & Realistic Combat Tuning")]
    public float baseMoveSpeed = 3.5f;
    public float baseStrafeSpeed = 2.5f;
    public float stoppingDistance = 4.0f; 
    public float dodgeDistance = 2.0f;
    public float jumpHeight = 2.0f;
    public float rotationSpeed = 20f;
    [Tooltip("Multiplier applied to rotation speed specifically while performing a dodge.")]
    public float dodgeFacingSpeedMultiplier = 0.3f;

    [Header("Separation Tuning")]
    public float separationRadius = 1.8f;
    public float separationWeight = 2.0f;

    [Header("Token Attack & Retreat Tuning (Corridor Range)")]
    public float attackRange = 2.5f;
    [Tooltip("Forward reach distance of the attack corridor zone.")]
    public float playerDodgeRange = 2.8f;
    [Tooltip("Left/Right width of the attack corridor.")]
    public float attackCorridorWidth = 1.2f;
    public float lungeSpeed = 7.0f;
    public float retreatDistance = 4.5f;
    public float retreatSpeed = 4.0f;

    public bool IsDodging { get; private set; } = false;

    private Coroutine activeDodgeRoutine;
    private Coroutine activeKnockbackRoutine;
    private Coroutine activeEnemyComboRoutine;
    private float groundYCoord;
    private AttackData lastProcessedAttackData = null;

    private BaseAttackDataSO currentActiveEnemyAttack = null;

    private float randomizedMoveSpeed;
    private float randomizedStrafeSpeed;

    private MeleeBehaviorState currentMeleeState = MeleeBehaviorState.Approach;
    private float arcTimer = 0f;
    private float waitTimer = 0f;
    private int strafeDirectionSign = 1;
    private bool holdsToken = false;
    private bool isDead = false;
    private string lastPlayedLocomotionState = "";

    private bool isReacting = false;
    private float reactionTimer = 0f;

    // Material flash caching for anticipation marker
    private Renderer[] enemyRenderers;
    private Dictionary<Renderer, Color[]> originalEnemyColors = new Dictionary<Renderer, Color[]>();

    private static readonly Collider[] overlapBuffer = new Collider[16];

    private void Awake()
    {
        cachedTransform = transform;
        animationEngine = GetComponent<EnemyAnimationEngine>();
        capsuleCollider = GetComponent<Collider>();
        groundYCoord = cachedTransform.position.y;
        enemyRenderers = GetComponentsInChildren<Renderer>();

        foreach (var r in enemyRenderers)
        {
            Color[] colors = new Color[r.materials.Length];
            for (int i = 0; i < r.materials.Length; i++)
            {
                if (r.materials[i].HasProperty("_Color")) colors[i] = r.materials[i].color;
            }
            originalEnemyColors.Add(r, colors);
        }

        if (animProfile != null) animProfile.InitializeDictionary();

        FindPlayerReference();

        randomizedMoveSpeed = baseMoveSpeed + Random.Range(-0.6f, 0.6f);
        randomizedStrafeSpeed = baseStrafeSpeed + Random.Range(-0.5f, 0.5f);
        strafeDirectionSign = Random.value > 0.5f ? 1 : -1;
        arcTimer = Random.Range(1.0f, 2.5f);
    }

    public void TriggerHitbox()
    {
        if (CheckIfAttackHit())
        {
            if (currentActiveEnemyAttack != null)
            {
                DealMeleeDamageToPlayer(
                    currentActiveEnemyAttack.DamageAmount,
                    currentActiveEnemyAttack.KnockbackForce,
                    // Convert the animation string into an integer hash!
                    Animator.StringToHash(currentActiveEnemyAttack.PlayerReactionAnimName),
                    currentActiveEnemyAttack.HitSound,
                    currentActiveEnemyAttack.StunDuration
                );
            }
            else
            {
                DealMeleeDamageToPlayer();
            }
            SetPlayerComboLock(true);
        }
        else
        {
            SetPlayerComboLock(false);
        }
    }

    public void DisableHitbox()
    {
        // Currently empty, but satisfies the Animation Event requirement. 
        // If you ever switch to physical overlap spheres instead of distance checks, 
        // you would disable the trigger collider here.
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        IsDodging = false;
        holdsToken = false;
        isDead = false;
        isReacting = false;
        currentMeleeState = MeleeBehaviorState.Approach;
        activeKnockbackRoutine = null;
        activeDodgeRoutine = null;
        activeEnemyComboRoutine = null;
        lastProcessedAttackData = null;
        lastPlayedLocomotionState = "";

        if (cachedTransform == null) cachedTransform = transform;
        if (capsuleCollider != null) capsuleCollider.enabled = true;

        FindPlayerReference();

        randomizedMoveSpeed = baseMoveSpeed + Random.Range(-0.6f, 0.6f);
        randomizedStrafeSpeed = baseStrafeSpeed + Random.Range(-0.5f, 0.5f);
        strafeDirectionSign = Random.value > 0.5f ? 1 : -1;
        arcTimer = Random.Range(1.0f, 2.5f);
    }

    private void OnDisable()
    {
        currentHealth = maxHealth;
        IsDodging = false;
        holdsToken = false;
        isDead = false;
        isReacting = false;
        currentMeleeState = MeleeBehaviorState.Approach;
        activeKnockbackRoutine = null;
        activeDodgeRoutine = null;
        activeEnemyComboRoutine = null;
        lastProcessedAttackData = null;
        lastPlayedLocomotionState = "";

        SetPlayerComboLock(false);

        if (cachedTransform == null) cachedTransform = transform;
        if (capsuleCollider != null) capsuleCollider.enabled = true;

        FindPlayerReference();

        randomizedMoveSpeed = baseMoveSpeed + Random.Range(-0.6f, 0.6f);
        randomizedStrafeSpeed = baseStrafeSpeed + Random.Range(-0.5f, 0.5f);
        strafeDirectionSign = Random.value > 0.5f ? 1 : -1;
        arcTimer = Random.Range(1.0f, 2.5f);
    }

    private void FindPlayerReference()
    {
        if (player == null)
        {
            player = FindObjectOfType<PlayerController>();
        }
        if (player != null)
        {
            playerTransform = player.transform;
        }
    }

    private void Start()
    {
        FindPlayerReference();
    }

    private void OnDestroy()
    {
        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(cachedTransform);
        }
        Time.timeScale = 1.0f;
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayerReference();
            if (player == null) return;
        }
        if (playerTransform == null) playerTransform = player.transform;

        Vector3 currentPos = cachedTransform.position;
        Vector3 playerPos = playerTransform.position;

        if (activeEnemyComboRoutine == null)
        {
            FacePlayer(playerPos, currentPos);
        }

        if (isDead) return;

        float sqrDistToPlayer = (playerPos - currentPos).sqrMagnitude;
        float reflexRangeSqr = reflexRange * reflexRange;

        if (isReacting)
        {
            reactionTimer -= Time.unscaledDeltaTime;
            if (reactionTimer <= 0f)
            {
                isReacting = false;
            }
            return;
        }

        if (!isMelee && !isRanged) return;

        if (isMelee && !IsDodging && activeKnockbackRoutine == null)
        {
            HandleMeleeMovement(Mathf.Sqrt(sqrDistToPlayer), currentPos, playerPos);
        }
        else if (isRanged && !IsDodging && activeKnockbackRoutine == null)
        {
            HandleRangedMovement(Mathf.Sqrt(sqrDistToPlayer));
        }

        // Prevent reflex dodging if disabled, out of range, attacking, or reacting
        if (!enableDodging || sqrDistToPlayer > reflexRangeSqr || activeEnemyComboRoutine != null || isReacting) 
        {
            return;
        }

        if (player != null && !player.enabled)
        {
            return;
        }

        PlayerState currentState = player.CurrentState;
        bool isPlayerAttacking = (currentState == player.AttackState);

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

    private void UpdateLocomotionAnimation(string stateKey, AnimationClip clip, float duration)
    {
        if (animationEngine == null || animProfile == null || IsDodging || isDead || isReacting) return;
        
        if (lastPlayedLocomotionState != stateKey)
        {
            lastPlayedLocomotionState = stateKey;
            if (clip != null)
            {
                animationEngine.PlayAnimation(clip, duration);
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
            if (hit != null && hit.gameObject != gameObject && hit.TryGetComponent<UltraInstinctCapsule>(out _))
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

    private void HandleMeleeMovement(float distToPlayer, Vector3 currentPos, Vector3 playerPos)
    {
        Vector3 dirToPlayer = playerPos - currentPos;
        dirToPlayer.y = 0f;
        dirToPlayer.Normalize();

        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;
        Vector3 separation = CalculateSeparationForce(currentPos);
        float deltaTime = Time.deltaTime;

        switch (currentMeleeState)
        {
            case MeleeBehaviorState.Approach:
                if (distToPlayer > stoppingDistance)
                {
                    Vector3 moveDelta = (dirToPlayer * randomizedMoveSpeed + rightDir * (randomizedStrafeSpeed * 0.2f * strafeDirectionSign) + separation) * deltaTime;
                    cachedTransform.position = currentPos + moveDelta;
                    
                    if (animProfile != null)
                        UpdateLocomotionAnimation("Approach", animProfile.walkClip, animProfile.walkTransitionDuration);
                }
                else
                {
                    currentMeleeState = MeleeBehaviorState.ArcStrafe;
                    arcTimer = Random.Range(1.8f, 2.8f);
                }
                break;

            case MeleeBehaviorState.ArcStrafe:
                if (distToPlayer > stoppingDistance * 1.5f)
                {
                    currentMeleeState = MeleeBehaviorState.Approach;
                    break;
                }

                arcTimer -= deltaTime;

                Vector3 strafeDelta = (rightDir * randomizedStrafeSpeed * strafeDirectionSign) + separation;
                float distanceError = distToPlayer - stoppingDistance;
                strafeDelta += dirToPlayer * (distanceError * randomizedMoveSpeed);
                
                cachedTransform.position = currentPos + strafeDelta * deltaTime;

                if (animProfile != null)
                {
                    AnimationClip strafeClip = strafeDirectionSign > 0 ? animProfile.strafeRightClip : animProfile.strafeLeftClip;
                    if (strafeClip == null) strafeClip = animProfile.walkClip;
                    string strafeKey = strafeDirectionSign > 0 ? "ArcStrafe_Right" : "ArcStrafe_Left";
                    UpdateLocomotionAnimation(strafeKey, strafeClip, animProfile.walkTransitionDuration);
                }

                if (arcTimer <= 0f)
                {
                    currentMeleeState = MeleeBehaviorState.Wait;
                    waitTimer = Random.Range(0.8f, 1.5f);
                }
                break;

            case MeleeBehaviorState.Wait:
                if (distToPlayer > stoppingDistance * 1.5f)
                {
                    currentMeleeState = MeleeBehaviorState.Approach;
                    break;
                }

                if (IsPlayerInAttackCorridor() && !holdsToken)
                {
                    if (GlobalTokenManager.Instance != null && GlobalTokenManager.Instance.RequestToken(cachedTransform, tokenType))
                    {
                        holdsToken = true;
                        currentMeleeState = MeleeBehaviorState.AttackLunge;
                        break;
                    }
                }

                waitTimer -= deltaTime;

                float waitDistError = distToPlayer - stoppingDistance;
                Vector3 holdDelta = Vector3.zero;
                if (Mathf.Abs(waitDistError) > 0.1f)
                {
                    holdDelta = dirToPlayer * (waitDistError * randomizedMoveSpeed * 0.5f);
                }
                cachedTransform.position = currentPos + (holdDelta + separation) * deltaTime;

                if (animProfile != null)
                    UpdateLocomotionAnimation("Wait_Idle", animProfile.idleClip, animProfile.idleTransitionDuration);

                if (waitTimer <= 0f)
                {
                    strafeDirectionSign *= -1;
                    currentMeleeState = MeleeBehaviorState.ArcStrafe;
                    arcTimer = Random.Range(1.8f, 2.8f);
                }
                break;

            case MeleeBehaviorState.AttackLunge:
                if (activeEnemyComboRoutine == null)
                {
                    activeEnemyComboRoutine = StartCoroutine(PerformEnemyComboRoutine());
                }
                break;

            case MeleeBehaviorState.Retreat:
                cachedTransform.position = currentPos - (dirToPlayer * retreatSpeed - separation) * deltaTime;

                if (animProfile != null)
                    UpdateLocomotionAnimation("Retreat", animProfile.walkClip, animProfile.walkTransitionDuration);

                if (distToPlayer >= retreatDistance)
                {
                    if (GlobalTokenManager.Instance != null && holdsToken)
                    {
                        GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
                        holdsToken = false;
                    }

                    currentMeleeState = MeleeBehaviorState.ArcStrafe;
                    arcTimer = Random.Range(1.5f, 2.5f);
                }
                break;
        }
    }

    private bool IsPlayerInAttackCorridor()
    {
        if (playerTransform == null) return false;
        
        Vector3 enemyPos = cachedTransform.position;
        Vector3 playerPos = playerTransform.position;
        Vector3 dirToPlayer = playerPos - enemyPos;
        dirToPlayer.y = 0f;

        float forwardDist = Vector3.Dot(dirToPlayer, cachedTransform.forward);
        float lateralDist = Mathf.Abs(Vector3.Dot(dirToPlayer, cachedTransform.right));

        return forwardDist >= 0f && forwardDist <= playerDodgeRange && lateralDist <= attackCorridorWidth;
    }

    private bool CheckIfAttackHit()
    {
        if (playerTransform == null) return false;
        float distToPlayer = Vector3.Distance(cachedTransform.position, playerTransform.position);
        return distToPlayer <= (attackRange * 1.5f);
    }

    private IEnumerator PerformEnemyComboRoutine()
    {
        float speedScale = Mathf.Max(0.1f, enemyAttackSpeedMultiplier);
        bool playerCaughtInCombo = false;

        try
        {
            if (enemyComboSequence == null || enemyComboSequence.Length == 0)
            {
                for (int hitCount = 0; hitCount < 3; hitCount++)
                {
                    if (isDead || player == null) yield break;

                    // --- ATTACK 1 ONLY: ANTICIPATION WINDOW ---
                    if (hitCount == 0) // (For the second block further down, it will be 'if (i == 0)')
                    {
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                        }
                        StartCoroutine(AnticipationFlashRoutine());

                        // REMOVED Time.timeScale modifications!

                        float anticipationElapsed = 0f;
                        while (anticipationElapsed < anticipationDuration)
                        {
                            if (isDead) yield break;

                            // Using standard time instead of unscaled time
                            anticipationElapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                    // ------------------------------------------------------------------

                    if (hitCount == 0)
                    {
                        if (playerTransform != null)
                        {
                            Vector3 startPos = cachedTransform.position;
                            Vector3 targetLerpPos = playerTransform.position - ((playerTransform.position - cachedTransform.position).normalized * Mathf.Min(attackRange * 0.8f, 1.2f));
                            targetLerpPos.y = groundYCoord;

                            float lerpElapsed = 0f;
                            float lerpDuration = 0.18f / speedScale;
                            while (lerpElapsed < lerpDuration)
                            {
                                if (isDead) { ResetTimeScale(); yield break; }
                                lerpElapsed += Time.deltaTime;
                                float t = Mathf.Clamp01(lerpElapsed / lerpDuration);
                                cachedTransform.position = Vector3.Lerp(startPos, targetLerpPos, t);
                                FacePlayer(playerTransform.position, cachedTransform.position);
                                yield return null;
                            }
                        }
                    }
                    else
                    {
                        if (!playerCaughtInCombo) break;

                        // SUBSEQUENT HITS (2 & 3): Always snap and face the player directly
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                            Vector3 dirToPlayer = (playerTransform.position - cachedTransform.position);
                            dirToPlayer.y = 0f;
                            dirToPlayer.Normalize();
                            
                            Vector3 snapPos = playerTransform.position - (dirToPlayer * Mathf.Min(attackRange * 0.8f, 1.2f));
                            snapPos.y = groundYCoord;
                            cachedTransform.position = snapPos;
                            FacePlayer(playerTransform.position, cachedTransform.position);
                        }
                    }

                    float elapsed = 0f;
                    float duration = 0.35f / speedScale;
                    while (elapsed < duration)
                    {
                        if (isDead) yield break;
                        elapsed += Time.deltaTime;
                        
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                            Vector3 targetPos = playerTransform.position;
                            targetPos.y = groundYCoord;
                            cachedTransform.position = Vector3.MoveTowards(cachedTransform.position, targetPos, lungeSpeed * Time.deltaTime);
                        }

                        yield return null;
                    }

                    // CHECK IF HIT 1 CONNECTED
                    if (hitCount == 0)
                    {
                        if (CheckIfAttackHit())
                        {
                            playerCaughtInCombo = true;
                        }
                        else
                        {
                            playerCaughtInCombo = false;
                            break; // Missed; cancel combo hits 2 & 3
                        }
                    }

                    yield return new WaitForSeconds(0.15f / speedScale);
                }
            }
            else
            {
                for (int i = 0; i < enemyComboSequence.Length; i++)
                {
                    if (isDead || player == null) yield break;

                    BaseAttackDataSO atk = enemyComboSequence[i];
                    currentActiveEnemyAttack = atk; // <-- ADD THIS LINE HERE
                    float atkDamage = atk != null ? atk.DamageAmount : meleeDamage;
                    float atkKnockback = atk != null ? atk.KnockbackForce : meleeKnockbackForce;
                    float atkStun = atk != null ? atk.StunDuration : 0.2f;
                    AudioClip atkSound = atk != null && atk.HitSound != null ? atk.HitSound : meleeHitSound;
                    int atkID = atk != null ? atk.GetHashCode() : 0;

                    // --- ATTACK 1 ONLY: ANTICIPATION WINDOW ---
                    if (i == 0)
                    {
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                        }
                        StartCoroutine(AnticipationFlashRoutine());

                        // REMOVED Time.timeScale modifications!

                        float anticipationElapsed = 0f;
                        while (anticipationElapsed < anticipationDuration)
                        {
                            if (isDead) yield break;

                            // Use standard time instead of unscaled time
                            anticipationElapsed += Time.deltaTime;
                            yield return null;
                        }
                    }
                    // ------------------------------------------------------------------

                    if (animProfile != null && animationEngine != null && atk != null)
                    {
                        AnimationClip attackClip = null;

                        if (!string.IsNullOrEmpty(atk.AnimationClipName))
                        {
                            attackClip = animProfile.GetAnimationClip(atk.AnimationClipName);
                        }
                        if (attackClip == null && !string.IsNullOrEmpty(atk.AttackName))
                        {
                            attackClip = animProfile.GetAnimationClip(atk.AttackName);
                        }
                        if (attackClip == null)
                        {
                            attackClip = animProfile.GetAnimationClip(atk.name);
                        }

                        if (attackClip != null)
                        {
                            animationEngine.PlayAnimation(attackClip, 0.05f / speedScale, speedScale);
                            lastPlayedLocomotionState = $"Attack_{atk.name}";
                        }
                    }

                    if (i == 0)
                    {
                        if (playerTransform != null)
                        {
                            Vector3 startPos = cachedTransform.position;
                            Vector3 dirToP = (playerTransform.position - cachedTransform.position).normalized;
                            Vector3 targetLerpPos = playerTransform.position - (dirToP * Mathf.Min(attackRange * 0.8f, 1.2f));
                            targetLerpPos.y = groundYCoord;

                            float lerpElapsed = 0f;
                            float lerpDuration = 0.18f / speedScale;
                            while (lerpElapsed < lerpDuration)
                            {
                                if (isDead) { ResetTimeScale(); yield break; }
                                lerpElapsed += Time.deltaTime;
                                float t = Mathf.Clamp01(lerpElapsed / lerpDuration);
                                cachedTransform.position = Vector3.Lerp(startPos, targetLerpPos, t);
                                FacePlayer(playerTransform.position, cachedTransform.position);
                                yield return null;
                            }
                        }
                    }
                    else
                    {
                        if (!playerCaughtInCombo) break;

                        // SUBSEQUENT HITS (2 & 3): Always snap and face the player directly
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                            Vector3 dirToPlayer = (playerTransform.position - cachedTransform.position);
                            dirToPlayer.y = 0f;
                            dirToPlayer.Normalize();
                            
                            Vector3 snapPos = playerTransform.position - (dirToPlayer * Mathf.Min(attackRange * 0.8f, 1.2f));
                            snapPos.y = groundYCoord;
                            cachedTransform.position = snapPos;
                            FacePlayer(playerTransform.position, cachedTransform.position);
                        }
                    }

                    float startup = (atk != null ? atk.StartupTime : 0.15f) / speedScale;
                    float active = (atk != null ? atk.ActiveTime : 0.15f) / speedScale;
                    float recovery = (atk != null ? atk.RecoveryTime : 0.2f) / speedScale;

                    float elapsedStartup = 0f;
                    while (elapsedStartup < startup)
                    {
                        if (isDead) yield break;
                        elapsedStartup += Time.deltaTime;
                        
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                            Vector3 targetPos = playerTransform.position;
                            targetPos.y = groundYCoord;
                            cachedTransform.position = Vector3.MoveTowards(cachedTransform.position, targetPos, lungeSpeed * Time.deltaTime);
                        }

                        yield return null;
                    }


                    float elapsedActive = 0f;
                    while (elapsedActive < active)
                    {
                        if (isDead) yield break;
                        elapsedActive += Time.deltaTime;
                        
                        if (playerTransform != null)
                        {
                            FacePlayer(playerTransform.position, cachedTransform.position);
                        }

                        yield return null;
                    }

                    yield return new WaitForSeconds(recovery);
                }
            }
        }
        finally
        {
            ResetTimeScale();
            if (playerCaughtInCombo)
            {
                SetPlayerComboLock(false);
            }

            if (GlobalTokenManager.Instance != null && holdsToken)
            {
                GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
                holdsToken = false;
            }

            activeEnemyComboRoutine = null;
            currentMeleeState = MeleeBehaviorState.ArcStrafe;
            arcTimer = Random.Range(1.0f, 2.0f);
        }
    }

    private void ResetTimeScale()
    {
        Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;
    }

    private IEnumerator AnticipationFlashRoutine()
    {
        foreach (var r in enemyRenderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_Color")) mat.color = anticipationFlashColor;
            }
        }

        yield return new WaitForSecondsRealtime(anticipationFlashDuration);

        foreach (var r in enemyRenderers)
        {
            if (r == null || !originalEnemyColors.ContainsKey(r)) continue;
            for (int i = 0; i < r.materials.Length; i++)
            {
                if (r.materials[i].HasProperty("_Color"))
                {
                    r.materials[i].color = originalEnemyColors[r][i];
                }
            }
        }
    }

    private void SetPlayerComboLock(bool isLocked)
    {
        if (player != null)
        {
            // COMMENT OUT THESE LINES! Let the State Machine handle the stun.
            // player.enabled = !isLocked; 
            // if (player.Animator != null)
            // {
            //     player.Animator.SetFloat("Speed", 0f);
            // }

            var playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null)
            {
                playerHealth.SetComboStunLock(isLocked);
            }
        }
    }

    private void HandleRangedMovement(float distToPlayer)
    {
        // Placeholder stub for Ranged behavior if mixed
    }

    public void DealMeleeDamageToPlayer(float damage, float knockback, int attackID, AudioClip hitSound, float stunDuration)
    {
        if (player != null && player.TryGetComponent<IDamageable>(out var playerDamageable))
        {
            Vector3 hitDir = (player.transform.position - cachedTransform.position).normalized;
            if (playerDamageable is PlayerHealth ph)
            {
                ph.TakeDamage(damage, cachedTransform.position, hitDir, knockback, hitSound, attackID, false, stunDuration);
            }
            else
            {
                playerDamageable.TakeDamage(damage, cachedTransform.position, hitDir, knockback, hitSound, attackID, false);
            }
        }
    }

    public void DealMeleeDamageToPlayer()
    {
        // Add a default fallback hash just in case
        DealMeleeDamageToPlayer(meleeDamage, meleeKnockbackForce, Animator.StringToHash("Hit_Default"), meleeHitSound, 0.4f);
    }

    private void FacePlayer(Vector3 playerPos, Vector3 currentPos, float speedMultiplier = 1.0f)
    {
        Vector3 dirToPlayer = playerPos - currentPos;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dirToPlayer);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRotation, (rotationSpeed * speedMultiplier) * Time.deltaTime);
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        ResetTimeScale();

        // --- INTERRUPT ATTACK CHAIN & KILL FORWARD MOMENTUM ON HIT ---
        if (activeEnemyComboRoutine != null)
        {
            StopCoroutine(activeEnemyComboRoutine);
            activeEnemyComboRoutine = null;
            currentMeleeState = MeleeBehaviorState.ArcStrafe;
            arcTimer = Random.Range(1.0f, 2.0f);
            SetPlayerComboLock(false);

            // Instantly cancel forward lunge momentum
            cachedTransform.position -= cachedTransform.forward * 0.3f;
        }

        // Remember the current incoming attack so the enemy doesn't immediately dodge the tail-end of it
        AttackData incomingAttack = GetActiveAttackData(player);
        if (incomingAttack != null)
        {
            lastProcessedAttackData = incomingAttack;
        }

        if (IsDodging)
        {
            if (activeDodgeRoutine != null)
            {
                StopCoroutine(activeDodgeRoutine);
                activeDodgeRoutine = null;
            }
            ForceResetDodgeState();
        }

        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }

        int finalDamage = Mathf.RoundToInt(damage);
        currentHealth -= finalDamage;

        PlayHitReaction(attackID, hitDirection, finalDamage);

        float knockbackDist = force;
        AttackData activeAttack = GetActiveAttackData(player);
        if (activeAttack != null)
        {
            knockbackDist = activeAttack.knockbackForce;
        }

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
            isReacting = true;
            reactionTimer = hitAnim.clip.length / Mathf.Max(0.01f, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction"; 
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        IsDodging = false;
        isReacting = false;
        ResetTimeScale();

        if (activeDodgeRoutine != null)
        {
            StopCoroutine(activeDodgeRoutine);
            activeDodgeRoutine = null;
        }
        if (activeKnockbackRoutine != null)
        {
            StopCoroutine(activeKnockbackRoutine);
            activeKnockbackRoutine = null;
        }
        if (activeEnemyComboRoutine != null)
        {
            StopCoroutine(activeEnemyComboRoutine);
            activeEnemyComboRoutine = null;
        }

        SetPlayerComboLock(false);

        ForceResetDodgeState();

        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(cachedTransform);
        }

        if (animProfile != null && animProfile.deathClip != null && animationEngine == null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        // 1. Drop the body to the ground if they died mid-air
        Vector3 startPos = cachedTransform.position;
        Vector3 groundPos = new Vector3(startPos.x, groundYCoord, startPos.z);
        float dropElapsed = 0f;
        float dropDuration = 0.2f;

        while (dropElapsed < dropDuration)
        {
            dropElapsed += Time.unscaledDeltaTime;
            cachedTransform.position = Vector3.Lerp(startPos, groundPos, dropElapsed / dropDuration);
            yield return null;
        }
        cachedTransform.position = groundPos;

        // 2. Wait for the disappear delay
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

    private void ExecuteUltraInstinctReflex(int attackID)
    {
        if (IsDodging) return;

        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }

        IsDodging = true;
        if (capsuleCollider != null) capsuleCollider.enabled = false;

        DodgeDirection targetDirection = DodgeDirection.DodgeRight; 
        float targetDelay = 0f;
        float targetDuration = 0.2f;
        bool foundMapping = false;

        for (int i = 0; i < dodgeMappings.Count; i++)
        {
            if (dodgeMappings[i].attackID == attackID)
            {
                targetDirection = dodgeMappings[i].direction;
                targetDelay = dodgeMappings[i].reactionDelay;
                targetDuration = dodgeMappings[i].dodgeDuration > 0f ? dodgeMappings[i].dodgeDuration : 0.2f; 
                foundMapping = true;
                break;
            }
        }

        SpecificDodgeData specificDodge = dodgeProfile != null ? dodgeProfile.GetSpecificDodge(attackID) : null;
        AnimationClip dodgeClip = specificDodge != null ? specificDodge.dodgeClip : (dodgeProfile != null ? dodgeProfile.defaultDodgeClip : null);
        float transition = specificDodge != null ? specificDodge.transitionDuration : (dodgeProfile != null ? dodgeProfile.defaultTransitionDuration : 0.05f);
        float speed = specificDodge != null ? specificDodge.playbackSpeed : (dodgeProfile != null ? dodgeProfile.defaultPlaybackSpeed : 1.0f);

        if (animationEngine != null && dodgeClip != null)
        {
            animationEngine.PlayAnimation(dodgeClip, transition, speed);
            lastPlayedLocomotionState = "Dodge";
        }

        activeDodgeRoutine = StartCoroutine(PerformDodgeRoutine(targetDirection, targetDelay, targetDuration, foundMapping));
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
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            if (playerTransform == null && player != null) playerTransform = player.transform;
            if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position);

            cachedTransform.position = Vector3.Lerp(startPos, targetPos, smoothProgress);
            yield return null;
        }

        cachedTransform.position = targetPos;
        activeKnockbackRoutine = null;
    }

    private IEnumerator PerformDodgeRoutine(DodgeDirection direction, float delay, float duration, bool hasMapping)
    {
        Vector3 startPos = cachedTransform.position;
        groundYCoord = startPos.y;

        if (delay > 0f)
        {
            float delayElapsed = 0f;
            while (delayElapsed < delay)
            {
                delayElapsed += Time.unscaledDeltaTime;
                if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position, dodgeFacingSpeedMultiplier);
                yield return null;
            }
        }

        Vector3 targetPos = startPos;
        Vector3 pPos = playerTransform != null ? playerTransform.position : startPos;
        Vector3 dirToPlayer = (pPos - cachedTransform.position).normalized;
        dirToPlayer.y = 0f;
        
        Vector3 rightDir = Vector3.Cross(Vector3.up, dirToPlayer).normalized;

        if (direction == DodgeDirection.DodgeLeft)
        {
            targetPos = startPos - (rightDir * dodgeDistance);
        }
        else if (direction == DodgeDirection.DodgeRight)
        {
            targetPos = startPos + (rightDir * dodgeDistance);
        }
        else if (direction == DodgeDirection.Backstep)
        {
            targetPos = startPos - (dirToPlayer * dodgeDistance);
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float smoothProgress = Mathf.SmoothStep(0f, 1f, progress);

            if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position, dodgeFacingSpeedMultiplier);

            if (direction == DodgeDirection.Jump)
            {
                float heightCurve = Mathf.Sin(progress * Mathf.PI) * jumpHeight;
                cachedTransform.position = new Vector3(startPos.x, groundYCoord + heightCurve, startPos.z);
            }
            else
            {
                cachedTransform.position = Vector3.Lerp(startPos, targetPos, smoothProgress);
            }

            yield return null;
        }

        if (direction == DodgeDirection.Jump)
        {
            cachedTransform.position = new Vector3(startPos.x, groundYCoord, groundYCoord);
        }
        else
        {
            cachedTransform.position = targetPos;
        }

        if (postDodgeBuffer > 0f)
        {
            float bufferElapsed = 0f;
            while (bufferElapsed < postDodgeBuffer)
            {
                bufferElapsed += Time.unscaledDeltaTime;
                if (playerTransform != null) FacePlayer(playerTransform.position, cachedTransform.position, dodgeFacingSpeedMultiplier);
                yield return null;
            }
        }

        if (capsuleCollider != null) capsuleCollider.enabled = true;
        IsDodging = false;
        activeDodgeRoutine = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        
        Vector3 boxCenter = transform.position + (transform.forward * (playerDodgeRange * 0.5f));
        Vector3 boxSize = new Vector3(attackCorridorWidth * 2f, 0.1f, playerDodgeRange);
        Gizmos.matrix = Matrix4x4.TRS(boxCenter, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, boxSize);
        Gizmos.matrix = oldMatrix;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, reflexRange);
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