using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using CombatSystem.Data; 
using CombatSystem.Animation;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class MinionEnemyBrain : BaseEnemyBrain, IDamageable, IHealable
{
    private MinionEnemyDataSO MinionData => enemyData as MinionEnemyDataSO;
    private EnemyAnimationEngine animationEngine;

    [Header("Animation Profiles & Direct Inspector Clips")]
    public EnemyAnimProfile animProfile;
    [Tooltip("Assign your Death animation clip directly here for a guaranteed zero-fail trigger.")]
    public AnimationClip directDeathClip;
    [Tooltip("Assign your default Hit/Flinch animation clip directly here.")]
    public AnimationClip directHitClip;

    [Header("Health & Combat Settings")]
    public float maxHealth = 60f;
    private float currentHealth;
    private bool isDead = false;
    private CharacterController charController;
    private bool isAttacking = false;
    
    // Coroutine & State Trackers
    private Coroutine activeKnockbackRoutine;
    private Coroutine activeHitRoutine;
    private Coroutine activeAttackRoutine;
    private bool isReacting = false;

    [Header("Dual-Ring Swarm Manager")]
    [Tooltip("Distance the audience keeps while waiting for an Engage slot.")]
    public float outerRingDistance = 8.0f;
    private bool holdsEngageToken = false;
    private bool holdsOuterRingToken = false;
    
    // Smooth Orbit & Horde Variables
    private float orbitSpeed = 1.8f;
    private int orbitDir = 1;
    private float orbitTimer = 0f;
    private bool isDisengaging = false;
    private float randomizedOuterDistanceOffset = 0f;
    private float randomizedSeparationWeight = 3.5f;

    [Header("Patrol Area (Stealth Phase)")]
    public PatrolZone assignedZone;
    public float patrolSpeed = 2.0f;
    public float waypointWaitTime = 2.0f;
    private Vector3 currentPatrolTarget;
    private bool isWaitingAtWaypoint = false;
    private bool hasSpottedPlayer = false; 
    private float groundYCoord;

    [Header("Patrol Bump Mechanics")]
    public float bumpTurnRadius = 1.0f;
    public float bumpTurnDistance = 4.0f;
    private float bumpCooldownTimer = 0f;

    [Header("Seamless Obstacle Avoidance (Whiskers)")]
    public float obstacleAvoidanceDistance = 3.0f;
    public float avoidanceWeight = 5.0f;
    public LayerMask obstacleMask;

    [Header("Swarm Coordination (Separation)")]
    public float separationRadius = 2.2f;
    public float separationWeight = 4.5f;
    public LayerMask enemyLayerMask;
    private static readonly Collider[] separationBuffer = new Collider[16];

    [Header("Vision & Detection Math")]
    public float visionRange = 10f;
    [Range(-1f, 1f)] public float visionFOVThreshold = 0.5f;

    [Header("Alert & Visual Feedback")]
    public GameObject exclamationMarkVisual;
    public float alertFreezeDuration = 0.6f;
    private bool isAlerting = false;

    private static float globalLastAttackTime = 0f;
    private const float MIN_ATTACK_STAGGER_DELAY = 0.6f; 

    private AnimationClip lastPlayedClip = null;

    protected override void Awake()
    {
        base.Awake();
        charController = GetComponent<CharacterController>();
        animationEngine = GetComponent<EnemyAnimationEngine>();
        currentHealth = maxHealth;
        groundYCoord = transform.position.y;

        if (animProfile != null) animProfile.InitializeDictionary();

        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(false);
            exclamationMarkVisual.transform.localScale = Vector3.zero;
        }
    }

    protected void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        isAttacking = false;
        hasSpottedPlayer = false;
        isAlerting = false;
        isWaitingAtWaypoint = false;
        holdsEngageToken = false;
        holdsOuterRingToken = false;
        isDisengaging = false;
        isReacting = false;
        
        StopAllCoroutines();
        activeKnockbackRoutine = null;
        activeHitRoutine = null;
        activeAttackRoutine = null;
        bumpCooldownTimer = 0f;
        lastPlayedClip = null;

        groundYCoord = transform.position.y;
        randomizedOuterDistanceOffset = Random.Range(-1.5f, 1.5f);
        randomizedSeparationWeight = separationWeight + Random.Range(-0.5f, 0.5f);

        orbitTimer = Random.Range(3.0f, 7.0f);
        orbitDir = Random.value > 0.5f ? 1 : -1;
        orbitSpeed = Random.Range(1.2f, 2.2f);

        if (charController != null) charController.enabled = true;
        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(false);
            exclamationMarkVisual.transform.localScale = Vector3.zero;
        }

        if (assignedZone != null)
            currentPatrolTarget = assignedZone.GetValidPatrolPoint(gameObject.GetInstanceID(), transform.position);
        else
            currentPatrolTarget = transform.position;
    }

    protected override void Update()
    {
        if (isDead || target == null) return;
        
        if (isReacting || isAttacking) return; 

        if (isDisengaging)
        {
            HandleDisengage();
            return;
        }

        if (!hasSpottedPlayer)
        {
            HandleStealthPhase();
            return; 
        }

        base.Update(); 

        if (currentState == AIState.Attack && activeAttackRoutine == null)
        {
            activeAttackRoutine = StartCoroutine(ExecuteMinionAttackRoutine());
        }
    }

    private void PlayCleanAnimation(string animKey, float transitionTime = 0.2f, float speedMultiplier = 1.0f)
    {
        if (animationEngine == null || isDead) return;

        AnimationClip clip = null;
        switch (animKey)
        {
            case "Idle": clip = animProfile != null ? animProfile.idleClip : null; break;
            case "Walk": clip = animProfile != null ? animProfile.walkClip : null; break;
            case "Attack": 
                if (selectedAttack != null && !string.IsNullOrEmpty(selectedAttack.AnimationClipName) && animProfile != null)
                    clip = animProfile.GetAnimationClip(selectedAttack.AnimationClipName);
                if (clip == null && animProfile != null && animProfile.attackReactions != null && animProfile.attackReactions.Count > 0)
                    clip = animProfile.attackReactions[0].reactionFront?.clip;
                break;
        }

        if (clip != null && lastPlayedClip != clip)
        {
            lastPlayedClip = clip;
            animationEngine.PlayAnimation(clip, transitionTime, speedMultiplier);
        }
    }

    protected override void HandleChaseState(float sqrDistToTarget)
    {
        if (isDead || MinionData == null) return;

        if (!holdsEngageToken && GlobalTokenManager.Instance != null)
        {
            holdsEngageToken = GlobalTokenManager.Instance.RequestToken(transform, TokenType.Engage);
            
            if (holdsEngageToken && holdsOuterRingToken)
            {
                GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.OuterRing);
                holdsOuterRingToken = false;
            }
        }

        if (!holdsEngageToken && !holdsOuterRingToken && GlobalTokenManager.Instance != null)
        {
            holdsOuterRingToken = GlobalTokenManager.Instance.RequestToken(transform, TokenType.OuterRing);
        }

        Vector3 dirToTarget = (target.position - transform.position).normalized;
        dirToTarget.y = 0f;

        if (holdsEngageToken)
        {
            if (sqrDistToTarget <= MinionData.AttackStopDistance * MinionData.AttackStopDistance)
            {
                currentState = AIState.RequestToken;
                tokenWaitTimer = 0f;
            }
            else
            {
                Vector3 desiredMove = dirToTarget * MinionData.MoveSpeed;
                Vector3 avoidanceForce = CalculateObstacleAvoidance();
                Vector3 separationForce = CalculateSeparationForce();
                
                Vector3 finalMoveDir = desiredMove + (avoidanceForce * avoidanceWeight) + separationForce;

                if (charController != null && charController.enabled)
                    charController.Move(finalMoveDir * Time.deltaTime + (Vector3.up * verticalVelocity * Time.deltaTime));

                if (finalMoveDir != Vector3.zero)
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalMoveDir.normalized), MinionData.RotationSpeed * Time.deltaTime);
                
                PlayCleanAnimation("Walk", 0.2f);
            }
            return;
        }

        if (holdsOuterRingToken)
        {
            float distToTarget = Mathf.Sqrt(sqrDistToTarget);
            float effectiveTargetDistance = outerRingDistance + randomizedOuterDistanceOffset;
            float distError = distToTarget - effectiveTargetDistance;
            
            orbitTimer -= Time.deltaTime;
            if (orbitTimer <= 0f)
            {
                orbitTimer = Random.Range(3.0f, 6.0f);
                orbitDir = Random.value > 0.5f ? 1 : -1;
                orbitSpeed = Random.Range(1.0f, 2.0f);
            }
            
            Vector3 distanceCorrection = dirToTarget * distError;
            Vector3 orbitalMove = Vector3.Cross(Vector3.up, dirToTarget).normalized * (orbitSpeed * orbitDir);
            
            Vector3 avoidanceForce = CalculateObstacleAvoidance();
            Vector3 separationForce = CalculateSeparationForce();
            
            Vector3 finalMoveDir = distanceCorrection + orbitalMove + (avoidanceForce * avoidanceWeight) + separationForce;

            if (charController != null && charController.enabled)
                charController.Move(finalMoveDir * Time.deltaTime + (Vector3.up * verticalVelocity * Time.deltaTime));

            if (dirToTarget != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dirToTarget), MinionData.RotationSpeed * Time.deltaTime);

            Vector3 horizontalVelocity = new Vector3(charController.velocity.x, 0f, charController.velocity.z);
            if (horizontalVelocity.magnitude > 0.15f)
            {
                PlayCleanAnimation("Walk", 0.2f);
            }
            else
            {
                PlayCleanAnimation("Idle", 0.2f);
            }
                
            return; 
        }

        if (!holdsEngageToken && !holdsOuterRingToken)
        {
            isDisengaging = true;
        }
    }

    protected override void HandleRequestTokenState(float distanceToTarget)
    {
        if (isDead || MinionData == null || MinionData.AvailableAttacks == null || MinionData.AvailableAttacks.Count == 0) return;
        selectedAttack = MinionData.AvailableAttacks[Random.Range(0, MinionData.AvailableAttacks.Count)];

        if (Time.time < globalLastAttackTime + MIN_ATTACK_STAGGER_DELAY)
        {
            ExecuteOrganicCircling();
            return;
        }

        if (GlobalTokenManager.Instance != null)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
            if (hasToken)
            {
                globalLastAttackTime = Time.time;
                currentState = AIState.Attack;
                stateTimer = selectedAttack.StartupTime + selectedAttack.ActiveTime + selectedAttack.RecoveryTime;
            }
            else
            {
                ExecuteOrganicCircling();
            }
        }
        else
        {
            currentState = AIState.Attack;
        }
    }

    private void ExecuteOrganicCircling()
    {
        if (isDead) return;
        Vector3 tangent = Vector3.Cross(Vector3.up, (target.position - transform.position)).normalized;
        Vector3 desiredMove = tangent * (MinDataMoveSpeedSafe() * 0.5f * orbitDir);
        Vector3 avoidanceForce = CalculateObstacleAvoidance();
        Vector3 separationForce = CalculateSeparationForce();
        Vector3 finalMoveDir = desiredMove + (avoidanceForce * avoidanceWeight) + separationForce;

        if (charController != null && charController.enabled)
            charController.Move(finalMoveDir * Time.deltaTime + (Vector3.up * verticalVelocity * Time.deltaTime));

        FaceTarget();
        
        Vector3 horizontalVelocity = new Vector3(charController.velocity.x, 0f, charController.velocity.z);
        if (horizontalVelocity.magnitude > 0.15f)
            PlayCleanAnimation("Walk", 0.2f);
        else
            PlayCleanAnimation("Idle", 0.2f);
    }

    private float MinDataMoveSpeedSafe() => MinionData != null ? MinionData.MoveSpeed : 3.5f;

    private IEnumerator ExecuteMinionAttackRoutine()
    {
        isAttacking = true;
        PlayCleanAnimation("Attack", 0.05f);

        if (selectedAttack == null || isDead) 
        {
            isAttacking = false;
            activeAttackRoutine = null;
            yield break;
        }

        float startup = selectedAttack.StartupTime;
        float elapsed = 0f;
        while (elapsed < startup)
        {
            if (isDead || target == null) yield break;
            elapsed += Time.deltaTime;
            FaceTarget();
            yield return null;
        }

        float activeTime = selectedAttack.ActiveTime;
        elapsed = 0f;
        bool damageDealt = false;

        while (elapsed < activeTime)
        {
            if (isDead || target == null) yield break;
            elapsed += Time.deltaTime;
            FaceTarget();

            if (charController != null && charController.enabled)
                charController.Move(transform.forward * (MinDataMoveSpeedSafe() * 1.4f) * Time.deltaTime);

            if (!damageDealt && target != null && Vector3.Distance(transform.position, target.position) <= 2.2f) 
            {
                DealDamageToPlayer();
                damageDealt = true;
            }

            yield return null;
        }

        yield return new WaitForSeconds(selectedAttack.RecoveryTime);

        if (!isDead && GlobalTokenManager.Instance != null)
        {
            if (hasToken)
            {
                GlobalTokenManager.Instance.ReleaseToken(transform, selectedAttack.RequiredTokenType);
                hasToken = false;
            }

            if (holdsEngageToken)
            {
                GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.Engage);
                holdsEngageToken = false;
            }

            holdsOuterRingToken = GlobalTokenManager.Instance.RequestToken(transform, TokenType.OuterRing);
        }

        isAttacking = false;
        activeAttackRoutine = null;
        currentState = AIState.Chase;
    }

    private Vector3 CalculateSeparationForce()
    {
        Vector3 separationForce = Vector3.zero;
        int hits = Physics.OverlapSphereNonAlloc(transform.position, separationRadius, separationBuffer, enemyLayerMask);

        for (int i = 0; i < hits; i++)
        {
            Collider peer = separationBuffer[i];
            if (peer == null || peer.gameObject == gameObject) continue; 

            Vector3 awayFromPeer = transform.position - peer.transform.position;
            awayFromPeer.y = 0f; 
            
            float sqrDistance = awayFromPeer.sqrMagnitude;
            if (sqrDistance > 0.0001f) 
            {
                separationForce += (awayFromPeer.normalized / Mathf.Sqrt(sqrDistance)) * randomizedSeparationWeight;
            }
        }

        return separationForce;
    }

    private void HandleDisengage()
    {
        if (isDead) return;
        Vector3 awayFromPlayer = (transform.position - target.position).normalized;
        awayFromPlayer.y = 0f;
        
        Vector3 avoidanceForce = CalculateObstacleAvoidance();
        Vector3 finalMoveDir = (awayFromPlayer + (avoidanceForce * avoidanceWeight)).normalized;
        
        if (charController != null && charController.enabled)
            charController.Move(finalMoveDir * patrolSpeed * Time.deltaTime + (Vector3.up * verticalVelocity * Time.deltaTime));

        if (finalMoveDir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalMoveDir), 10f * Time.deltaTime);
            
        PlayCleanAnimation("Walk", 0.2f);

        float distToPlayer = Vector3.Distance(transform.position, target.position);
        if (distToPlayer > visionRange + 3f)
        {
            isDisengaging = false;
            hasSpottedPlayer = false;
            
            if (assignedZone != null)
                currentPatrolTarget = assignedZone.GetValidPatrolPoint(gameObject.GetInstanceID(), transform.position);
        }
        
        if (GlobalTokenManager.Instance != null && distToPlayer <= visionRange + 3f)
        {
            if (GlobalTokenManager.Instance.RequestToken(transform, TokenType.OuterRing))
            {
                holdsOuterRingToken = true;
                isDisengaging = false;
                currentState = AIState.Chase;
            }
        }
    }

    // ==========================================
    // SEAMLESS OBSTACLE AVOIDANCE (ANY OBSTACLE LAYER)
    // ==========================================
    private Vector3 CalculateObstacleAvoidance()
    {
        Vector3 avoidanceForce = Vector3.zero;
        Vector3 origin = transform.position + (Vector3.up * 0.4f);

        // 5 Wide-Angle Whiskers to instantly detect and slide around any obstacle layer collider
        float[] angles = { 0f, -30f, 30f, -60f, 60f };

        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 dir = Quaternion.Euler(0, angles[i], 0) * transform.forward;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, obstacleAvoidanceDistance, obstacleMask))
            {
                // Calculate push-away force based on proximity and surface normal
                float closeness = 1.0f - (hit.distance / obstacleAvoidanceDistance);
                Vector3 slideDir = Vector3.ProjectOnPlane(dir, hit.normal).normalized;
                
                // Blend normal push to veer away and tangent slide to walk smoothly around it
                avoidanceForce += (hit.normal * 1.5f + slideDir) * closeness;
            }
        }

        avoidanceForce.y = 0f;
        return avoidanceForce;
    }

    // ==========================================
    // BULLETPROOF DAMAGE & DEATH PIPELINE
    // ==========================================
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        currentHealth -= damage;

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        hasSpottedPlayer = true; 
        isDisengaging = false; 

        if (assignedZone != null) assignedZone.ReleasePoint(gameObject.GetInstanceID());

        if (isAttacking || activeAttackRoutine != null)
        {
            if (activeAttackRoutine != null) { StopCoroutine(activeAttackRoutine); activeAttackRoutine = null; }
            isAttacking = false;
            isWaitingAtWaypoint = false;
            isAlerting = false;
            if (exclamationMarkVisual != null) exclamationMarkVisual.SetActive(false);
            
            if (GlobalTokenManager.Instance != null && holdsEngageToken)
            {
                GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.Engage);
                holdsEngageToken = false;
            }
        }

        if (activeKnockbackRoutine != null) StopCoroutine(activeKnockbackRoutine);
        if (charController != null && charController.enabled)
            activeKnockbackRoutine = StartCoroutine(ApplyKnockback(hitDirection, force));

        if (activeHitRoutine != null) StopCoroutine(activeHitRoutine);
        activeHitRoutine = StartCoroutine(HitReactionRoutine());
    }

    private IEnumerator HitReactionRoutine()
    {
        if (isDead) yield break;
        isReacting = true;
        
        AnimationClip hitClipToPlay = directHitClip != null ? directHitClip : (animProfile != null ? animProfile.defaultHitFront?.clip : null);
        if (hitClipToPlay != null && animationEngine != null)
        {
            lastPlayedClip = hitClipToPlay;
            animationEngine.PlayAnimation(hitClipToPlay, 0.01f, 1.0f);
        }
        
        yield return new WaitForSeconds(0.4f); 
        
        if (!isDead)
        {
            isReacting = false;
            lastPlayedClip = null; 
            if (currentState == AIState.Attack) currentState = AIState.Chase; 
        }
        activeHitRoutine = null;
    }

    private IEnumerator ApplyKnockback(Vector3 hitDirection, float force)
    {
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -transform.forward;
        pushDir.Normalize();

        float elapsed = 0f;
        float duration = 0.12f;
        while (elapsed < duration && !isDead)
        {
            elapsed += Time.deltaTime;
            if (charController != null && charController.enabled)
                charController.Move(pushDir * (force * Time.deltaTime / duration));
            yield return null;
        }
        activeKnockbackRoutine = null;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true; 

        StopAllCoroutines();

        if (assignedZone != null) assignedZone.ReleasePoint(gameObject.GetInstanceID());
        if (GlobalTokenManager.Instance != null) GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);
        
        if (charController != null) charController.enabled = false; 
        if (exclamationMarkVisual != null) exclamationMarkVisual.SetActive(false);

        AnimationClip deathClipToPlay = directDeathClip != null ? directDeathClip : (animProfile != null ? animProfile.deathClip : null);
        if (deathClipToPlay != null && animationEngine != null)
        {
            lastPlayedClip = deathClipToPlay;
            animationEngine.PlayAnimation(deathClipToPlay, 0f, animProfile != null ? animProfile.deathPlaybackSpeed : 1.0f);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(2.5f); 

        if (EnemyObjectPool.Instance != null) 
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        else 
            gameObject.SetActive(false);
    }

    private void HandleStealthPhase()
    {
        if (isDead || isAlerting) return; 

        if (CanSeePlayer())
        {
            StartCoroutine(AlertSequenceRoutine());
            return;
        }

        if (assignedZone == null)
        {
            PlayCleanAnimation("Idle", 0.2f);
            return;
        }

        if (bumpCooldownTimer > 0f) bumpCooldownTimer -= Time.deltaTime;

        if (bumpCooldownTimer <= 0f && !isWaitingAtWaypoint)
        {
            if (CheckAndHandlePeerBump()) return; 
        }

        Vector3 dirToTarget = currentPatrolTarget - transform.position;
        dirToTarget.y = 0f;
        float distToTarget = dirToTarget.magnitude;

        if (distToTarget < 0.5f)
        {
            if (!isWaitingAtWaypoint) StartCoroutine(WaypointWaitRoutine());
        }
        else if (!isWaitingAtWaypoint)
        {
            Vector3 desiredDir = dirToTarget.normalized;
            Vector3 avoidanceForce = CalculateObstacleAvoidance();
            Vector3 separationForce = CalculateSeparationForce();
            Vector3 finalMoveDir = (desiredDir + (avoidanceForce * avoidanceWeight) + separationForce).normalized;

            Vector3 targetMoveVector = finalMoveDir * patrolSpeed * Time.deltaTime;
            Vector3 currentPos = transform.position;
            currentPos.y = groundYCoord;
            transform.position = currentPos;

            if (charController != null && charController.enabled)
                charController.Move(targetMoveVector + (Vector3.up * verticalVelocity * Time.deltaTime));
            
            if (finalMoveDir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalMoveDir), 10f * Time.deltaTime);

            PlayCleanAnimation("Walk", 0.2f);
        }
    }

    private bool CheckAndHandlePeerBump()
    {
        if (isDead) return false;
        int hits = Physics.OverlapSphereNonAlloc(transform.position, bumpTurnRadius, separationBuffer, enemyLayerMask);
        
        for (int i = 0; i < hits; i++)
        {
            Collider peer = separationBuffer[i];
            if (peer == null || peer.gameObject == gameObject) continue;

            MinionEnemyBrain peerBrain = peer.GetComponent<MinionEnemyBrain>();
            if (peerBrain == null || peerBrain.IsSpottedOrAlerting()) continue; 

            float myClearance = GetClearanceBehind(transform);
            float peerClearance = GetClearanceBehind(peer.transform);

            bool iShouldTurn = myClearance > peerClearance;
            if (Mathf.Abs(myClearance - peerClearance) < 0.1f) 
                iShouldTurn = gameObject.GetInstanceID() > peer.gameObject.GetInstanceID();

            if (iShouldTurn && myClearance > 1.0f) 
            {
                Execute180BumpTurn(myClearance);
                return true; 
            }
        }
        return false;
    }

    private float GetClearanceBehind(Transform t)
    {
        Vector3 origin = t.position + (Vector3.up * 0.4f); 
        if (Physics.Raycast(origin, -t.forward, out RaycastHit hit, bumpTurnDistance, obstacleMask))
            return hit.distance;
        return bumpTurnDistance; 
    }

    private void Execute180BumpTurn(float availableSpace)
    {
        if (isDead) return;
        if (assignedZone != null) assignedZone.ReleasePoint(gameObject.GetInstanceID());
        transform.rotation = Quaternion.LookRotation(-transform.forward);
        float safeMoveDistance = Mathf.Min(bumpTurnDistance, availableSpace - 0.5f); 
        currentPatrolTarget = transform.position + (transform.forward * safeMoveDistance);
        bumpCooldownTimer = 2.0f;
    }

    public bool IsSpottedOrAlerting()
    {
        return hasSpottedPlayer || isAlerting || currentState == AIState.Chase || currentState == AIState.Attack || isDisengaging;
    }

    private bool CanSeePlayer()
    {
        Vector3 dirToTarget = target.position - transform.position;
        float distToTarget = dirToTarget.magnitude;

        if (distToTarget > visionRange) return false;

        float dotProduct = Vector3.Dot(transform.forward, dirToTarget.normalized);
        if (dotProduct < visionFOVThreshold) return false;

        Vector3 eyeLevelPos = transform.position + Vector3.up * 1.5f; 
        Vector3 targetEyeLevelPos = target.position + Vector3.up * 1.5f;

        if (Physics.Raycast(eyeLevelPos, (targetEyeLevelPos - eyeLevelPos).normalized, out RaycastHit hit, visionRange, obstacleMask))
            return false; 

        return true;
    }

    private IEnumerator WaypointWaitRoutine()
    {
        isWaitingAtWaypoint = true;
        PlayCleanAnimation("Idle", 0.2f);

        yield return new WaitForSeconds(waypointWaitTime);
        if (!isDead)
        {
            currentPatrolTarget = assignedZone.GetValidPatrolPoint(gameObject.GetInstanceID(), transform.position);
            isWaitingAtWaypoint = false;
        }
    }

    private IEnumerator AlertSequenceRoutine()
    {
        isAlerting = true;
        if (assignedZone != null) assignedZone.ReleasePoint(gameObject.GetInstanceID());

        FaceTarget();
        PlayCleanAnimation("Idle", 0.1f); 

        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(true);
            float elapsed = 0f;
            float popDuration = 0.15f;

            while (elapsed < popDuration && !isDead)
            {
                elapsed += Time.deltaTime;
                float scale = Mathf.LerpUnclamped(0f, 1.2f, elapsed / popDuration);
                exclamationMarkVisual.transform.localScale = new Vector3(scale, scale, scale);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < 0.1f && !isDead)
            {
                elapsed += Time.deltaTime;
                float scale = Mathf.LerpUnclamped(1.2f, 1.0f, elapsed / 0.1f);
                exclamationMarkVisual.transform.localScale = new Vector3(scale, scale, scale);
                yield return null;
            }
        }

        yield return new WaitForSeconds(alertFreezeDuration);

        if (!isDead)
        {
            if (exclamationMarkVisual != null) exclamationMarkVisual.SetActive(false);
            currentState = AIState.Chase; 
            hasSpottedPlayer = true;      
            isAlerting = false;
        }
    }

    private void FaceTarget()
    {
        if (isDead || target == null) return;
        Vector3 dir = (target.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), MinionData.RotationSpeed * Time.deltaTime);
    }

    private void DealDamageToPlayer()
    {
        if (isDead) return;
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

    public bool NeedsHealing() => currentHealth < maxHealth && !isDead;
    public void ReceiveHeal(float amount) { if (!isDead) currentHealth = Mathf.Min(maxHealth, currentHealth + amount); }
    public Transform GetTransform() => transform;
}