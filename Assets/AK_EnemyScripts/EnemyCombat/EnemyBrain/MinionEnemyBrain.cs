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
    // Existing values keep their order; new ones are appended.
    public enum MinionState { Unaware, Suspicious, Chase, Orbit, Engage, Stun, Dead, Recover, Search }
    public MinionState CurrentState { get; private set; }

    private enum AnimKey { None, Idle, Walk, StrafeLeft, StrafeRight, Backpedal, Attack, Hit, Death }

    private MinionEnemyDataSO MinionData => enemyData as MinionEnemyDataSO;
    private EnemyAnimationEngine minionAnim;
    private float MoveSpd => MinionData != null ? MinionData.MoveSpeed : 3.5f;
    private float StopDist => MinionData != null ? MinionData.AttackStopDistance : 2f;

    // ------------------------------------------------------------------ Inspector (existing names kept)
    [Header("Animation Profiles & Direct Inspector Clips")]
    public EnemyAnimProfile animProfile;
    [Tooltip("Assign your Death animation clip directly here for a guaranteed zero-fail trigger.")]
    public AnimationClip directDeathClip;
    [Tooltip("Assign your default Hit/Flinch animation clip directly here.")]
    public AnimationClip directHitClip;

    [Header("Health & Combat Settings")]
    public float maxHealth = 60f;
    private float currentHealth;

    [Header("Dual-Ring Swarm Manager")]
    public float outerRingDistance = 8.0f;

    [Header("Patrol Area (Stealth Phase)")]
    public PatrolZone assignedZone;
    public float patrolSpeed = 2.0f;
    public float waypointWaitTime = 2.0f;

    [Header("Patrol Peer Avoidance (was Bump Mechanics)")]
    [Tooltip("Patrollers closer than 2x this yield by priority.")]
    public float bumpTurnRadius = 1.0f;
    [Tooltip("Look-ahead distance for keep-right peer avoidance.")]
    public float bumpTurnDistance = 4.0f;

    [Header("Obstacle Avoidance (sphere cast)")]
    public float obstacleAvoidanceDistance = 3.0f;
    public float avoidanceWeight = 5.0f;
    [Tooltip("Walls/props only. Must NOT contain the Player or Enemy layers.")]
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
    [Tooltip("Max turn rate in degrees/second. Replaces the old Slerp factor (the jitter source).")]
    public float turnSpeed = 300f;
    public float acceleration = 12f;
    public float deceleration = 16f;
    [Tooltip("Hysteresis: start moving above Exit, return to idle below Enter (m/s).")]
    public float idleEnterSpeed = 0.15f;
    public float idleExitSpeed = 0.4f;
    [Tooltip("Min seconds between locomotion clip switches (stops Idle/Walk flicker).")]
    public float minLocomotionDwell = 0.25f;

    [Header("Orbit Ring")]
    [Tooltip("No radial correction inside +/- this band around the ring distance.")]
    public float ringBand = 1.0f;
    public float noRingTokenExtraDistance = 2.0f;
    public Vector2 orbitSpeedRange = new Vector2(1.2f, 2.2f);
    [Tooltip("Seconds of circling before the next pause or direction flip.")]
    public Vector2 orbitSegmentTime = new Vector2(3f, 6f);
    [Tooltip("Minimum angle (deg) between orbiting minions around the player.")]
    public float angularSpacing = 35f;

    [Header("Breathers (Idle Pauses)")]
    [Range(0f, 1f)] public float orbitPauseChance = 0.5f;
    public Vector2 orbitPauseDuration = new Vector2(0.8f, 1.6f);
    [Tooltip("After an attack: back off this far, then idle for the breath time.")]
    public float recoverBackoffDistance = 2.0f;
    public Vector2 recoverBreathDuration = new Vector2(1.2f, 2.4f);

    [Header("Attack Rotation & Commitment")]
    public Vector2 reEngageCooldown = new Vector2(2.5f, 5f);
    public float engageApproachTimeout = 6f;
    [Tooltip("Total arc (deg) in front of the LOCKED attack direction that can be hit. Sidestep = dodge.")]
    public float attackFacingArc = 70f;
    public float attackTurnSpeed = 220f;
    [Tooltip("After a flinch, the Engage cooldown is capped to this.")]
    public float hitReengageDelay = 0.5f;

    // ------------------------------------------------------------------ Runtime
    private float randomizedSeparationWeight;
    private bool holdsEngageToken, holdsOuterRingToken;
    private float nextTokenRequestTime, engageReadyTime, waitingSince, engageApproachStart, stateEnterTime;
    private float ringOffset, orbitSpeed, orbitSegmentTimer, orbitPauseTimer;
    private int orbitSign = 1;
    private float recoverTimer, breathTimer;
    private bool recoverBreathing;

    private Vector3 velocity, desiredVelocity, desiredFacing, knockVelocity, cachedSeparation;
    private float facingTurnSpeed, knockDecel, nextSeparationTime;
    private int avoidSide = 1;

    private AnimKey currentAnim = AnimKey.None;
    private AnimationClip lastLocoClip;
    private float animChangeTime;
    private bool animLocked;
    private bool warnedMissingAttackClip;

    private Coroutine activeRoutine;
    private Vector3 pendingHitDirection;
    private int pendingAttackId;

    private Vector3 stuckSample;
    private float stuckSampleTime;

    private static readonly List<MinionEnemyBrain> registry = new List<MinionEnemyBrain>(32);
    private static readonly Collider[] separationBuffer = new Collider[16];
    private static float globalLastAttackTime = 0f;
    private const float MIN_ATTACK_STAGGER_DELAY = 0.6f;
    private const float GROUND_STICK = -4f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        globalLastAttackTime = 0f;
        registry.Clear();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    private float GravityY => gravity != 0f ? -Mathf.Abs(gravity) : Physics.gravity.y;

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
    private void ReleaseAttackToken()
    {
        if (hasToken && selectedAttack != null && GlobalTokenManager.Instance != null)
            GlobalTokenManager.Instance.ReleaseToken(transform, selectedAttack.RequiredTokenType);
        hasToken = false;
    }

    private void CleanupTokens()
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

    private bool WantsEngage =>
        isActiveAndEnabled && target != null && !holdsEngageToken &&
        (CurrentState == MinionState.Orbit || CurrentState == MinionState.Chase) &&
        Time.time >= engageReadyTime;

    // Longest-waiting eligible minion goes first (fairness solved in the brain, token manager untouched).
    private bool IsNextInLine()
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

    private void TryAcquireTokens()
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

    private void GiveUpEngage()
    {
        CleanupTokens();
        engageReadyTime = Time.time + Random.Range(1f, 2f);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Orbit);
    }

    // ------------------------------------------------------------------ State machine
    private void TransitionToState(MinionState newState)
    {
        if (CurrentState == MinionState.Dead) return;
        MinionState old = CurrentState;

        if (old == MinionState.Unaware && newState != MinionState.Unaware && assignedZone != null)
            assignedZone.ReleasePoint(gameObject.GetInstanceID());

        if (old == MinionState.Suspicious && exclamationMarkVisual != null)
            exclamationMarkVisual.SetActive(false);

        if (old == MinionState.Engage) ReleaseAttackToken(); // leak fix: leaving Engage for ANY reason frees the attack token

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
        if (dt <= 0f) return; // pause / timeScale 0

        ApplyMovement(dt);              // uses last frame's intent -> exactly ONE CharacterController.Move per frame
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
            // Suspicious / Engage / Stun are owned by their coroutines
        }
    }

    // ------------------------------------------------------------------ Movement core
    private void ApplyMovement(float dt)
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
            if (face.sqrMagnitude < 0.09f) return; // not moving: hold heading
        }
        float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg, facingTurnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void SteerTo(Vector3 goal, float speed, bool faceTarget)
    {
        Vector3 to = Flat(goal - transform.position);
        float dist = to.magnitude;
        if (dist < 0.05f) return;

        Vector3 dir = AvoidObstacles(to / dist);
        desiredVelocity = Vector3.ClampMagnitude(dir * speed + GetSeparation(), speed * 1.15f);
        if (faceTarget) desiredFacing = Flat(PerceivedTargetPosition - transform.position);
    }

    private Vector3 AvoidObstacles(Vector3 dir)
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

    private Vector3 GetSeparation()
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
            cachedSeparation = Vector3.ClampMagnitude(push, 1f) * randomizedSeparationWeight; // clamped -> no direction thrash
        }
        return cachedSeparation;
    }

    // One event per ~1.2 s if the agent wanted to move but did not.
    private bool TrackStuck(bool wantsToMove)
    {
        if (!wantsToMove) { stuckSample = transform.position; stuckSampleTime = Time.time; return false; }
        if (Time.time - stuckSampleTime < 1.2f) return false;
        bool stuck = Flat(transform.position - stuckSample).sqrMagnitude < 0.09f;
        stuckSample = transform.position;
        stuckSampleTime = Time.time;
        return stuck;
    }

    // ------------------------------------------------------------------ Animation (single pipeline, state-change only)
    private void UpdateLocomotionAnimation()
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
        if (clip == lastLocoClip) return; // same clip (e.g. Walk <-> Backpedal): never restart it
        lastLocoClip = clip;
        minionAnim.PlayAnimation(clip, fade, 1f);
    }

    // ------------------------------------------------------------------ Chase
    private void HandleChase(float dt)
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

        if (TrackStuck(desiredVelocity.sqrMagnitude > 0.6f)) avoidSide = -avoidSide; // sidestep the blocker
    }

    // ------------------------------------------------------------------ Orbit (strafing circle + breathers)
    private void HandleOrbit(float dt)
    {
        TryAcquireTokens();
        if (holdsEngageToken) { TransitionToState(MinionState.Chase); return; }

        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;
        if (dist < 0.01f) return;
        Vector3 dirTo = toTarget / dist;
        Vector3 ringTangent = Vector3.Cross(Vector3.up, -dirTo); // direction of increasing angle around the player

        float ring = outerRingDistance + ringOffset + (holdsOuterRingToken ? 0f : noRingTokenExtraDistance);
        if (dist > ring + ringBand + 3f) { TransitionToState(MinionState.Chase); return; } // player ran off: sprint back

        // radial: dead-band + gentle gain (no more 5 m/s backing off at 3 m)
        float err = dist - ring;
        float radial = 0f;
        if (Mathf.Abs(err) > ringBand)
            radial = Mathf.Clamp((err - Mathf.Sign(err) * ringBand) * 1.5f, -MoveSpd * 0.8f, MoveSpd * 0.8f);
        if (dist < StopDist + 1f) radial = -MoveSpd * 0.6f; // breathing space

        // segment timer: circle, then maybe stop and breathe (idle), then continue
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
        desiredFacing = Flat(PerceivedTargetPosition - transform.position); // face the player -> strafe clips match
    }

    private Vector3 AngularSpacingPush(Vector3 ringTangent)
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

            float delta = Vector3.SignedAngle(myOff, oOff, Vector3.up); // + = peer sits further along +ring angle
            float a = Mathf.Abs(delta);
            if (a < angularSpacing)
                push -= ringTangent * (Mathf.Sign(delta) * (1f - a / angularSpacing) * orbitSpeedRange.y);
        }
        return push;
    }

    // ------------------------------------------------------------------ Recover (back off -> idle breath -> orbit)
    private void HandleRecover(float dt)
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
            // the breather: stand still, the idle clip plays through the normal hysteresis
            breathTimer -= dt;
            if (breathTimer <= 0f) TransitionToState(MinionState.Orbit);
        }
    }

    // ------------------------------------------------------------------ Engage (committed attack)
    private void HoldAndFace()
    {
        desiredVelocity = Vector3.zero;
        desiredFacing = Flat(PerceivedTargetPosition - transform.position);
        facingTurnSpeed = attackTurnSpeed;
    }

    private IEnumerator EngageRoutine()
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

        // telegraph / windup: still tracking (turn-capped, delayed perception)
        float t = 0f;
        while (t < selectedAttack.StartupTime)
        {
            if (target == null) { ReturnToPatrol(); yield break; }
            t += Time.deltaTime;
            HoldAndFace();
            yield return null;
        }

        // direction is LOCKED here -> a sidestep dodges the lunge
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

    private void PlayAttackAnimation()
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
            Debug.LogWarning("[MinionEnemyBrain] Attack has no resolvable clip (AnimationClipName not found in EnemyAnimProfile).", this);
        }
    }

    private void DealDamageToPlayer()
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
        NotifyDamaged(); // instant alert + group alert (Perception part)

        if (currentHealth <= 0f)
        {
            TransitionToState(MinionState.Dead);
            return;
        }

        Vector3 push = Flat(hitDirection);
        if (push.sqrMagnitude < 0.0001f) push = -transform.forward;
        push.Normalize();
        const float knockDuration = 0.18f;
        knockVelocity = push * (2f * Mathf.Max(0f, force) / knockDuration); // linear decay -> total push distance == force
        knockDecel = knockVelocity.magnitude / knockDuration;
        velocity = Vector3.zero;

        pendingHitDirection = hitDirection;
        pendingAttackId = attackID;
        TransitionToState(MinionState.Stun); // re-entering Stun replays the reaction on every combo hit
    }

    private IEnumerator HitReactionRoutine()
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
        TransitionToState(MinionState.Chase); // flinch, then re-engage. Never flee.
    }

    // Same path the Capsule and Imp use (animationEngine.PlayAnimation + directional profile data).
    private float PlayHitReaction()
    {
        if (minionAnim == null) return 0.35f;

        AnimationClip clip = null;
        float fade = 0.05f, speed = 1f;

        if (directHitClip != null)
        {
            clip = directHitClip;
        }
        else if (animProfile != null)
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
                speed = hit.playbackSpeed;
            }
        }

        if (clip == null) return 0.3f;

        currentAnim = AnimKey.Hit;
        lastLocoClip = null;
        minionAnim.PlayAnimation(clip, fade, speed);
        return Mathf.Clamp(clip.length / Mathf.Max(0.01f, speed), 0.25f, 0.7f);
    }

    private void Die()
    {
        CleanupTokens();
        if (GlobalTokenManager.Instance != null) GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);

        if (characterController != null) characterController.enabled = false;
        if (exclamationMarkVisual != null) exclamationMarkVisual.SetActive(false);

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

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(2.5f);
        if (EnemyObjectPool.Instance != null) EnemyObjectPool.Instance.ReturnToPool(gameObject);
        else gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ IHealable
    public bool NeedsHealing() => currentHealth < maxHealth && CurrentState != MinionState.Dead;
    public void ReceiveHeal(float amount) { if (CurrentState != MinionState.Dead) currentHealth = Mathf.Min(maxHealth, currentHealth + amount); }
    public Transform GetTransform() => transform;
}


// PART 2/3 - Perception, awareness meter, memory, alert, group alert, debug gizmos.
public partial class MinionEnemyBrain
{
    [Header("Perception - Vision (horizontal cone = Vision FOV Threshold above)")]
    [Tooltip("Optional. Falls back to transform + 1.5 m.")]
    public Transform eyeAnchor;
    [Range(10f, 180f)] public float verticalFOV = 80f;
    [Tooltip("Inside this radius the minion notices you from any angle (still needs line of sight).")]
    public float closeAwarenessRadius = 2.5f;
    [Tooltip("Per-agent random delay before it reacts / tracks your real position.")]
    public Vector2 reactionDelayRange = new Vector2(0.15f, 0.45f);
    [Tooltip("Perception ticks per second (staggered per agent).")]
    public float perceptionRate = 14f;

    [Header("Perception - Hearing")]
    public float hearingRadius = 12f;
    [Tooltip("Player speed (m/s) that counts as full footstep loudness.")]
    public float footstepFullLoudnessSpeed = 6f;
    public float attackHearingRadius = 18f;
    [Range(0.1f, 1f)] public float wallHearingMultiplier = 0.5f;

    [Header("Awareness Meter")]
    [Tooltip("ON = old behaviour: seeing the player alerts instantly.")]
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
    private const int HIST_LEN = 24;
    private readonly Vector3[] posHistory = new Vector3[HIST_LEN];
    private int histCount, histHead;

    private float awareness, timeSinceSeen, reactionDelay, perceptionTimer, stimulusHoldUntil, investigateAt, heardAt;
    private bool seesTarget, hasMemory, prevValid, triedPlayerCtrl;
    private Vector3 lastKnownPos, lastKnownVel, prevTargetPos, lastTargetVelocity;
    private PlayerController playerCtrl;

    private Vector3 EyePosition => eyeAnchor != null ? eyeAnchor.position : transform.position + Vector3.up * 1.5f;
    private Vector3 ChaseGoal => (seesTarget || timeSinceSeen < 0.5f) ? target.position : lastKnownPos;

    private void ResetPerception()
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
        perceptionTimer = Random.Range(0f, 1f / Mathf.Max(1f, perceptionRate)); // stagger agents
        playerCtrl = null;
        triedPlayerCtrl = false;
    }

    // What the agent AIMS at: your position from reactionDelay seconds ago (no instant snap after a dodge).
    private Vector3 PerceivedTargetPosition
    {
        get
        {
            if (histCount == 0) return target != null ? target.position : transform.position;
            int back = Mathf.Min(histCount - 1, Mathf.RoundToInt(reactionDelay * perceptionRate));
            int idx = (histHead - 1 - back + HIST_LEN) % HIST_LEN;
            return posHistory[idx];
        }
    }

    private void TickPerception(float dt)
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
                Vector2 err = Random.insideUnitCircle * 1.5f; // sound gives an approximate position only
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

    // combat = alerted agents keep tracking you without the cone (still need line of sight).
    private bool EvaluateSight(bool combat, out float quality)
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

        // Multi-point LOS. Linecast ends AT the point, so a wall BEHIND the player never blocks sight.
        if (!Physics.Linecast(eye, tp + Vector3.up * 1.6f, obstacleMask, QueryTriggerInteraction.Ignore)) return true;
        if (!Physics.Linecast(eye, tp + Vector3.up * 1.0f, obstacleMask, QueryTriggerInteraction.Ignore)) return true;
        return false;
    }

    private bool EvaluateHearing(float targetSpeed, out float quality)
    {
        quality = 0f;
        float radius = hearingRadius * Mathf.Clamp01(targetSpeed / Mathf.Max(0.1f, footstepFullLoudnessSpeed));
        if (IsPlayerAttacking()) radius = Mathf.Max(radius, attackHearingRadius);
        if (radius < 0.5f) return false;

        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > radius) return false;

        if (Physics.Linecast(EyePosition, target.position + Vector3.up, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            radius *= wallHearingMultiplier; // walls attenuate
            if (dist > radius) return false;
        }

        quality = 0.3f + 0.7f * (1f - dist / radius);
        return true;
    }

    private bool IsPlayerAttacking()
    {
        if (playerCtrl == null)
        {
            if (triedPlayerCtrl) return false;
            triedPlayerCtrl = true;
            playerCtrl = target.GetComponentInParent<PlayerController>();
            if (playerCtrl == null) return false;
        }
        return playerCtrl.CurrentState == playerCtrl.AttackState;
    }

    // ------------------------------------------------------------------ Alert / group alert
    private void BeginAlert()
    {
        awareness = 1f;
        if (!hasMemory && target != null) { lastKnownPos = target.position; hasMemory = true; }
        AlertAllies(lastKnownPos);
        TransitionToState(MinionState.Suspicious);
    }

    private void NotifyDamaged()
    {
        awareness = 1f;
        timeSinceSeen = 0f;
        if (target != null) { lastKnownPos = target.position; hasMemory = true; }
        AlertAllies(lastKnownPos);
    }

    private void AlertAllies(Vector3 pos)
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

    private void ReceiveAlert(Vector3 pos)
    {
        if (CurrentState != MinionState.Unaware) return;
        awareness = Mathf.Max(awareness, 0.6f);
        stimulusHoldUntil = Time.time + 1f;
        lastKnownPos = pos;
        hasMemory = true;
        if (investigateAt < 0f) investigateAt = Time.time + reactionDelay;
    }

    private void AlertHold()
    {
        desiredVelocity = Vector3.zero;
        desiredFacing = Flat((hasMemory ? lastKnownPos : target.position) - transform.position);
        facingTurnSpeed = turnSpeed * 0.8f;
    }

    private void SetMark(float s)
    {
        exclamationMarkVisual.transform.localScale = new Vector3(s, s, s);
    }

    private IEnumerator AlertSequenceRoutine()
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

        engageReadyTime = Time.time + Random.Range(0.2f, 1.0f); // group alerts do not all swing at once
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Chase);
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------ Debug overlay (select the enemy)
    private void OnDrawGizmosSelected()
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
    [Tooltip("Look-around sweep (degrees each side) while waiting at a patrol point.")]
    public float lookAroundAngle = 55f;

    private Vector3 currentPatrolTarget;
    private bool patrolWaiting;
    private float patrolWaitTimer, lookBaseYaw, lookPhase, patrolSpeedFactor;
    private float socialPauseUntil, nextSocialTime, peerYieldUntil;
    private Transform socialPartner;

    // Search state
    private int searchPhase;               // 0 = walk to point, 1 = scan
    private Vector3 searchPoint;
    private float searchEndTime, scanTimer, scanBaseYaw, scanT;
    private int searchPointsVisited;

    private void ResetPatrol()
    {
        patrolWaiting = false;
        patrolSpeedFactor = Random.Range(0.85f, 1.15f);
        socialPauseUntil = 0f;
        socialPartner = null;
        nextSocialTime = Time.time + Random.Range(4f, 12f);
        peerYieldUntil = 0f;
        PickPatrolPoint();
    }

    private void PickPatrolPoint()
    {
        if (assignedZone != null)
            currentPatrolTarget = assignedZone.GetValidPatrolPoint(gameObject.GetInstanceID(), transform.position);
        else
            currentPatrolTarget = transform.position;
    }

    // ------------------------------------------------------------------ Patrol
    private void HandleUnaware(float dt)
    {
        if (assignedZone == null) return; // guard without a zone: stands idle, still perceives

        // Social pause: two calm patrollers stop facing each other, then leave on fresh points.
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

        // Partially aware: stop and look toward the stimulus (turn is rate-limited, never a snap).
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

        float speed = patrolSpeed * patrolSpeedFactor * Mathf.Clamp01(dist / 2f + 0.3f); // eases into the point
        float angle = Vector3.Angle(transform.forward, to);
        speed *= Mathf.Clamp01(Mathf.InverseLerp(120f, 35f, angle));                       // big turn = pivot first, never a snap
        if (Time.time < peerYieldUntil) speed = 0f;

        Vector3 dir = AvoidObstacles(to / dist);
        dir = PatrolPeerSteer(dir);

        desiredVelocity = Vector3.ClampMagnitude(dir * speed + GetSeparation() * 0.5f, patrolSpeed * 1.2f);
        desiredFacing = velocity.sqrMagnitude > 0.25f ? Vector3.zero : to; // face travel direction; pivot toward the goal when slow

        if (TrackStuck(speed > 0.3f)) PickPatrolPoint();
    }

    // Keep-right passing (head-on pairs both shift right = no deadlock) + priority yield for very close pairs.
    private Vector3 PatrolPeerSteer(Vector3 dir)
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
            if (Vector3.Dot(dir, off / d) < 0.3f) continue; // only peers in front

            shift += right * (1f - d / bumpTurnDistance);
            if (d < bumpTurnRadius * 2f && myId < o.GetInstanceID() && Time.time >= peerYieldUntil)
                peerYieldUntil = Time.time + Random.Range(0.4f, 1.0f); // lower id yields
        }

        Vector3 steered = dir + shift * 1.2f;
        return steered.sqrMagnitude > 0.0001f ? steered.normalized : dir;
    }

    private void TrySocialPause()
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

    private void BeginSocialPause(Transform partner, float duration)
    {
        socialPartner = partner;
        socialPauseUntil = Time.time + duration;
        nextSocialTime = Time.time + 25f;
        patrolWaiting = false;
    }

    // ------------------------------------------------------------------ Search
    private void EnterSearch()
    {
        searchEndTime = Time.time + searchDuration;
        searchPointsVisited = 0;
        searchPhase = 0;
        searchPoint = hasMemory ? lastKnownPos : (target != null ? target.position : transform.position);
    }

    private void HandleSearch(float dt)
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

    // Expanding search, biased along the player's last known velocity.
    private void NextSearchPoint()
    {
        searchPointsVisited++;
        Vector3 bias = Flat(lastKnownVel);
        bias = bias.sqrMagnitude > 0.25f ? bias.normalized : Flat(transform.forward);

        float spread = searchRadius * (1f + searchPointsVisited * 0.35f);
        Vector2 r = Random.insideUnitCircle * spread;
        searchPoint = lastKnownPos + bias * (searchPointsVisited * 1.5f) + new Vector3(r.x, 0f, r.y);
        searchPhase = 0;
    }

    private void ReturnToPatrol()
    {
        CleanupTokens();
        awareness = 0f;
        hasMemory = false;
        investigateAt = -1f;
        TransitionToState(MinionState.Unaware);
        PickPatrolPoint(); // zone returns a valid point near where the minion currently stands
    }
}