using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Animation;
using CombatSystem.Data;

public interface IHealable
{
    bool NeedsHealing();
    void ReceiveHeal(float amount);
    Transform GetTransform();
}

[RequireComponent(typeof(LineRenderer))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class HealerController : MonoBehaviour, IDamageable
{
    [Header("References")]
    public Transform playerTransform;
    public Transform staffTransform;
    public GameObject batPrefab;
    private LineRenderer healBeam;
    private Collider bodyCollider;
    private HealerHealVFX healVFX;
    private HealerSummonVFX summonVFX;
    private HealReceiveFX allyFX;
    private Transform allyFXOwner;

    [Header("Core References & Animation Profiles")]
    public EnemyAnimProfile animProfile;
    private EnemyAnimationEngine animationEngine;

    [Header("Animation Clips")]
    [Tooltip("The hand-raise animation played on start and every time a bat is spawned.")]
    public AnimationClip spawnAnimationClip;
    [Tooltip("Animation played when retreating while keeping eyes on the player.")]
    public AnimationClip backwalkAnimationClip;
    [Tooltip("Dedicated animation played while holding position and healing an ally.")]
    public AnimationClip healAnimationClip;
    private bool isSpawning = false;
    private Coroutine spawnRoutine;
    private bool pendingSpawnAfterHit = false;

    [Header("Animation Speed Controls")]
    public float spawnAnimationSpeed = 1.0f;
    public float backwalkAnimationSpeed = 1.0f;
    public float healAnimationSpeed = 1.0f;
    public float strafeAnimationSpeed = 1.0f;
    public float walkAnimationSpeed = 1.0f;
    public float idleAnimationSpeed = 1.0f;

    [Header("Healer Stats")]
    public float maxHealth = 100f;
    private float currentHealth;
    public float moveSpeed = 3.5f;

    [Header("Spawning Bats")]
    public float batSpawnInterval = 6.0f;
    public Transform batSpawnPoint;
    private float spawnTimer;

    [Header("Healing & Positioning")]
    public float healRange = 8.0f;
    public float healAmountPerSecond = 15f;
    [Tooltip("If the player gets closer than this, the healer backs away.")]
    public float maintainPlayerDistance = 6.0f;
    [Tooltip("Radius to search for allies.")]
    public float allySearchRadius = 15.0f;

    private IHealable currentAllyTarget;
    // Each healer owns its reusable search buffer. It grows only when crowded.
    private Collider[] searchBuffer = new Collider[32];
    private readonly HashSet<Transform> seenAllies = new HashSet<Transform>();
    private readonly Dictionary<Transform, ulong> lastServed = new Dictionary<Transform, ulong>();
    private readonly List<Transform> expiredHistory = new List<Transform>();
    private ulong serviceOrder;
    private Transform claimedTarget;
    private float healingTurnTimer;

    // One reservation per ally, shared by every healer.
    private static readonly Dictionary<Transform, HealerController> targetOwners =
        new Dictionary<Transform, HealerController>();

    [Header("Healing Target Sharing")]
    [Tooltip("Seconds of actual healing before trying a different injured, unclaimed ally.")]
    [Min(0.1f)] [SerializeField] private float healingTurnDuration = 2.5f;
    [Tooltip("Layers containing ally colliders. Everything preserves the original search.")]
    [SerializeField] private LayerMask allyLayers = ~0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetReservations()
    {
        targetOwners.Clear();
    }
    private bool isDead = false;
    private bool isReacting = false;
    private float reactionTimer = 0f;
    private string lastPlayedLocomotionState = "";

    // Strafing variables
    private float strafeChangeTimer = 0f;
    private int strafeDirectionSign = 1;

    [Header("State Stability")]
    [Tooltip("Extra distance the player must retreat before the Healer stops backing away.")]
    [SerializeField] private float distanceBuffer = 1.5f;
    [Tooltip("Extra distance past Heal Range before the Healer stops healing and walks to the ally.")]
    [SerializeField] private float healRangeBuffer = 1.0f;
    [Tooltip("Seconds between ally searches.")]
    [SerializeField] private float allySearchInterval = 0.3f;

    private bool isBackingAway;
    private bool isHealingInRange;
    private float allySearchTimer;

    [Header("Camera Visibility Gate")]
    [Tooltip("The Healer never spawns bats until it has been inside the camera view at least once.")]
    [SerializeField] private bool requireSightingBeforeSpawning = true;
    [Tooltip("If on, the bat timer also pauses whenever the Healer is off-screen, not only before the first sighting.")]
    [SerializeField] private bool spawnOnlyWhileVisible = false;
    [Tooltip("How far inside the screen edge (0-0.5 of the screen) the Healer must be to count as seen.")]
    [SerializeField] private float viewportEdgePadding = 0.03f;
    [Tooltip("Height above the Healer's pivot used for the visibility check (about chest height).")]
    [SerializeField] private float visibilityHeightOffset = 1.0f;

    private Camera viewCamera;
    private bool hasBeenSeen;

    private void OnDisable()
    {
        ClearAllyTarget();
        lastServed.Clear();
        seenAllies.Clear();
        expiredHistory.Clear();
        System.Array.Clear(searchBuffer, 0, searchBuffer.Length);
        // Disabling the script (pool / ship passenger) must also cancel a running spawn sequence
        StopAllCoroutines();
        spawnRoutine = null;
        isSpawning = false;
        pendingSpawnAfterHit = false;
        if (healBeam != null) healBeam.enabled = false;
        if (healVFX != null) healVFX.StopCasting();
        if (summonVFX != null) summonVFX.Cancel();
    }

    private void Awake()
    {
        healBeam = GetComponent<LineRenderer>();
        healBeam.enabled = false;
        healBeam.useWorldSpace = true; // Prevents LineRenderer stretching glitches
        bodyCollider = GetComponent<Collider>();
        animationEngine = GetComponent<EnemyAnimationEngine>();

        healVFX = GetComponent<HealerHealVFX>();
        if (healVFX == null) healVFX = gameObject.AddComponent<HealerHealVFX>();
        healVFX.staff = staffTransform;

        summonVFX = GetComponent<HealerSummonVFX>();
        if (summonVFX == null) summonVFX = gameObject.AddComponent<HealerSummonVFX>();

        if (animProfile != null) animProfile.InitializeDictionary();
        if (playerTransform == null) playerTransform = FindObjectOfType<PlayerController>()?.transform;
    }

    private void OnEnable()
    {
        StopAllCoroutines();

        ClearAllyTarget();
        lastServed.Clear();
        serviceOrder = 0;
        spawnRoutine = null;
        reactionTimer = 0f;
        currentHealth = maxHealth;
        isDead = false;
        isReacting = false;
        isSpawning = false;
        pendingSpawnAfterHit = false;
        spawnTimer = batSpawnInterval;
        healBeam.enabled = false;
        currentAllyTarget = null;
        lastPlayedLocomotionState = "";

        isBackingAway = false;
        isHealingInRange = false;
        allySearchTimer = Random.Range(0f, Mathf.Max(0.05f, allySearchInterval));
        hasBeenSeen = !requireSightingBeforeSpawning;

        // The pool disables the collider before mounting; make sure the Healer is hittable once active
        if (bodyCollider != null) bodyCollider.enabled = true;

        if (playerTransform == null) playerTransform = FindObjectOfType<PlayerController>()?.transform;

        // The opening hand-raise and first bats now wait for the first camera sighting
        // (handled in HandleBatSpawningTimer). With the gate turned off, spawn immediately as before.
        if (hasBeenSeen) TriggerSpawnSequence();
    }

    private void TriggerSpawnSequence()
    {
        if (spawnAnimationClip != null && animationEngine != null)
        {
            if (spawnRoutine != null) StopCoroutine(spawnRoutine);
            spawnRoutine = StartCoroutine(SpawnAnimationRoutine());
        }
    }

    private IEnumerator SpawnAnimationRoutine()
    {
        isSpawning = true;
        ClearAllyTarget();
        StopHealing();

        FacePlayerInstant();

        float totalDuration = spawnAnimationClip.length / Mathf.Max(0.01f, spawnAnimationSpeed);
        animationEngine.PlayAnimation(spawnAnimationClip, 0.05f, spawnAnimationSpeed);
        lastPlayedLocomotionState = "SpawnAnimation";

        float elapsed = 0f;
        bool batsSpawned = false;
        float halfDuration = totalDuration * 0.5f;

        // the magic circle spins up between the raised hands until the bat appears
        if (summonVFX != null) summonVFX.BeginCharge(batSpawnPoint, halfDuration);

        // Stay stationary while spawning, but smoothly track and face the player
        while (elapsed < totalDuration)
        {
            if (isDead) yield break;

            float dt = Time.deltaTime;
            elapsed += dt;

            FacePlayerSmooth(10f * dt);

            // Spawn bats precisely at the halfway mark of the animation
            if (!batsSpawned && elapsed >= halfDuration)
            {
                SpawnBatsAction();
                batsSpawned = true;
            }

            yield return null;
        }

        isSpawning = false;
        spawnRoutine = null;
    }

    private void FacePlayerInstant()
    {
        if (playerTransform == null) return;
        Vector3 lookDir = (playerTransform.position - transform.position);
        lookDir.y = 0f;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    private void FacePlayerSmooth(float t)
    {
        if (playerTransform == null) return;
        Vector3 targetDir = (playerTransform.position - transform.position);
        targetDir.y = 0f;
        if (targetDir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(targetDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }
    }

    private void SpawnBatsAction()
    {
        if (summonVFX != null) summonVFX.Release();   // flash, sparks and a ring where the bat appears

        if (EnemyObjectPool.Instance != null && batPrefab != null && batSpawnPoint != null)
        {
            EnemyObjectPool.Instance.GetPooledEnemy(batPrefab, batSpawnPoint.position, Quaternion.identity);
        }
    }

    private void Update()
    {
        if (isDead || playerTransform == null) return;

        // Handle hit reaction pause window
        if (isReacting)
        {
            reactionTimer -= Time.deltaTime;
            if (reactionTimer <= 0f)
            {
                isReacting = false;

                // Once he is done getting hit, if his spawn was interrupted, play animation and then spawn!
                if (pendingSpawnAfterHit)
                {
                    pendingSpawnAfterHit = false;
                    TriggerSpawnSequence();
                }
            }
            return;
        }

        if (isSpawning) return;

        // Also resume an interrupted summon if no hit-reaction clip was available.
        if (pendingSpawnAfterHit)
        {
            pendingSpawnAfterHit = false;
            TriggerSpawnSequence();
            if (isSpawning) return;
        }

        HandleBatSpawningTimer();
        // StartCoroutine runs immediately up to its first yield.
        if (isSpawning) return;
        ManageBehavior();
    }

    private void HandleBatSpawningTimer()
    {
        bool visible = (!hasBeenSeen || spawnOnlyWhileVisible) && IsInPlayerCameraView();

        // Gate: no bats until the camera has seen this Healer
        if (!hasBeenSeen)
        {
            if (!visible) return;

            hasBeenSeen = true;
            spawnTimer = batSpawnInterval;
            TriggerSpawnSequence(); // opening hand-raise + first bats, now that the player can see it
            return;
        }

        if (spawnOnlyWhileVisible && !visible) return;

        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            spawnTimer = batSpawnInterval;
            TriggerSpawnSequence();
        }
    }

    private bool IsInPlayerCameraView()
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
            if (viewCamera == null) return false;
        }

        Vector3 vp = viewCamera.WorldToViewportPoint(transform.position + Vector3.up * visibilityHeightOffset);

        return vp.z > 0f
            && vp.x > viewportEdgePadding && vp.x < 1f - viewportEdgePadding
            && vp.y > viewportEdgePadding && vp.y < 1f - viewportEdgePadding;
    }

    private void ManageBehavior()
    {
        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // 1. BACKWALK: If player gets too close, back away while facing the player.
        // Hysteresis: starts below maintainPlayerDistance, stops only beyond maintainPlayerDistance + distanceBuffer.
        if (isBackingAway)
        {
            if (distToPlayer > maintainPlayerDistance + distanceBuffer) isBackingAway = false;
        }
        else if (distToPlayer < maintainPlayerDistance)
        {
            isBackingAway = true;
        }

        if (isBackingAway)
        {
            ClearAllyTarget();
            StopHealing();

            Vector3 dirAwayFromPlayer = (transform.position - playerTransform.position).normalized;
            dirAwayFromPlayer.y = 0f;
            transform.position += dirAwayFromPlayer * moveSpeed * Time.deltaTime;

            FacePlayerSmooth(15f * Time.deltaTime);

            AnimationClip backwalkClip = backwalkAnimationClip != null ? backwalkAnimationClip : (animProfile != null ? animProfile.walkClip : null);
            UpdateLocomotionAnimation("Backwalk", backwalkClip, 0.15f, backwalkAnimationSpeed);
            return;
        }

        // 2. Validate every frame, but perform physics searches only on the timer.
        if (currentAllyTarget != null &&
            (!TryGetAvailableAlly(currentAllyTarget, out Transform validTarget) ||
             validTarget != claimedTarget || IsClaimedByAnother(validTarget) ||
             (validTarget.position - transform.position).sqrMagnitude >
                 Mathf.Max(0f, allySearchRadius) * Mathf.Max(0f, allySearchRadius)))
        {
            ClearAllyTarget();
        }

        allySearchTimer -= Time.deltaTime;
        if (allySearchTimer <= 0f)
        {
            allySearchTimer = Mathf.Max(0.05f, allySearchInterval);
            if (currentAllyTarget == null ||
                healingTurnTimer >= Mathf.Max(0.1f, healingTurnDuration))
            {
                FindNextAlly();
            }
        }

        // 3. MOVE TO OR HEAL ALLY
        if (currentAllyTarget != null)
        {
            Transform allyTransform = currentAllyTarget.GetTransform();
            float distToAlly = Vector3.Distance(transform.position, allyTransform.position);

            // Heal-range hysteresis: enters at healRange, leaves only beyond healRange + healRangeBuffer
            if (isHealingInRange)
            {
                if (distToAlly > healRange + healRangeBuffer) isHealingInRange = false;
            }
            else if (distToAlly <= healRange)
            {
                isHealingInRange = true;
            }

            if (!isHealingInRange)
            {
                // Move towards ally
                StopHealing();
                Vector3 moveDir = (allyTransform.position - transform.position).normalized;
                moveDir.y = 0f;
                transform.position += moveDir * moveSpeed * Time.deltaTime;
                if (moveDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * Time.deltaTime);
                }

                UpdateLocomotionAnimation("MoveToAlly", animProfile != null ? animProfile.walkClip : null, 0.15f, walkAnimationSpeed);
            }
            else
            {
                // In range: HOLD POSITION, point staff, enable beam, and play healing animation!
                ExecuteHealing(allyTransform);
            }
        }
        else
        {
            // 4. IDLE / BASIC STRAFE: When safe and no targets need healing, strafe left and right while facing player
            StopHealing();

            strafeChangeTimer -= Time.deltaTime;
            if (strafeChangeTimer <= 0f)
            {
                strafeChangeTimer = Random.Range(2.0f, 4.0f);
                strafeDirectionSign = Random.value > 0.5f ? 1 : -1;
            }

            Vector3 rightDir = Vector3.Cross(Vector3.up, transform.forward).normalized;
            Vector3 strafeMove = rightDir * (strafeDirectionSign * (moveSpeed * 0.4f)) * Time.deltaTime;
            transform.position += strafeMove;

            FacePlayerSmooth(10f * Time.deltaTime);

            if (animProfile != null)
            {
                AnimationClip strafeClip = strafeDirectionSign > 0 ? animProfile.strafeRightClip : animProfile.strafeLeftClip;
                if (strafeClip == null) strafeClip = animProfile.walkClip;
                string strafeKey = strafeDirectionSign > 0 ? "StrafeRight" : "StrafeLeft";
                UpdateLocomotionAnimation(strafeKey, strafeClip, 0.15f, strafeAnimationSpeed);
            }
        }
    }

    private void UpdateLocomotionAnimation(string stateKey, AnimationClip clip, float duration, float speedMultiplier = 1.0f)
    {
        if (animationEngine == null || animProfile == null || isDead || isReacting || isSpawning) return;

        if (lastPlayedLocomotionState != stateKey)
        {
            lastPlayedLocomotionState = stateKey;
            if (clip != null)
            {
                animationEngine.PlayAnimation(clip, duration, speedMultiplier);
            }
        }
    }

    // Interfaces do not use Unity's destroyed-object null check automatically.
    private bool TryGetAvailableAlly(IHealable ally, out Transform allyTransform)
    {
        allyTransform = null;
        if (ally == null) return false;
        if (ally is UnityEngine.Object unityObject && unityObject == null) return false;
        if (ally is Behaviour behaviour && !behaviour.isActiveAndEnabled) return false;

        allyTransform = ally.GetTransform();
        return allyTransform != null && allyTransform != transform &&
            allyTransform != playerTransform && allyTransform.gameObject.activeInHierarchy &&
            ally.NeedsHealing();
    }

    private bool IsClaimedByAnother(Transform target)
    {
        if (!targetOwners.TryGetValue(target, out HealerController owner)) return false;

        // Recover stale reservations after disable, destruction or pooling.
        if (owner == null || !owner.isActiveAndEnabled || owner.isDead ||
            owner.claimedTarget != target)
        {
            targetOwners.Remove(target);
            return false;
        }
        return owner != this;
    }

    private void ClearAllyTarget()
    {
        // ReferenceEquals lets us also remove a destroyed Unity object as a key.
        if (!ReferenceEquals(claimedTarget, null) &&
            targetOwners.TryGetValue(claimedTarget, out HealerController owner) &&
            owner == this)
        {
            targetOwners.Remove(claimedTarget);
        }

        if (currentAllyTarget != null) StopHealing();
        claimedTarget = null;
        currentAllyTarget = null;
        allyFX = null;
        allyFXOwner = null;
        isHealingInRange = false;
        healingTurnTimer = 0f;
    }

    private void FindNextAlly()
    {
        // Remove destroyed/disabled entries without modifying a dictionary during enumeration.
        expiredHistory.Clear();
        foreach (var entry in lastServed)
        {
            if (entry.Key == null || !entry.Key.gameObject.activeInHierarchy)
                expiredHistory.Add(entry.Key);
        }
        for (int i = 0; i < expiredHistory.Count; i++)
            lastServed.Remove(expiredHistory[i]);

        int hitCount;
        float radius = Mathf.Max(0f, allySearchRadius);
        do
        {
            hitCount = Physics.OverlapSphereNonAlloc(
                transform.position, radius, searchBuffer, allyLayers,
                QueryTriggerInteraction.UseGlobal);
            if (hitCount < searchBuffer.Length) break;

            // A full buffer may have omitted allies. Grow and repeat instead of
            // silently ignoring them; subsequent searches reuse the larger buffer.
            System.Array.Resize(ref searchBuffer, searchBuffer.Length * 2);
        } while (true);

        seenAllies.Clear();
        IHealable bestAlly = null;
        Transform bestTransform = null;
        ulong oldestTurn = ulong.MaxValue;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = searchBuffer[i];
            if (hit == null) continue;
            IHealable ally = hit.GetComponentInParent<IHealable>();
            if (!TryGetAvailableAlly(ally, out Transform candidate)) continue;
            if (!seenAllies.Add(candidate) || candidate == claimedTarget ||
                IsClaimedByAnother(candidate)) continue;

            float distance = (candidate.position - transform.position).sqrMagnitude;
            if (distance > radius * radius) continue;

            // Serve never-visited allies first, then the least recently served.
            // Distance only breaks ties: collider order no longer chooses the target.
            lastServed.TryGetValue(candidate, out ulong previousTurn);
            if (previousTurn < oldestTurn ||
                (previousTurn == oldestTurn && distance < bestDistance))
            {
                bestAlly = ally;
                bestTransform = candidate;
                oldestTurn = previousTurn;
                bestDistance = distance;
            }
        }

        System.Array.Clear(searchBuffer, 0, searchBuffer.Length);
        seenAllies.Clear();
        // If all other allies are claimed or healthy, keep helping our current ally.
        if (bestAlly == null) return;

        ClearAllyTarget();
        currentAllyTarget = bestAlly;
        claimedTarget = bestTransform;
        targetOwners[bestTransform] = this;
        lastServed[bestTransform] = ++serviceOrder;
    }

    private void ExecuteHealing(Transform allyTransform)
    {
        // HOLD POSITION: No movement delta is applied here, keeping the healer completely stationary.

        // Look at ally
        Vector3 lookDir = (allyTransform.position - transform.position).normalized;
        lookDir.y = 0f;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), 10f * Time.deltaTime);
        }

        // Point staff at ally
        if (staffTransform != null)
        {
            staffTransform.LookAt(allyTransform.position + Vector3.up);
        }

        // Heal VFX: energy stream from the staff + aura on the ally (replaces the old line renderer beam)
        if (allyFX == null || allyFXOwner != allyTransform)
        {
            allyFXOwner = allyTransform;
            allyFX = allyTransform.GetComponent<HealReceiveFX>();
            if (allyFX == null) allyFX = allyTransform.gameObject.AddComponent<HealReceiveFX>();
        }
        if (healVFX != null) healVFX.Cast(allyFX);   // the aura on the ally starts by itself when the first stream particles arrive

        // Play dedicated healing casting animation using the heal animation speed multiplier
        AnimationClip activeHealClip = healAnimationClip != null ? healAnimationClip : (animProfile != null ? (animProfile.GetAnimationClip("HealAction") ?? animProfile.idleClip) : null);
        UpdateLocomotionAnimation("CastingHeal", activeHealClip, 0.15f, healAnimationSpeed);

        // Apply health over time
        healingTurnTimer += Time.deltaTime;
        currentAllyTarget.ReceiveHeal(healAmountPerSecond * Time.deltaTime);
    }

    private void StopHealing()
    {
        healBeam.enabled = false;
        if (healVFX != null) healVFX.StopCasting();

        if (staffTransform != null)
        {
            staffTransform.localRotation = Quaternion.Slerp(staffTransform.localRotation, Quaternion.identity, 10f * Time.deltaTime);
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        ClearAllyTarget();
        StopHealing();
        currentHealth -= damage;

        // Deliberately ignore force: damage and hit reactions remain, but no knockback.
        // Keep the signature unchanged for existing IDamageable callers.

        // If he gets hit while spawning, cancel the current spawn sequence immediately so bats don't spawn
        if (isSpawning)
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
            isSpawning = false;
            if (summonVFX != null) summonVFX.Cancel();   // the summon fizzles out
            pendingSpawnAfterHit = true; // Mark to replay spawn animation & spawn bats AFTER getting hit
        }

        PlayHitReaction(attackID, hitDirection);

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

        if (hitDirection == Vector3.zero) hitDirection = -transform.forward;
        Vector3 localDir = transform.InverseTransformDirection(hitDirection.normalized);
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
            isReacting = true;
            reactionTimer = hitAnim.clip.length / Mathf.Max(0.01f, hitAnim.playbackSpeed);
            lastPlayedLocomotionState = "Reaction";
        }
    }

    private void Die()
    {
        if (summonVFX != null) summonVFX.Cancel();
        if (isDead) return;
        isDead = true;
        ClearAllyTarget();
        StopHealing();

        if (TryGetComponent<EnemySoulDrop>(out var soulDrop))
        {
            soulDrop.TriggerSoulDrop();
        }

        isReacting = false;
        isSpawning = false;
        pendingSpawnAfterHit = false;
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);

        if (animProfile != null && animProfile.deathClip != null && animationEngine != null)
        {
            animationEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
        }

        StartCoroutine(DeathDisappearRoutine());
    }

    private IEnumerator DeathDisappearRoutine()
    {
        yield return new WaitForSeconds(1.0f);

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, healRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maintainPlayerDistance);
    }
}