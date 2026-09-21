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
    private static readonly Collider[] searchBuffer = new Collider[16];
    private bool isDead = false;
    private bool isReacting = false;
    private float reactionTimer = 0f;
    private string lastPlayedLocomotionState = "";

    // Strafing variables
    private float strafeChangeTimer = 0f;
    private int strafeDirectionSign = 1;

    private void Awake()
    {
        healBeam = GetComponent<LineRenderer>();
        healBeam.enabled = false;
        healBeam.useWorldSpace = true; // Prevents LineRenderer stretching glitches
        animationEngine = GetComponent<EnemyAnimationEngine>();

        if (animProfile != null) animProfile.InitializeDictionary();
        if (playerTransform == null) playerTransform = FindObjectOfType<PlayerController>()?.transform;
    }

    private void OnEnable()
    {
        StopAllCoroutines();

        currentHealth = maxHealth;
        isDead = false;
        isReacting = false;
        isSpawning = false;
        pendingSpawnAfterHit = false;
        spawnTimer = batSpawnInterval;
        healBeam.enabled = false;
        currentAllyTarget = null;
        lastPlayedLocomotionState = "";

        if (playerTransform == null) playerTransform = FindObjectOfType<PlayerController>()?.transform;

        // Play spawn hand-raise animation on start/enable
        TriggerSpawnSequence();
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
        StopHealing();

        FacePlayerInstant();

        float totalDuration = spawnAnimationClip.length / Mathf.Max(0.01f, spawnAnimationSpeed);
        animationEngine.PlayAnimation(spawnAnimationClip, 0.05f, spawnAnimationSpeed);
        lastPlayedLocomotionState = "SpawnAnimation";

        float elapsed = 0f;
        bool batsSpawned = false;
        float halfDuration = totalDuration * 0.5f;

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

        HandleBatSpawningTimer();
        ManageBehavior();
    }

    private void HandleBatSpawningTimer()
    {
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f)
        {
            spawnTimer = batSpawnInterval;
            TriggerSpawnSequence();
        }
    }

    private void ManageBehavior()
    {
        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // 1. BACKWALK: If player gets too close, back away while facing the player
        if (distToPlayer < maintainPlayerDistance)
        {
            StopHealing();
            
            Vector3 dirAwayFromPlayer = (transform.position - playerTransform.position).normalized;
            dirAwayFromPlayer.y = 0f;
            transform.position += dirAwayFromPlayer * moveSpeed * Time.deltaTime;

            FacePlayerSmooth(15f * Time.deltaTime);

            AnimationClip backwalkClip = backwalkAnimationClip != null ? backwalkAnimationClip : (animProfile != null ? animProfile.walkClip : null);
            UpdateLocomotionAnimation("Backwalk", backwalkClip, 0.15f, backwalkAnimationSpeed);
            return;
        }

        // 2. FIND ALLY: Look for someone to heal
        if (currentAllyTarget == null || !currentAllyTarget.NeedsHealing())
        {
            StopHealing();
            FindLowestHealthAlly();
        }

        // 3. MOVE TO OR HEAL ALLY
        if (currentAllyTarget != null)
        {
            Transform allyTransform = currentAllyTarget.GetTransform();
            float distToAlly = Vector3.Distance(transform.position, allyTransform.position);

            if (distToAlly > healRange)
            {
                // Move towards ally
                StopHealing();
                Vector3 moveDir = (allyTransform.position - transform.position).normalized;
                moveDir.y = 0f;
                transform.position += moveDir * moveSpeed * Time.deltaTime;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * Time.deltaTime);

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

    private void FindLowestHealthAlly()
    {
        currentAllyTarget = null;
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, allySearchRadius, searchBuffer);
        
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = searchBuffer[i];
            if (hit.gameObject != gameObject)
            {
                IHealable healableAlly = hit.GetComponentInParent<IHealable>();
                
                if (healableAlly != null && healableAlly.GetTransform() != transform)
                {
                    if (healableAlly.NeedsHealing())
                    {
                        currentAllyTarget = healableAlly;
                        break; 
                    }
                }
            }
        }
    }

    private void ExecuteHealing(Transform allyTransform)
    {
        // HOLD POSITION: No movement delta is applied here, keeping the healer completely stationary.

        // Look at ally
        Vector3 lookDir = (allyTransform.position - transform.position).normalized;
        lookDir.y = 0f;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), 10f * Time.deltaTime);

        // Point staff at ally
        if (staffTransform != null)
        {
            staffTransform.LookAt(allyTransform.position + Vector3.up);
        }

        // Enable Beam VFX
        healBeam.enabled = true;
        healBeam.SetPosition(0, staffTransform != null ? staffTransform.position : transform.position);
        healBeam.SetPosition(1, allyTransform.position + Vector3.up);

        // Play dedicated healing casting animation using the heal animation speed multiplier
        AnimationClip activeHealClip = healAnimationClip != null ? healAnimationClip : (animProfile != null ? (animProfile.GetAnimationClip("HealAction") ?? animProfile.idleClip) : null);
        UpdateLocomotionAnimation("CastingHeal", activeHealClip, 0.15f, healAnimationSpeed);

        // Apply health over time
        currentAllyTarget.ReceiveHeal(healAmountPerSecond * Time.deltaTime);
    }

    private void StopHealing()
    {
        healBeam.enabled = false;
        
        if (staffTransform != null)
        {
            staffTransform.localRotation = Quaternion.Slerp(staffTransform.localRotation, Quaternion.identity, 10f * Time.deltaTime);
        }
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (isDead) return;

        StopHealing();
        currentHealth -= damage;
        
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        transform.position += pushDir.normalized * (force * 0.5f); 

        // If he gets hit while spawning, cancel the current spawn sequence immediately so bats don't spawn
        if (isSpawning)
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
            isSpawning = false;
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
        if (isDead) return;
        isDead = true;
        StopHealing();
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