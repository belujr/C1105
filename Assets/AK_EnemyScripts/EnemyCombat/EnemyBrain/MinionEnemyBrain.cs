using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;
using CombatSystem.Data;
using CombatSystem.Animation;

// PART 1/3 - Core. Other parts: MinionEnemyBrain.Perception.cs, MinionEnemyBrain.Patrol.cs (same folder).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public partial class MinionEnemyBrain : BaseEnemyBrain, IDamageable, IHealable
{
    public enum MinionState { Unaware, Suspicious, Chase, Orbit, Engage, Stun, Dead, Recover, Search }
    public MinionState CurrentState { get; protected set; } // [R2]

    protected enum AnimKey { None, Idle, Walk, StrafeLeft, StrafeRight, Backpedal, Attack, Hit, Death } // [R2]

    protected MinionEnemyDataSO MinionData => enemyData as MinionEnemyDataSO; // [R2]
    protected EnemyAnimationEngine minionAnim; // [R2]
    protected float MoveSpd => MinionData != null ? MinionData.MoveSpeed : 3.5f; // [R2]
    protected float StopDist => MinionData != null ? MinionData.AttackStopDistance : 2f; // [R2]

    // ------------------------------------------------------------------ Inspector (existing names kept)
    [Header("Animation Profiles & Direct Inspector Clips")]
    public EnemyAnimProfile animProfile;
    public AnimationClip directDeathClip;
    public AnimationClip directHitClip;

    [Header("Health & Combat Settings")]
    public float maxHealth = 60f;
    protected float currentHealth; // [R2]

    [Header("Dual-Ring Swarm Manager")]
    public float outerRingDistance = 8.0f;

    [Header("Patrol Area (Stealth Phase)")]
    public PatrolZone assignedZone;
    public float patrolSpeed = 2.0f;
    public float waypointWaitTime = 2.0f;

    [Header("Patrol Peer Avoidance (was Bump Mechanics)")]
    public float bumpTurnRadius = 1.0f;
    public float bumpTurnDistance = 4.0f;

    [Header("Obstacle Avoidance (sphere cast)")]
    public float obstacleAvoidanceDistance = 3.0f;
    public float avoidanceWeight = 5.0f;
    public LayerMask obstacleMask;

    [Header("Swarm Coordination (Separation)")]
    public float separationRadius = 2.2f;
    public float separationWeight = 4.5f;
    public LayerMask enemyLayerMask;

    [Header("Vision & Detection Math")]
    public float visionRange = 10f;
    [Range(-1f, 1f)] public float visionFOVThreshold = 0.5f;

    [Header("Alert & Visual Feedback")]
    public GameObject exclamationMarkVisual;
    public float alertFreezeDuration = 0.6f;

    // ------------------------------------------------------------------ Inspector (new)
    [Header("Movement Feel")]
    public float turnSpeed = 300f;
    public float acceleration = 12f;
    public float deceleration = 16f;
    public float idleEnterSpeed = 0.15f;
    public float idleExitSpeed = 0.4f;
    public float minLocomotionDwell = 0.25f;

    [Header("Orbit Ring")]
    public float ringBand = 1.0f;
    public float noRingTokenExtraDistance = 2.0f;
    public Vector2 orbitSpeedRange = new Vector2(1.2f, 2.2f);
    public Vector2 orbitSegmentTime = new Vector2(3f, 6f);
    public float angularSpacing = 35f;

    [Header("Breathers (Idle Pauses)")]
    [Range(0f, 1f)] public float orbitPauseChance = 0.5f;
    public Vector2 orbitPauseDuration = new Vector2(0.8f, 1.6f);
    public float recoverBackoffDistance = 2.0f;
    public Vector2 recoverBreathDuration = new Vector2(1.2f, 2.4f);

    [Header("Attack Rotation & Commitment")]
    public Vector2 reEngageCooldown = new Vector2(2.5f, 5f);
    public float engageApproachTimeout = 6f;
    public float attackFacingArc = 70f;
    public float attackTurnSpeed = 220f;
    public float hitReengageDelay = 0.5f;

    // ------------------------------------------------------------------ Runtime
    protected float randomizedSeparationWeight; // [R2]
    protected bool holdsEngageToken, holdsOuterRingToken; // [R2]
    protected float nextTokenRequestTime, engageReadyTime, waitingSince, engageApproachStart, stateEnterTime; // [R2]
    protected float ringOffset, orbitSpeed, orbitSegmentTimer, orbitPauseTimer; // [R2]
    protected int orbitSign = 1; // [R2]
    protected float recoverTimer, breathTimer; // [R2]
    protected bool recoverBreathing; // [R2]

    protected Vector3 velocity, desiredVelocity, desiredFacing, knockVelocity, cachedSeparation; // [R2]
    protected float facingTurnSpeed, knockDecel, nextSeparationTime; // [R2]
    protected int avoidSide = 1; // [R2]

    protected AnimKey currentAnim = AnimKey.None; // [R2]
    protected AnimationClip lastLocoClip; // [R2]
    protected float animChangeTime; // [R2]
    protected bool animLocked; // [R2]
    protected bool warnedMissingAttackClip; // [R2]

    protected Coroutine activeRoutine; // [R2]
    protected Vector3 pendingHitDirection; // [R2]
    protected int pendingAttackId; // [R2]

    protected Vector3 stuckSample; // [R2]
    protected float stuckSampleTime; // [R2]

    protected static readonly List<MinionEnemyBrain> registry = new List<MinionEnemyBrain>(32); // [R2]
    protected static readonly Collider[] separationBuffer = new Collider[16]; // [R2]
    protected static float globalLastAttackTime = 0f; // [R2]
    protected const float MIN_ATTACK_STAGGER_DELAY = 0.6f; // [R2]
    protected const float GROUND_STICK = -4f; // [R2]

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        globalLastAttackTime = 0f;
        registry.Clear();
    }

    protected static Vector3 Flat(Vector3 v) { v.y = 0f; return v; } // [R2]
    protected float GravityY => gravity != 0f ? -Mathf.Abs(gravity) : Physics.gravity.y; // [R2]

    // ------------------------------------------------------------------ Lifecycle
    protected override void Awake()
    {
        base.Awake();
        minionAnim = GetComponent<EnemyAnimationEngine>();
        base.animationEngine = minionAnim;

        if (animProfile != null) animProfile.InitializeDictionary();

        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(false);
            exclamationMarkVisual.transform.localScale = Vector3.zero;
        }
    }

    protected virtual void OnEnable()
    {
        StopAllCoroutines();
        activeRoutine = null;

        currentHealth = maxHealth;
        CurrentState = MinionState.Unaware;
        stateEnterTime = Time.time;

        holdsEngageToken = false;
        holdsOuterRingToken = false;
        hasToken = false;
        selectedAttack = null;

        velocity = desiredVelocity = desiredFacing = knockVelocity = cachedSeparation = Vector3.zero;
        verticalVelocity = 0f;
        facingTurnSpeed = turnSpeed;
        currentAnim = AnimKey.None;
        lastLocoClip = null;
        animLocked = false;
        animChangeTime = -10f;
        recoverBreathing = false;

        ringOffset = Random.Range(-1.5f, 1.5f);
        randomizedSeparationWeight = separationWeight + Random.Range(-0.5f, 0.5f);
        orbitSpeed = Random.Range(orbitSpeedRange.x, orbitSpeedRange.y);
        orbitSign = Random.value > 0.5f ? 1 : -1;
        avoidSide = Random.value > 0.5f ? 1 : -1;
        orbitSegmentTimer = Random.Range(orbitSegmentTime.x, orbitSegmentTime.y);
        orbitPauseTimer = 0f;
        engageReadyTime = 0f;
        waitingSince = Time.time + Random.Range(0f, 0.4f);
        nextTokenRequestTime = 0f;
        nextSeparationTime = 0f;
        stuckSample = transform.position;
        stuckSampleTime = Time.time;

        if (!registry.Contains(this)) registry.Add(this);
        if (characterController != null) characterController.enabled = true;

        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(false);
            exclamationMarkVisual.transform.localScale = Vector3.zero;
        }

        ResetPerception();
        ResetPatrol();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        CleanupTokens();
        registry.Remove(this);
    }

    // ------------------------------------------------------------------ Tokens
    protected virtual void ReleaseAttackToken() // [R2]
    {
        if (hasToken && selectedAttack != null && GlobalTokenManager.Instance != null)
            GlobalTokenManager.Instance.ReleaseToken(transform, selectedAttack.RequiredTokenType);
        hasToken = false;
    }

    protected virtual void CleanupTokens() // [R2]
    {
        if (GlobalTokenManager.Instance != null)
        {
            if (holdsEngageToken) GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.Engage);
            if (holdsOuterRingToken) GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.OuterRing);
        }
        ReleaseAttackToken();
        holdsEngageToken = false;
        holdsOuterRingToken = false;
    }

    protected virtual bool WantsEngage => // [R2]
        isActiveAndEnabled && target != null && !holdsEngageToken &&
        (CurrentState == MinionState.Orbit || CurrentState == MinionState.Chase) &&
        Time.time >= engageReadyTime;

    protected virtual bool IsNextInLine() // [R2]
    {
        int myId = GetInstanceID();
        for (int i = 0; i < registry.Count; i++)
        {
            MinionEnemyBrain o = registry[i];
            if (o == null || o == this || !o.WantsEngage) continue;
            if (o.waitingSince < waitingSince || (o.waitingSince == waitingSince && o.GetInstanceID() < myId)) return false;
        }
        return true;
    }

    protected virtual void TryAcquireTokens() // [R2]
    {
        if (GlobalTokenManager.Instance == null || Time.time < nextTokenRequestTime) return;
        nextTokenRequestTime = Time.time + 0.2f;

        if (!holdsEngageToken && Time.time >= engageReadyTime && IsNextInLine())
        {
            if (GlobalTokenManager.Instance.RequestToken(transform, TokenType.Engage))
            {
                holdsEngageToken = true;
                engageApproachStart = Time.time;
                if (holdsOuterRingToken)
                {
                    GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.OuterRing);
                    holdsOuterRingToken = false;
                }
                return;
            }
        }

        if (!holdsEngageToken && !holdsOuterRingToken)
            holdsOuterRingToken = GlobalTokenManager.Instance.RequestToken(transform, TokenType.OuterRing);
    }

    protected virtual void GiveUpEngage() // [R2]
    {
        CleanupTokens();
        engageReadyTime = Time.time + Random.Range(1f, 2f);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Orbit);
    }

    // ------------------------------------------------------------------ State machine
    protected virtual void TransitionToState(MinionState newState) // [R2]
    {
        if (CurrentState == MinionState.Dead) return;
        MinionState old = CurrentState;

        if (old == MinionState.Unaware && newState != MinionState.Unaware && assignedZone != null)
            assignedZone.ReleasePoint(gameObject.GetInstanceID());

        if (old == MinionState.Suspicious && exclamationMarkVisual != null)
            exclamationMarkVisual.SetActive(false);

        if (old == MinionState.Engage) ReleaseAttackToken();

        if (newState == MinionState.Unaware || newState == MinionState.Search || newState == MinionState.Suspicious)
            CleanupTokens();

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        animLocked = false;
        CurrentState = newState;
        stateEnterTime = Time.time;
        stuckSample = transform.position;
        stuckSampleTime = Time.time;

        switch (newState)
        {
            case MinionState.Unaware:
                patrolWaiting = false;
                break;
            case MinionState.Suspicious:
                activeRoutine = StartCoroutine(AlertSequenceRoutine());
                break;
            case MinionState.Orbit:
                orbitPauseTimer = 0f;
                orbitSegmentTimer = Random.Range(orbitSegmentTime.x, orbitSegmentTime.y);
                break;
            case MinionState.Engage:
                activeRoutine = StartCoroutine(EngageRoutine());
                break;
            case MinionState.Recover:
                recoverBreathing = false;
                recoverTimer = 0f;
                break;
            case MinionState.Search:
                EnterSearch();
                break;
            case MinionState.Stun:
                activeRoutine = StartCoroutine(HitReactionRoutine());
                break;
            case MinionState.Dead:
                Die();
                break;
        }
    }

    protected override void Update()
    {
        if (CurrentState == MinionState.Dead) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        ApplyMovement(dt);
        UpdateLocomotionAnimation();

        desiredVelocity = Vector3.zero;
        desiredFacing = Vector3.zero;
        facingTurnSpeed = turnSpeed;

        bool targetOk = target != null && target.gameObject.activeInHierarchy;
        if (!targetOk)
        {
            if (CurrentState != MinionState.Unaware && CurrentState != MinionState.Stun) ReturnToPatrol();
            else if (CurrentState == MinionState.Unaware) HandleUnaware(dt);
            return;
        }

        TickPerception(dt);

        switch (CurrentState)
        {
            case MinionState.Unaware: HandleUnaware(dt); break;
            case MinionState.Chase: HandleChase(dt); break;
            case MinionState.Orbit: HandleOrbit(dt); break;
            case MinionState.Recover: HandleRecover(dt); break;
            case MinionState.Search: HandleSearch(dt); break;
        }
    }

    // ------------------------------------------------------------------ Movement core
    protected virtual void ApplyMovement(float dt) // [R2]
    {
        if (characterController == null || !characterController.enabled) return;

        Vector3 want = desiredVelocity; want.y = 0f;
        float rate = want.sqrMagnitude > velocity.sqrMagnitude ? acceleration : deceleration;
        velocity = Vector3.MoveTowards(velocity, want, rate * dt);

        if (knockVelocity.sqrMagnitude > 0.0001f)
            knockVelocity = Vector3.MoveTowards(knockVelocity, Vector3.zero, knockDecel * dt);
        else
            knockVelocity = Vector3.zero;

        if (characterController.isGrounded && verticalVelocity < 0f) verticalVelocity = GROUND_STICK;
        else verticalVelocity = Mathf.Max(verticalVelocity + GravityY * dt, -40f);

        Vector3 move = velocity + knockVelocity;
        move.y = verticalVelocity;
        characterController.Move(move * dt);

        Vector3 face = desiredFacing; face.y = 0f;
        if (face.sqrMagnitude < 0.0001f)
        {
            face = velocity; face.y = 0f;
            if (face.sqrMagnitude < 0.09f) return;
        }
        float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg, facingTurnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    protected void SteerTo(Vector3 goal, float speed, bool faceTarget) // [R2]
    {
        Vector3 to = Flat(goal - transform.position);
        float dist = to.magnitude;
        if (dist < 0.05f) return;

        Vector3 dir = AvoidObstacles(to / dist);
        desiredVelocity = Vector3.ClampMagnitude(dir * speed + GetSeparation(), speed * 1.15f);
        if (faceTarget) desiredFacing = Flat(PerceivedTargetPosition - transform.position);
    }

    protected Vector3 AvoidObstacles(Vector3 dir) // [R2]
    {
        if (obstacleMask.value == 0 || characterController == null) return dir;

        Vector3 origin = transform.position + characterController.center;
        float radius = characterController.radius * 0.95f;
        if (Physics.SphereCast(origin, radius, dir, out RaycastHit hit, obstacleAvoidanceDistance, obstacleMask, QueryTriggerInteraction.Ignore)
            && hit.normal.y < 0.5f)
        {
            float closeness = 1f - Mathf.Clamp01(hit.distance / obstacleAvoidanceDistance);
            Vector3 slide = Flat(Vector3.ProjectOnPlane(dir, hit.normal));
            if (slide.sqrMagnitude < 0.04f) slide = Vector3.Cross(Vector3.up, hit.normal) * avoidSide;
            float t = Mathf.Clamp01(closeness * avoidanceWeight * 0.25f);
            dir = Vector3.Slerp(dir, slide.normalized, t);
        }
        return dir;
    }

    protected Vector3 GetSeparation() // [R2]
    {
        if (Time.time >= nextSeparationTime)
        {
            nextSeparationTime = Time.time + 0.06f + Random.Range(0f, 0.02f);
            Vector3 push = Vector3.zero;
            int hits = Physics.OverlapSphereNonAlloc(transform.position, separationRadius, separationBuffer, enemyLayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
            {
                Collider peer = separationBuffer[i];
                if (peer == null || peer.transform == transform || peer.transform.IsChildOf(transform)) continue;

                Vector3 away = Flat(transform.position - peer.transform.position);
                float sqr = away.sqrMagnitude;
                if (sqr < 0.0001f || sqr > separationRadius * separationRadius) continue;
                float d = Mathf.Sqrt(sqr);
                push += (away / d) * (1f - d / separationRadius);
            }
            cachedSeparation = Vector3.ClampMagnitude(push, 1f) * randomizedSeparationWeight;
        }
        return cachedSeparation;
    }

    protected bool TrackStuck(bool wantsToMove) // [R2]
    {
        if (!wantsToMove) { stuckSample = transform.position; stuckSampleTime = Time.time; return false; }
        if (Time.time - stuckSampleTime < 1.2f) return false;
        bool stuck = Flat(transform.position - stuckSample).sqrMagnitude < 0.09f;
        stuckSample = transform.position;
        stuckSampleTime = Time.time;
        return stuck;
    }

    // ------------------------------------------------------------------ Animation (single pipeline, state-change only)
    protected virtual void UpdateLocomotionAnimation() // [R2]
    {
        if (animLocked || animProfile == null || minionAnim == null) return;

        Vector3 hv = velocity; hv.y = 0f;
        float speed = hv.magnitude;

        bool restingLike = currentAnim == AnimKey.Idle || currentAnim == AnimKey.None || currentAnim == AnimKey.Attack || currentAnim == AnimKey.Hit;
        bool moving = restingLike ? speed > idleExitSpeed : speed > idleEnterSpeed;

        AnimKey want = AnimKey.Idle;
        if (moving)
        {
            Vector3 local = transform.InverseTransformDirection(hv);
            float ax = Mathf.Abs(local.x), az = Mathf.Abs(local.z);
            bool alreadyStrafing = currentAnim == AnimKey.StrafeLeft || currentAnim == AnimKey.StrafeRight;
            bool strafing = alreadyStrafing ? ax > az * 0.6f : ax > az * 1.2f;
            if (strafing) want = local.x > 0f ? AnimKey.StrafeRight : AnimKey.StrafeLeft;
            else want = local.z >= 0f ? AnimKey.Walk : AnimKey.Backpedal;
        }

        if (want == currentAnim) return;

        bool locomotionNow = currentAnim == AnimKey.Idle || currentAnim == AnimKey.Walk || currentAnim == AnimKey.StrafeLeft ||
                             currentAnim == AnimKey.StrafeRight || currentAnim == AnimKey.Backpedal;
        if (locomotionNow && Time.time - animChangeTime < minLocomotionDwell) return;

        AnimationClip clip;
        float fade = animProfile.walkTransitionDuration;
        switch (want)
        {
            case AnimKey.Idle:
                clip = animProfile.idleClip;
                fade = animProfile.idleTransitionDuration;
                break;
            case AnimKey.StrafeLeft:
                clip = animProfile.strafeLeftClip;
                if (clip == null) clip = animProfile.walkClip;
                break;
            case AnimKey.StrafeRight:
                clip = animProfile.strafeRightClip;
                if (clip == null) clip = animProfile.walkClip;
                break;
            default:
                clip = animProfile.walkClip;
                break;
        }
        if (clip == null) return;

        currentAnim = want;
        animChangeTime = Time.time;
        if (clip == lastLocoClip) return;
        lastLocoClip = clip;
        minionAnim.PlayAnimation(clip, fade, 1f);
    }

    // ------------------------------------------------------------------ Chase
    protected virtual void HandleChase(float dt) // [R2]
    {
        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;
        TryAcquireTokens();

        if (holdsEngageToken)
        {
            if (Time.time - engageApproachStart > engageApproachTimeout) { GiveUpEngage(); return; }
            if (dist <= StopDist) { TransitionToState(MinionState.Engage); return; }
            SteerTo(ChaseGoal, MoveSpd, false);
        }
        else
        {
            float ring = outerRingDistance + ringOffset + (holdsOuterRingToken ? 0f : noRingTokenExtraDistance);
            if (dist <= ring + ringBand) { TransitionToState(MinionState.Orbit); return; }
            SteerTo(ChaseGoal, MoveSpd, false);
        }

        if (TrackStuck(desiredVelocity.sqrMagnitude > 0.6f)) avoidSide = -avoidSide;
    }

    // ------------------------------------------------------------------ Orbit (strafing circle + breathers)
    protected virtual void HandleOrbit(float dt) // [R2]
    {
        TryAcquireTokens();
        if (holdsEngageToken) { TransitionToState(MinionState.Chase); return; }

        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;
        if (dist < 0.01f) return;
        Vector3 dirTo = toTarget / dist;
        Vector3 ringTangent = Vector3.Cross(Vector3.up, -dirTo);

        float ring = outerRingDistance + ringOffset + (holdsOuterRingToken ? 0f : noRingTokenExtraDistance);
        if (dist > ring + ringBand + 3f) { TransitionToState(MinionState.Chase); return; }

        float err = dist - ring;
        float radial = 0f;
        if (Mathf.Abs(err) > ringBand)
            radial = Mathf.Clamp((err - Mathf.Sign(err) * ringBand) * 1.5f, -MoveSpd * 0.8f, MoveSpd * 0.8f);
        if (dist < StopDist + 1f) radial = -MoveSpd * 0.6f;

        if (orbitPauseTimer > 0f) orbitPauseTimer -= dt;
        orbitSegmentTimer -= dt;
        if (orbitPauseTimer <= 0f && orbitSegmentTimer <= 0f)
        {
            if (Random.value < orbitPauseChance)
            {
                orbitPauseTimer = Random.Range(orbitPauseDuration.x, orbitPauseDuration.y);
                orbitSegmentTimer = orbitPauseTimer + Random.Range(orbitSegmentTime.x, orbitSegmentTime.y);
            }
            else
            {
                orbitSegmentTimer = Random.Range(orbitSegmentTime.x, orbitSegmentTime.y);
                orbitSign = Random.value > 0.5f ? 1 : -1;
                orbitSpeed = Random.Range(orbitSpeedRange.x, orbitSpeedRange.y);
            }
        }
        bool paused = orbitPauseTimer > 0f;

        Vector3 vel = dirTo * radial;
        if (!paused) vel += ringTangent * (orbitSign * orbitSpeed);
        vel += AngularSpacingPush(ringTangent);

        float mag = vel.magnitude;
        if (mag > 0.05f) vel = AvoidObstacles(vel / mag) * mag;

        desiredVelocity = Vector3.ClampMagnitude(vel + GetSeparation(), MoveSpd * 0.9f);
        desiredFacing = Flat(PerceivedTargetPosition - transform.position);
    }

    protected Vector3 AngularSpacingPush(Vector3 ringTangent) // [R2]
    {
        Vector3 myOff = Flat(transform.position - target.position);
        if (myOff.sqrMagnitude < 0.01f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < registry.Count; i++)
        {
            MinionEnemyBrain o = registry[i];
            if (o == null || o == this) continue;
            if (o.CurrentState != MinionState.Orbit && o.CurrentState != MinionState.Recover) continue;

            Vector3 oOff = Flat(o.transform.position - target.position);
            if (oOff.sqrMagnitude < 0.01f) continue;

            float delta = Vector3.SignedAngle(myOff, oOff, Vector3.up);
            float a = Mathf.Abs(delta);
            if (a < angularSpacing)
                push -= ringTangent * (Mathf.Sign(delta) * (1f - a / angularSpacing) * orbitSpeedRange.y);
        }
        return push;
    }

    // ------------------------------------------------------------------ Recover (back off -> idle breath -> orbit)
    protected virtual void HandleRecover(float dt) // [R2]
    {
        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;
        Vector3 dirTo = dist > 0.01f ? toTarget / dist : transform.forward;
        desiredFacing = Flat(PerceivedTargetPosition - transform.position);

        if (!recoverBreathing)
        {
            recoverTimer += dt;
            Vector3 back = AvoidObstacles(-dirTo);
            desiredVelocity = Vector3.ClampMagnitude(back * MoveSpd * 0.7f + GetSeparation(), MoveSpd);
            if (dist >= StopDist + recoverBackoffDistance || recoverTimer > 1.3f)
            {
                recoverBreathing = true;
                breathTimer = Random.Range(recoverBreathDuration.x, recoverBreathDuration.y);
            }
        }
        else
        {
            breathTimer -= dt;
            if (breathTimer <= 0f) TransitionToState(MinionState.Orbit);
        }
    }

    // ------------------------------------------------------------------ Engage (committed attack)
    protected void HoldAndFace() // [R2]
    {
        desiredVelocity = Vector3.zero;
        desiredFacing = Flat(PerceivedTargetPosition - transform.position);
        facingTurnSpeed = attackTurnSpeed;
    }

    protected virtual IEnumerator EngageRoutine() // [R2]
    {
        if (MinionData == null || MinionData.AvailableAttacks == null || MinionData.AvailableAttacks.Count == 0)
        {
            GiveUpEngage();
            yield break;
        }

        while (Time.time < globalLastAttackTime + MIN_ATTACK_STAGGER_DELAY)
        {
            if (target == null) { ReturnToPatrol(); yield break; }
            HoldAndFace();
            yield return null;
        }

        selectedAttack = MinionData.AvailableAttacks[Random.Range(0, MinionData.AvailableAttacks.Count)];

        if (GlobalTokenManager.Instance != null)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
            if (!hasToken) { GiveUpEngage(); yield break; }
        }

        globalLastAttackTime = Time.time;
        PlayAttackAnimation();

        float t = 0f;
        while (t < selectedAttack.StartupTime)
        {
            if (target == null) { ReturnToPatrol(); yield break; }
            t += Time.deltaTime;
            HoldAndFace();
            yield return null;
        }

        Vector3 lockedDir = Flat(transform.forward);
        lockedDir = lockedDir.sqrMagnitude > 0.0001f ? lockedDir.normalized : Vector3.forward;

        t = 0f;
        bool damageDealt = false;
        float range = selectedAttack.AttackRange;
        while (t < selectedAttack.ActiveTime)
        {
            if (target == null) { ReturnToPatrol(); yield break; }
            t += Time.deltaTime;
            desiredVelocity = lockedDir * (MoveSpd * 1.4f);
            desiredFacing = lockedDir;

            if (!damageDealt)
            {
                Vector3 toP = Flat(target.position - transform.position);
                float d = toP.magnitude;
                if (d <= range && (d < 0.3f || Vector3.Angle(lockedDir, toP) <= attackFacingArc * 0.5f))
                {
                    DealDamageToPlayer();
                    damageDealt = true;
                }
            }
            yield return null;
        }

        t = 0f;
        while (t < selectedAttack.RecoveryTime)
        {
            t += Time.deltaTime;
            desiredVelocity = Vector3.zero;
            yield return null;
        }

        CleanupTokens();
        engageReadyTime = Time.time + Random.Range(reEngageCooldown.x, reEngageCooldown.y);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Recover);
    }

    protected virtual void PlayAttackAnimation() // [R2]
    {
        AnimationClip clip = null;
        if (animProfile != null && selectedAttack != null && !string.IsNullOrEmpty(selectedAttack.AnimationClipName))
            clip = animProfile.GetAnimationClip(selectedAttack.AnimationClipName);

        animLocked = true;
        currentAnim = AnimKey.Attack;
        lastLocoClip = null;

        if (clip != null && minionAnim != null) minionAnim.PlayAnimation(clip, 0.05f, 1f);
        else if (!warnedMissingAttackClip)
        {
            warnedMissingAttackClip = true;
            Debug.LogWarning("[MinionEnemyBrain] Attack has no resolvable clip.", this);
        }
    }

    protected virtual void DealDamageToPlayer() // [R2]
    {
        if (target != null && target.TryGetComponent<IDamageable>(out var playerDamageable))
        {
            Vector3 hitDir = Flat(target.position - transform.position).normalized;
            float dmgAmount = selectedAttack != null ? selectedAttack.DamageAmount : 15f;
            float knockback = selectedAttack != null ? selectedAttack.KnockbackForce : 2f;
            AudioClip sound = selectedAttack != null ? selectedAttack.HitSound : null;

            playerDamageable.TakeDamage(dmgAmount, target.position, hitDir, knockback, sound, -1, false);
        }
    }

    // ------------------------------------------------------------------ Damage / hit reaction / death
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (CurrentState == MinionState.Dead) return;

        currentHealth -= damage;
        CleanupTokens();
        NotifyDamaged();

        if (currentHealth <= 0f)
        {
            TransitionToState(MinionState.Dead);
            return;
        }

        Vector3 push = Flat(hitDirection);
        if (push.sqrMagnitude < 0.0001f) push = -transform.forward;
        push.Normalize();
        const float knockDuration = 0.18f;
        knockVelocity = push * (2f * Mathf.Max(0f, force) / knockDuration);
        knockDecel = knockVelocity.magnitude / knockDuration;
        velocity = Vector3.zero;

        pendingHitDirection = hitDirection;
        pendingAttackId = attackID;
        TransitionToState(MinionState.Stun);
    }

    protected virtual IEnumerator HitReactionRoutine() // [R2]
    {
        animLocked = true;
        float duration = PlayHitReaction();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            yield return null;
        }

        engageReadyTime = Mathf.Min(engageReadyTime, Time.time + hitReengageDelay);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Chase);
    }

    protected float PlayHitReaction() // [R2]
    {
        if (minionAnim == null) return 0.35f;

        AnimationClip clip = null;
        float fade = 0.05f, speed = 1f;

        if (animProfile != null)
        {
            Vector3 dir = Flat(pendingHitDirection);
            if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward;
            Vector3 local = transform.InverseTransformDirection(dir.normalized);
            bool zAxis = Mathf.Abs(local.z) > Mathf.Abs(local.x);

            AttackReactionData data = animProfile.GetReaction(pendingAttackId);
            HitAnimationData hit = null;
            if (data != null)
                hit = zAxis ? (local.z > 0f ? data.reactionFront : data.reactionBack)
                            : (local.x > 0f ? data.reactionRight : data.reactionLeft);

            if (hit == null || hit.clip == null)
                hit = zAxis ? (local.z > 0f ? animProfile.defaultHitFront : animProfile.defaultHitBack)
                            : (local.x > 0f ? animProfile.defaultHitRight : animProfile.defaultHitLeft);

            if ((hit == null || hit.clip == null) && animProfile.attackReactions != null && animProfile.attackReactions.Count > 0)
                hit = animProfile.attackReactions[0].reactionFront;

            if (hit != null && hit.clip != null)
            {
                clip = hit.clip;
                fade = hit.transitionDuration;
                speed = hit.playbackSpeed > 0.01f ? hit.playbackSpeed : 1f;
            }
        }

        if (clip == null && directHitClip != null) clip = directHitClip;
        if (clip == null) return 0.3f;

        currentAnim = AnimKey.Hit;
        lastLocoClip = null;
        minionAnim.PlayAnimation(clip, fade, speed);
        return Mathf.Clamp(clip.length / Mathf.Max(0.01f, speed), 0.25f, 0.7f);
    }

    protected virtual void Die() // [R2]
    {
        CleanupTokens();
        if (GlobalTokenManager.Instance != null) GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);

        if (characterController != null) characterController.enabled = false;
        if (exclamationMarkVisual != null) exclamationMarkVisual.SetActive(false);

        if (TryGetComponent<EnemySoulDrop>(out var soulDrop))
        {
            soulDrop.TriggerSoulDrop();
        }
        animLocked = true;
        currentAnim = AnimKey.Death;
        lastLocoClip = null;

        AnimationClip deathClip = directDeathClip != null ? directDeathClip : (animProfile != null ? animProfile.deathClip : null);
        if (deathClip != null && minionAnim != null)
        {
            float fade = animProfile != null ? animProfile.deathTransitionDuration : 0.1f;
            float spd = animProfile != null ? animProfile.deathPlaybackSpeed : 1f;
            minionAnim.PlayAnimation(deathClip, fade, spd);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    protected IEnumerator DeathDisappearRoutine() // [R2]
    {
        yield return new WaitForSeconds(2.5f);
        if (EnemyObjectPool.Instance != null) EnemyObjectPool.Instance.ReturnToPool(gameObject);
        else gameObject.SetActive(false);
    }

    public bool NeedsHealing() => currentHealth < maxHealth && CurrentState != MinionState.Dead;
    public void ReceiveHeal(float amount) { if (CurrentState != MinionState.Dead) currentHealth = Mathf.Min(maxHealth, currentHealth + amount); }
    public Transform GetTransform() => transform;
}


// PART 2/3 - Perception, awareness meter, memory, alert, group alert, debug gizmos.
public partial class MinionEnemyBrain
{
    [Header("Perception - Vision (horizontal cone = Vision FOV Threshold above)")]
    public Transform eyeAnchor;
    [Range(10f, 180f)] public float verticalFOV = 80f;
    public float closeAwarenessRadius = 2.5f;
    public Vector2 reactionDelayRange = new Vector2(0.15f, 0.45f);
    public float perceptionRate = 14f;

    [Header("Perception - Hearing")]
    public float hearingRadius = 12f;
    public float footstepFullLoudnessSpeed = 6f;
    public float attackHearingRadius = 18f;
    [Range(0.1f, 1f)] public float wallHearingMultiplier = 0.5f;

    [Header("Awareness Meter")]
    public bool instantAlertOnSight = false;
    public float sightGainPerSecond = 1.2f;
    public float soundGainPerSecond = 1.0f;
    public float awarenessDecayPerSecond = 0.25f;
    [Range(0.1f, 0.9f)] public float suspiciousThreshold = 0.4f;

    [Header("Memory, Search & Group Alert")]
    public float loseSightDelay = 2.5f;
    public float searchDuration = 8f;
    public float searchRadius = 4f;
    public bool alertAlliesOnHit = true;
    public float alertRadius = 10f;

    // ------------------------------------------------------------------ Runtime
    protected const int HIST_LEN = 24; // [R2]
    protected readonly Vector3[] posHistory = new Vector3[HIST_LEN]; // [R2]
    protected int histCount, histHead; // [R2]

    protected float awareness, timeSinceSeen, reactionDelay, perceptionTimer, stimulusHoldUntil, investigateAt, heardAt; // [R2]
    protected bool seesTarget, hasMemory, prevValid, triedPlayerCtrl; // [R2]
    protected Vector3 lastKnownPos, lastKnownVel, prevTargetPos, lastTargetVelocity; // [R2]
    protected PlayerController playerCtrl; // [R2]

    protected Vector3 EyePosition => eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * 1.5f; // [R2]
    protected Vector3 ChaseGoal => (seesTarget || timeSinceSeen < 0.5f) ? target.position : lastKnownPos; // [R2]

    protected virtual void ResetPerception() // [R2]
    {
        awareness = 0f;
        seesTarget = false;
        hasMemory = false;
        prevValid = false;
        timeSinceSeen = 999f;
        stimulusHoldUntil = 0f;
        investigateAt = -1f;
        heardAt = -999f;
        histCount = 0;
        histHead = 0;
        lastKnownVel = Vector3.zero;
        lastTargetVelocity = Vector3.zero;
        reactionDelay = Random.Range(reactionDelayRange.x, reactionDelayRange.y);
        perceptionTimer = Random.Range(0f, 1f / Mathf.Max(1f, perceptionRate));
        playerCtrl = null;
        triedPlayerCtrl = false;
    }

    protected Vector3 PerceivedTargetPosition // [R2]
    {
        get
        {
            if (histCount == 0) return target != null ? target.position : transform.position;
            int back = Mathf.Min(histCount - 1, Mathf.RoundToInt(reactionDelay * perceptionRate));
            int idx = (histHead - 1 - back + HIST_LEN) % HIST_LEN;
            return posHistory[idx];
        }
    }

    protected virtual void TickPerception(float dt) // [R2]
    {
        if (CurrentState == MinionState.Unaware && awareness > 0f && Time.time > stimulusHoldUntil)
            awareness = Mathf.Max(0f, awareness - awarenessDecayPerSecond * dt);

        perceptionTimer -= dt;
        if (perceptionTimer > 0f) return;
        float tickDt = 1f / Mathf.Max(1f, perceptionRate);
        perceptionTimer = tickDt;

        Vector3 tp = target.position;
        Vector3 flatVel = prevValid ? Flat(tp - prevTargetPos) / tickDt : Vector3.zero;
        prevTargetPos = tp;
        prevValid = true;
        lastTargetVelocity = Vector3.Lerp(lastTargetVelocity, flatVel, 0.5f);

        posHistory[histHead] = tp;
        histHead = (histHead + 1) % HIST_LEN;
        if (histCount < HIST_LEN) histCount++;

        bool combat = CurrentState == MinionState.Chase || CurrentState == MinionState.Orbit ||
                      CurrentState == MinionState.Recover || CurrentState == MinionState.Engage || CurrentState == MinionState.Stun;

        seesTarget = EvaluateSight(combat, out float sightQuality);
        bool heard = EvaluateHearing(flatVel.magnitude, out float hearQuality);

        if (seesTarget)
        {
            timeSinceSeen = 0f;
            lastKnownPos = tp;
            lastKnownVel = lastTargetVelocity;
            hasMemory = true;
        }
        else timeSinceSeen += tickDt;

        if (heard)
        {
            heardAt = Time.time;
            if (!seesTarget)
            {
                Vector2 err = Random.insideUnitCircle * 1.5f;
                lastKnownPos = tp + new Vector3(err.x, 0f, err.y);
                hasMemory = true;
            }
        }

        switch (CurrentState)
        {
            case MinionState.Unaware:
                if (seesTarget)
                {
                    stimulusHoldUntil = Time.time + 0.4f;
                    awareness = instantAlertOnSight ? 1f : Mathf.Min(1f, awareness + sightGainPerSecond * sightQuality * tickDt);
                }
                if (heard)
                {
                    stimulusHoldUntil = Time.time + 0.4f;
                    awareness = Mathf.Min(1f, awareness + soundGainPerSecond * hearQuality * tickDt);
                    if (investigateAt < 0f) investigateAt = Time.time + reactionDelay;
                }
                if (awareness >= 1f) BeginAlert();
                else if (investigateAt >= 0f && Time.time >= investigateAt)
                {
                    investigateAt = -1f;
                    if (awareness >= suspiciousThreshold && hasMemory) TransitionToState(MinionState.Search);
                }
                break;

            case MinionState.Chase:
            case MinionState.Orbit:
            case MinionState.Recover:
                if (seesTarget || heard) awareness = 1f;
                else if (timeSinceSeen > loseSightDelay && Time.time - heardAt > loseSightDelay)
                    TransitionToState(MinionState.Search);
                break;

            case MinionState.Search:
                if (seesTarget)
                {
                    awareness = 1f;
                    TransitionToState(MinionState.Chase);
                }
                break;
        }
    }

    protected bool EvaluateSight(bool combat, out float quality) // [R2]
    {
        quality = 0f;
        Vector3 eye = EyePosition;
        Vector3 tp = target.position;
        Vector3 flatTo = Flat(tp - transform.position);
        float dist = flatTo.magnitude;

        if (combat)
        {
            if (dist > visionRange * 1.5f) return false;
            quality = 1f;
        }
        else if (dist <= closeAwarenessRadius)
        {
            quality = 1.5f;
        }
        else
        {
            if (dist > visionRange) return false;
            float dot = Vector3.Dot(Flat(transform.forward).normalized, flatTo / Mathf.Max(dist, 0.001f));
            if (dot < visionFOVThreshold) return false;

            Vector3 toHead = (tp + Vector3.up * 1.6f) - eye;
            float elevation = Mathf.Asin(Mathf.Clamp(toHead.y / Mathf.Max(toHead.magnitude, 0.001f), -1f, 1f)) * Mathf.Rad2Deg;
            if (Mathf.Abs(elevation) > verticalFOV * 0.5f) return false;

            quality = (0.4f + 0.6f * (1f - dist / visionRange)) * (0.5f + 0.5f * Mathf.InverseLerp(visionFOVThreshold, 1f, dot));
        }

        if (!Physics.Linecast(eye, tp + Vector3.up * 1.6f, obstacleMask, QueryTriggerInteraction.Ignore)) return true;
        if (!Physics.Linecast(eye, tp + Vector3.up * 1.0f, obstacleMask, QueryTriggerInteraction.Ignore)) return true;
        return false;
    }

    protected bool EvaluateHearing(float targetSpeed, out float quality) // [R2]
    {
        quality = 0f;
        float radius = hearingRadius * Mathf.Clamp01(targetSpeed / Mathf.Max(0.1f, footstepFullLoudnessSpeed));
        if (IsPlayerAttacking()) radius = Mathf.Max(radius, attackHearingRadius);
        if (radius < 0.5f) return false;

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > radius) return false;

        if (Physics.Linecast(EyePosition, target.position + Vector3.up, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            radius *= wallHearingMultiplier;
            if (dist > radius) return false;
        }

        quality = 0.3f + 0.7f * (1f - dist / radius);
        return true;
    }

    protected bool IsPlayerAttacking() // [R2]
    {
        if (playerCtrl == null)
        {
            if (triedPlayerCtrl) return false;
            triedPlayerCtrl = true;
            playerCtrl = target.GetComponentInParent<PlayerController>();
            if (playerCtrl == null) return false;
        }
        // INTEGRATED TASK A FIX: Includes heavy and AOE attacks
        return playerCtrl.CurrentState == playerCtrl.AttackState ||
               playerCtrl.CurrentState == playerCtrl.PowerPunchState ||
               playerCtrl.CurrentState == playerCtrl.AOEAttackState; 
    }

    // ------------------------------------------------------------------ Alert / group alert
    protected virtual void BeginAlert() // [R2]
    {
        awareness = 1f;
        if (!hasMemory && target != null) { lastKnownPos = target.position; hasMemory = true; }
        AlertAllies(lastKnownPos);
        TransitionToState(MinionState.Suspicious);
    }

    protected virtual void NotifyDamaged() // [R2]
    {
        awareness = 1f;
        timeSinceSeen = 0f;
        if (target != null) { lastKnownPos = target.position; hasMemory = true; }
        AlertAllies(lastKnownPos);
    }

    protected void AlertAllies(Vector3 pos) // [R2]
    {
        if (!alertAlliesOnHit) return;
        float r2 = alertRadius * alertRadius;
        for (int i = 0; i < registry.Count; i++)
        {
            MinionEnemyBrain o = registry[i];
            if (o == null || o == this) continue;
            if ((o.transform.position - transform.position).sqrMagnitude > r2) continue;
            o.ReceiveAlert(pos);
        }
    }

    protected virtual void ReceiveAlert(Vector3 pos) // [R2]
    {
        if (CurrentState != MinionState.Unaware) return;
        awareness = Mathf.Max(awareness, 0.6f);
        stimulusHoldUntil = Time.time + 1f;
        lastKnownPos = pos;
        hasMemory = true;
        if (investigateAt < 0f) investigateAt = Time.time + reactionDelay;
    }

    protected void AlertHold() // [R2]
    {
        desiredVelocity = Vector3.zero;
        desiredFacing = Flat((hasMemory ? lastKnownPos : target.position) - transform.position);
        facingTurnSpeed = turnSpeed * 0.8f;
    }

    protected void SetMark(float s) // [R2]
    {
        exclamationMarkVisual.transform.localScale = new Vector3(s, s, s);
    }

    protected IEnumerator AlertSequenceRoutine() // [R2]
    {
        if (exclamationMarkVisual != null)
        {
            exclamationMarkVisual.SetActive(true);

            float e = 0f;
            while (e < 0.15f)
            {
                e += Time.deltaTime;
                SetMark(Mathf.LerpUnclamped(0f, 1.2f, e / 0.15f));
                AlertHold();
                yield return null;
            }

            e = 0f;
            while (e < 0.1f)
            {
                e += Time.deltaTime;
                SetMark(Mathf.LerpUnclamped(1.2f, 1f, e / 0.1f));
                AlertHold();
                yield return null;
            }
        }

        float f = 0f;
        while (f < alertFreezeDuration)
        {
            f += Time.deltaTime;
            AlertHold();
            yield return null;
        }

        engageReadyTime = Time.time + Random.Range(0.2f, 1.0f);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Chase);
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------ Debug overlay
    protected virtual void OnDrawGizmosSelected() // [R2]
    {
        Vector3 p = transform.position + Vector3.up * 0.1f;
        float half = Mathf.Acos(Mathf.Clamp(visionFOVThreshold, -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 f = transform.forward;

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Vector3 prev = p + Quaternion.Euler(0f, -half, 0f) * f * visionRange;
        Gizmos.DrawLine(p, prev);
        for (int i = 1; i <= 16; i++)
        {
            float a = Mathf.Lerp(-half, half, i / 16f);
            Vector3 n = p + Quaternion.Euler(0f, a, 0f) * f * visionRange;
            Gizmos.DrawLine(prev, n);
            prev = n;
        }
        Gizmos.DrawLine(p, prev);

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(p, closeAwarenessRadius);
        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.6f);
        Gizmos.DrawWireSphere(p, hearingRadius);

        if (!Application.isPlaying) return;

        if (hasMemory)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(p, lastKnownPos + Vector3.up * 0.1f);
            Gizmos.DrawWireSphere(lastKnownPos + Vector3.up * 0.1f, 0.3f);
        }

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 2.6f,
            CurrentState + "  aw " + awareness.ToString("0.00") +
            (holdsEngageToken ? "  [ENGAGE]" : "") + (holdsOuterRingToken ? "  [RING]" : ""));
    }
#endif
}


// PART 3/3 - Patrol (stealth phase), peer avoidance, social pause, Search and return to patrol.
public partial class MinionEnemyBrain
{
    [Header("Patrol Behaviour")]
    [Range(0f, 1f)] public float socialPauseChance = 0.35f;
    public Vector2 socialPauseDuration = new Vector2(2f, 5f);
    public float lookAroundAngle = 55f;

    protected Vector3 currentPatrolTarget; // [R2]
    protected bool patrolWaiting; // [R2]
    protected float patrolWaitTimer, lookBaseYaw, lookPhase, patrolSpeedFactor; // [R2]
    protected float socialPauseUntil, nextSocialTime, peerYieldUntil; // [R2]
    protected Transform socialPartner; // [R2]

    // Search state
    protected int searchPhase; // [R2]
    protected Vector3 searchPoint; // [R2]
    protected float searchEndTime, scanTimer, scanBaseYaw, scanT; // [R2]
    protected int searchPointsVisited; // [R2]

    protected virtual void ResetPatrol() // [R2]
    {
        patrolWaiting = false;
        patrolSpeedFactor = Random.Range(0.85f, 1.15f);
        socialPauseUntil = 0f;
        socialPartner = null;
        nextSocialTime = Time.time + Random.Range(4f, 12f);
        peerYieldUntil = 0f;
        PickPatrolPoint();
    }

    protected void PickPatrolPoint() // [R2]
    {
        if (assignedZone != null)
            currentPatrolTarget = assignedZone.GetValidPatrolPoint(gameObject.GetInstanceID(), transform.position);
        else
            currentPatrolTarget = transform.position;
    }

    // ------------------------------------------------------------------ Patrol
    protected virtual void HandleUnaware(float dt) // [R2]
    {
        if (assignedZone == null) return;

        if (Time.time < socialPauseUntil)
        {
            if (socialPartner != null)
            {
                desiredFacing = Flat(socialPartner.position - transform.position);
                facingTurnSpeed = turnSpeed * 0.4f;
            }
            return;
        }
        if (socialPartner != null)
        {
            socialPartner = null;
            patrolWaiting = false;
            PickPatrolPoint();
        }

        if (awareness >= suspiciousThreshold && hasMemory)
        {
            desiredFacing = Flat(lastKnownPos - transform.position);
            facingTurnSpeed = turnSpeed * 0.6f;
            return;
        }

        if (patrolWaiting)
        {
            lookPhase += dt * 0.8f;
            desiredFacing = Quaternion.Euler(0f, lookBaseYaw + Mathf.Sin(lookPhase) * lookAroundAngle, 0f) * Vector3.forward;
            facingTurnSpeed = turnSpeed * 0.3f;

            patrolWaitTimer -= dt;
            if (patrolWaitTimer <= 0f)
            {
                patrolWaiting = false;
                PickPatrolPoint();
            }
            return;
        }

        TrySocialPause();

        Vector3 to = Flat(currentPatrolTarget - transform.position);
        float dist = to.magnitude;
        if (dist < 0.6f)
        {
            patrolWaiting = true;
            patrolWaitTimer = waypointWaitTime * Random.Range(0.7f, 1.4f);
            lookBaseYaw = transform.eulerAngles.y;
            lookPhase = 0f;
            return;
        }

        float speed = patrolSpeed * patrolSpeedFactor * Mathf.Clamp01(dist / 2f + 0.3f);
        float angle = Vector3.Angle(transform.forward, to);
        speed *= Mathf.Clamp01(Mathf.InverseLerp(120f, 35f, angle));
        if (Time.time < peerYieldUntil) speed = 0f;

        Vector3 dir = AvoidObstacles(to / dist);
        dir = PatrolPeerSteer(dir);

        desiredVelocity = Vector3.ClampMagnitude(dir * speed + GetSeparation() * 0.5f, patrolSpeed * 1.2f);
        desiredFacing = velocity.sqrMagnitude > 0.25f ? Vector3.zero : to;

        if (TrackStuck(speed > 0.3f)) PickPatrolPoint();
    }

    protected Vector3 PatrolPeerSteer(Vector3 dir) // [R2]
    {
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        Vector3 shift = Vector3.zero;
        int myId = GetInstanceID();

        for (int i = 0; i < registry.Count; i++)
        {
            MinionEnemyBrain o = registry[i];
            if (o == null || o == this || o.CurrentState != MinionState.Unaware) continue;

            Vector3 off = Flat(o.transform.position - transform.position);
            float d = off.magnitude;
            if (d < 0.01f || d > bumpTurnDistance) continue;
            if (Vector3.Dot(dir, off / d) < 0.3f) continue;

            shift += right * (1f - d / bumpTurnDistance);
            if (d < bumpTurnRadius * 2f && myId < o.GetInstanceID() && Time.time >= peerYieldUntil)
                peerYieldUntil = Time.time + Random.Range(0.4f, 1.0f);
        }

        Vector3 steered = dir + shift * 1.2f;
        return steered.sqrMagnitude > 0.0001f ? steered.normalized : dir;
    }

    protected void TrySocialPause() // [R2]
    {
        if (Time.time < nextSocialTime) return;
        nextSocialTime = Time.time + 1f;

        for (int i = 0; i < registry.Count; i++)
        {
            MinionEnemyBrain o = registry[i];
            if (o == null || o == this || o.CurrentState != MinionState.Unaware || Time.time < o.socialPauseUntil) continue;

            Vector3 off = Flat(o.transform.position - transform.position);
            float d = off.magnitude;
            if (d < 0.01f || d > 2.8f) continue;

            Vector3 dirToO = off / d;
            if (Vector3.Dot(transform.forward, dirToO) < 0.5f || Vector3.Dot(o.transform.forward, -dirToO) < 0.5f) continue;
            if (Random.value > socialPauseChance) continue;

            float dur = Random.Range(socialPauseDuration.x, socialPauseDuration.y);
            BeginSocialPause(o.transform, dur);
            o.BeginSocialPause(transform, dur);
            return;
        }
    }

    protected void BeginSocialPause(Transform partner, float duration) // [R2]
    {
        socialPartner = partner;
        socialPauseUntil = Time.time + duration;
        nextSocialTime = Time.time + 25f;
        patrolWaiting = false;
    }

    // ------------------------------------------------------------------ Search
    protected virtual void EnterSearch() // [R2]
    {
        searchEndTime = Time.time + searchDuration;
        searchPointsVisited = 0;
        searchPhase = 0;
        searchPoint = hasMemory ? lastKnownPos : (target != null ? target.position : transform.position);
    }

    protected virtual void HandleSearch(float dt) // [R2]
    {
        if (Time.time >= searchEndTime) { ReturnToPatrol(); return; }

        if (searchPhase == 0)
        {
            if (Flat(searchPoint - transform.position).magnitude < 1f)
            {
                searchPhase = 1;
                scanTimer = Random.Range(1.4f, 2.4f);
                scanBaseYaw = transform.eulerAngles.y;
                scanT = 0f;
                return;
            }

            SteerTo(searchPoint, patrolSpeed * 1.5f, false);
            if (TrackStuck(desiredVelocity.sqrMagnitude > 0.3f)) { avoidSide = -avoidSide; NextSearchPoint(); }
        }
        else
        {
            scanT += dt;
            desiredFacing = Quaternion.Euler(0f, scanBaseYaw + Mathf.Sin(scanT * 2.2f) * 75f, 0f) * Vector3.forward;
            facingTurnSpeed = turnSpeed * 0.5f;

            scanTimer -= dt;
            if (scanTimer <= 0f) NextSearchPoint();
        }
    }

    protected void NextSearchPoint() // [R2]
    {
        searchPointsVisited++;
        Vector3 bias = Flat(lastKnownVel);
        bias = bias.sqrMagnitude > 0.25f ? bias.normalized : Flat(transform.forward);

        float spread = searchRadius * (1f + searchPointsVisited * 0.35f);
        Vector2 r = Random.insideUnitCircle * spread;
        searchPoint = lastKnownPos + bias * (searchPointsVisited * 1.5f) + new Vector3(r.x, 0f, r.y);
        searchPhase = 0;
    }

    protected virtual void ReturnToPatrol() // [R2]
    {
        CleanupTokens();
        awareness = 0f;
        hasMemory = false;
        investigateAt = -1f;
        TransitionToState(MinionState.Unaware);
        PickPatrolPoint();
    }
}