using System.Collections;
using UnityEngine;

public class CombatHitboxController : MonoBehaviour
{
    [Header("Limb Transforms")]
    public Transform leftFist;
    public Transform rightFist;
    public Transform leftFoot;
    public Transform rightFoot;
    public Transform leftElbow;
    public Transform rightElbow;
    public Transform leftKnee;
    public Transform rightKnee;

    [Header("Default Hitbox Settings")]
    [Tooltip("Keep this tight (e.g. 0.3f to 0.35f) to prevent ghost hits at long range.")]
    public float hitboxRadius = 0.35f;
    public LayerMask enemyLayer;

    [Header("VFX Settings")]
    [Tooltip("Lifts the single-target VFX slightly so it doesn't get buried in grass or the floor.")]
    public float vfxHeightOffset = 0f;
    [Tooltip("Lifts the AOE VFX slightly off the ground.")]
    public float aoeVfxHeightOffset = 0.1f;
    [Tooltip("Pulls single-target VFX toward the camera so the enemy mesh doesn't clip half of it away.")]
    public float vfxTowardCameraOffset = 0f;
    [Tooltip("Logs to the Console every time a VFX is spawned (or when one is missing).")]
    public bool debugVFX = false;

    public enum VFXAnchor { Limb, ContactPoint, Midpoint, EnemyBody }
    [Tooltip("Where single-target VFX spawn:\n" +
             "Limb = on the striking fist/foot\n" +
             "ContactPoint = closest point on the enemy's collider\n" +
             "Midpoint = halfway between the two\n" +
             "EnemyBody = the enemy's chest/body center (same spot every time, follows the enemy's animation)")]
    public VFXAnchor vfxAnchor = VFXAnchor.EnemyBody;
    [Tooltip("Makes the single-target VFX stick to the enemy, so it moves with knockback.")]
    public bool vfxFollowsTarget = true;

    [Header("Hit Stop Settings")]
    [Tooltip("Time scale during hit stop. Slightly above 0 avoids odd animation/physics behaviour.")]
    public float hitStopTimeScale = 0.02f;

    private bool isHitStopping = false;
    private Transform currentActiveLimb;
    private bool isHitboxActive = false;
    private Collider[] hitResults = new Collider[10];

    // Cached references (avoids per-frame lookups)
    private PlayerController player;
    private IsoCameraRig camRig;
    private Camera mainCam;

    private void Awake()
    {
        player = GetComponentInParent<PlayerController>();
    }

    private void OnDisable()
    {
        // Safety: never leave the game frozen if this object is disabled mid hit-stop
        if (isHitStopping)
        {
            Time.timeScale = 1f;
            isHitStopping = false;
        }
    }

    public void TriggerHitbox(int limbIndex)
    {
        switch (limbIndex)
        {
            case 0: currentActiveLimb = leftFist; break;
            case 1: currentActiveLimb = rightFist; break;
            case 2: currentActiveLimb = rightFoot; break;
            case 3: currentActiveLimb = leftFoot; break;
            case 4: currentActiveLimb = leftElbow; break;
            case 5: currentActiveLimb = rightElbow; break;
            case 6: currentActiveLimb = leftKnee; break;
            case 7: currentActiveLimb = rightKnee; break;
            default: currentActiveLimb = rightFist; break;
        }
        isHitboxActive = true;
    }

    public void DisableHitbox()
    {
        isHitboxActive = false;
        currentActiveLimb = null;
    }

    // LateUpdate, not Update: the Animator poses the limbs AFTER Update, so reading limb positions
    // in Update would use last frame's pose (the hit and the VFX would lag one frame behind the fist).
    private void LateUpdate()
    {
        if (isHitboxActive && currentActiveLimb != null)
        {
            CheckForHits();
        }
    }

    private void CheckForHits()
    {
        // Lazy fallback in case the player wasn't found in Awake
        if (player == null) player = GetComponentInParent<PlayerController>();
        if (player == null) return;

        AttackData currentHit = null;

        if (player.CurrentState == player.AOEAttackState)
        {
            currentHit = player.specialAttackY;
        }
        else if (player.CurrentState == player.PowerPunchState)
        {
            if (player.equippedStyle != null)
                currentHit = player.equippedStyle.GetActiveChargeAttack();
        }
        else
        {
            if (player.equippedStyle != null && player.equippedStyle.lightComboSequence.Length > player.CurrentComboIndex)
            {
                currentHit = player.equippedStyle.lightComboSequence[player.CurrentComboIndex];
            }
        }

        if (currentHit == null) return;

        int finalDamage = Mathf.RoundToInt(currentHit.damage * player.CurrentChargeMultiplier);
        float finalKnockback = currentHit.knockbackForce * player.CurrentChargeMultiplier;

        if (currentHit.isAOE)
        {
            int hits = Physics.OverlapSphereNonAlloc(player.transform.position, currentHit.aoeRadius, hitResults, enemyLayer);
            int validHitCount = 0;

            for (int i = 0; i < hits; i++)
            {
                if (validHitCount >= currentHit.maxEnemiesHit) break;

                Collider enemyCol = hitResults[i];
                if (enemyCol.transform.IsChildOf(player.transform)) continue;

                Vector3 toEnemy = enemyCol.transform.position - player.transform.position;
                toEnemy.y = 0;
                Vector3 dirToEnemy = toEnemy.normalized;

                if (Vector3.Angle(player.transform.forward, dirToEnemy) <= currentHit.coneAngle / 2f)
                {
                    IDamageable damageable = enemyCol.GetComponent<IDamageable>();
                    if (damageable != null)
                    {
                        Vector3 hitPoint = enemyCol.ClosestPoint(player.transform.position);
                        Vector3 forceVector = (dirToEnemy * finalKnockback) + (Vector3.up * currentHit.verticalLift);
                        float finalForce = forceVector.magnitude > 0 ? forceVector.magnitude : finalKnockback;

                        Vector3 hitDirection = forceVector.normalized;
                        damageable.TakeDamage(finalDamage, hitPoint, hitDirection, finalForce, currentHit.customHitSound, currentHit.attackID, true);
                        validHitCount++;
                    }
                }
            }

            // AOE VFX: centered on the player, facing the player's direction, lifted slightly off the ground
            if (debugVFX) Debug.Log("[CombatHitbox] AOE branch used by attack: " + currentHit + " (targets hit: " + validHitCount + ")", this);
            SpawnVFX(currentHit.customVFX, player.transform.position, player.transform.forward, aoeVfxHeightOffset, false);

            if (validHitCount > 0) TriggerJuice(currentHit);
            DisableHitbox();
        }
        else
        {
            int hits = Physics.OverlapSphereNonAlloc(currentActiveLimb.position, hitboxRadius, hitResults, enemyLayer);

            for (int i = 0; i < hits; i++)
            {
                Collider enemyCol = hitResults[i];
                // Skip anything belonging to the player (own body colliders on a parent or child object)
                if (enemyCol.transform.IsChildOf(player.transform) || enemyCol.transform.IsChildOf(transform)) continue;

                IDamageable damageable = enemyCol.GetComponent<IDamageable>();

                if (damageable != null)
                {
                    // Knockback direction (unchanged): from attacker to enemy, flattened
                    Vector3 hitDirection = (enemyCol.transform.position - transform.position).normalized;
                    hitDirection.y = 0;

                    // Exact impact point on the enemy's collider surface
                    Vector3 exactHitPoint = enemyCol.ClosestPoint(currentActiveLimb.position);

                    damageable.TakeDamage(finalDamage, exactHitPoint, hitDirection, finalKnockback, currentHit.customHitSound, currentHit.attackID, false);

                    if (debugVFX) Debug.Log("[CombatHitbox] Single-target hit by attack: " + currentHit + " on " + enemyCol.name, this);

                    // Pick where the VFX appears: on the striking limb, on the enemy surface, or halfway
                    Vector3 vfxPoint;
                    Transform followTarget = enemyCol.transform;
                    switch (vfxAnchor)
                    {
                        case VFXAnchor.ContactPoint: vfxPoint = exactHitPoint; break;
                        case VFXAnchor.Midpoint: vfxPoint = (currentActiveLimb.position + exactHitPoint) * 0.5f; break;
                        case VFXAnchor.EnemyBody: vfxPoint = GetEnemyBodyPoint(enemyCol, out followTarget); break;
                        default: vfxPoint = currentActiveLimb.position; break;
                    }

                    // vfxTowardCameraOffset should stay at 0 unless you really need it (see notes)
                    SpawnVFX(currentHit.customVFX, vfxPoint, player.transform.forward, vfxHeightOffset, true,
                             vfxFollowsTarget ? followTarget : null);

                    TriggerJuice(currentHit);
                    DisableHitbox();
                    break;
                }
            }
        }
    }

    // Spawns a VFX prefab with a safe rotation, optional pull toward the camera, and automatic cleanup
    private void SpawnVFX(ParticleSystem prefab, Vector3 point, Vector3 direction, float heightOffset, bool pullTowardCamera, Transform followTarget = null)
    {
        if (prefab == null)
        {
            if (debugVFX) Debug.LogWarning("[CombatHitbox] This attack has no customVFX assigned in its AttackData.", this);
            return;
        }

        // Flatten the direction and make sure it is never zero (LookRotation(0) breaks)
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = player.transform.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;

        Vector3 pos = point + Vector3.up * heightOffset;

        if (pullTowardCamera)
        {
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam != null)
                pos += (mainCam.transform.position - pos).normalized * vfxTowardCameraOffset;
        }

        ParticleSystem vfx = Instantiate(prefab, pos, Quaternion.LookRotation(direction.normalized));
        vfx.Play(true);

        // Stick the VFX to the enemy so it moves with knockback
        if (followTarget != null)
        {
            VFXFollowTarget follow = vfx.gameObject.AddComponent<VFXFollowTarget>();
            follow.Attach(followTarget);
        }

        Destroy(vfx.gameObject, GetVFXLifetime(vfx));

        if (debugVFX) Debug.Log("[CombatHitbox] Spawned " + vfx.name + " at " + pos, vfx);
    }

    // A stable point on the enemy's body that follows its animation.
    // Humanoid rigs: the chest bone (follows the visible body, even if the collider stays on the root).
    // Anything else: the center of the enemy's collider.
    private Vector3 GetEnemyBodyPoint(Collider enemyCol, out Transform followTarget)
    {
        Animator anim = enemyCol.GetComponentInParent<Animator>();
        if (anim != null && anim.isHuman)
        {
            Transform bone = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (bone != null)
            {
                followTarget = bone;
                return bone.position;
            }
        }

        followTarget = enemyCol.transform;
        return enemyCol.bounds.center;
    }

    // Longest lifetime across the root and all child particle systems, so nothing gets cut off
    private float GetVFXLifetime(ParticleSystem root)
    {
        float max = 0f;
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule m = ps.main;
            float total = m.startDelay.constantMax + m.duration + m.startLifetime.constantMax;
            max = Mathf.Max(max, total);
        }
        return max + 0.1f;
    }

    private void TriggerJuice(AttackData hitData)
    {
        if (!isHitStopping)
        {
            StartCoroutine(HitStopRoutine(hitData.hitStopDuration));
        }

        if (camRig == null && Camera.main != null)
            camRig = Camera.main.GetComponent<IsoCameraRig>();

        if (camRig != null)
        {
            camRig.TriggerShake(hitData.cameraShakeDuration, hitData.cameraShakeIntensity);
        }
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        isHitStopping = true;
        Time.timeScale = hitStopTimeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        isHitStopping = false;
    }

    private void OnDrawGizmos()
    {
        if (isHitboxActive && currentActiveLimb != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(currentActiveLimb.position, hitboxRadius);
        }
    }
}