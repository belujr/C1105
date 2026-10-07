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

    [Header("Enemy hit feedback (glow, distortion, debris, soul wisps)")]
    [Tooltip("Plays the HitFeedbackManager effects on the enemy when a hit lands.")]
    public bool enemyHitFeedback = true;
    [Tooltip("Damage that counts as a full-strength hit for the feedback effects.")]
    public float heavyHitDamage = 30f;

    public enum VFXAnchor { Limb, ContactPoint, Midpoint, EnemyBody }
    [Tooltip("Where single-target VFX spawn:\n" +
             "Limb = on the striking fist/foot\n" +
             "ContactPoint = closest point on the enemy's collider\n" +
             "Midpoint = halfway between the two\n" +
             "EnemyBody = the enemy's chest/body center (same spot every time, follows the enemy's animation)")]
    public VFXAnchor vfxAnchor = VFXAnchor.EnemyBody;
    [Tooltip("Makes the single-target VFX stick to the enemy, so it moves with knockback.")]
    public bool vfxFollowsTarget = true;

    [Header("Swing VFX (motion lines / swoosh while a limb moves)")]
    [Tooltip("Safety: the swing VFX is stopped after this many seconds even if the animation never calls DisableHitbox.")]
    public float swingMaxDuration = 0.8f;

    [Header("Skill Preview Screen (leave empty on the real player)")]
    [Tooltip("Only for the preview dummy: a point on the preview enemy's chest where the hit VFX always appears.")]
    public Transform previewHitPoint;
    [HideInInspector] public AttackData previewAttack; // set by SkillPreviewManager

    [Header("Hit Stop Settings")]
    [Tooltip("Time scale during hit stop. Slightly above 0 avoids odd animation/physics behaviour.")]
    public float hitStopTimeScale = 0.02f;

    private bool isHitStopping = false;
    private Transform currentActiveLimb;
    private bool isHitboxActive = false;
    private Collider[] hitResults = new Collider[10];

    // The swing VFX currently attached to a limb
    private GameObject activeSwing;
    private float swingStopTime;

    // True while a SwooshStamp has already been started for the current swing (so TriggerHitbox doesn't start a second one)
    private bool swingStampStarted;

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

        StopSwingVFX();
    }

    private Transform GetLimb(int limbIndex)
    {
        switch (limbIndex)
        {
            case 0: return leftFist;
            case 1: return rightFist;
            case 2: return rightFoot;
            case 3: return leftFoot;
            case 4: return leftElbow;
            case 5: return rightElbow;
            case 6: return leftKnee;
            case 7: return rightKnee;
            default: return rightFist;
        }
    }

    public void TriggerHitbox(int limbIndex)
    {
        currentActiveLimb = GetLimb(limbIndex);
        isHitboxActive = true;

        // If an earlier BeginSwing event already started the swing VFX, keep it. Otherwise start it now.
        if (activeSwing == null && !swingStampStarted) StartSwingVFX(currentActiveLimb);
    }

    // OPTIONAL animation event. Put it at the START of the strike motion (the frame the leg begins to lift
    // or the arm begins to move), so the swoosh / motion lines cover the whole movement instead of
    // only starting at the hit window. Same limb numbers as TriggerHitbox.
    public void BeginSwing(int limbIndex)
    {
        StartSwingVFX(GetLimb(limbIndex));
    }

    // OPTIONAL animation event: ends only the swing VFX (DisableHitbox also ends it)
    public void EndSwing()
    {
        StopSwingVFX();
    }

    // Called by the animation event at the end of the strike: ends hit detection AND the swing VFX
    public void DisableHitbox()
    {
        StopHitDetection();
        StopSwingVFX();
    }

    // Ends hit detection only. Used internally after a hit lands, so the swing VFX can keep following the limb
    private void StopHitDetection()
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

        if (activeSwing != null && Time.unscaledTime >= swingStopTime)
        {
            StopSwingVFX();
        }
    }

    private void CheckForHits()
    {
        // Lazy fallback in case the player wasn't found in Awake
        if (player == null) player = GetComponentInParent<PlayerController>();
        if (player == null) return;

        AttackData currentHit = ResolveCurrentAttack();

        if (currentHit == null) return;

        int finalDamage = Mathf.RoundToInt(currentHit.damage * player.CurrentChargeMultiplier);
        float finalKnockback = currentHit.knockbackForce * player.CurrentChargeMultiplier;

        // In the skill preview, AOE-flagged attacks (e.g. spinning kicks) are treated like single hits.
        // The AOE branch spawns the VFX at the player's feet the instant the hitbox starts, which is
        // wrong for a preview that should show the impact on the enemy.
        bool runAsAoe = currentHit.isAOE && previewAttack == null;

        if (runAsAoe)
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
                        PlayEnemyHitFeedback(damageable as Component, hitPoint, hitDirection, finalDamage);
                        validHitCount++;
                    }
                }
            }

            // AOE VFX: centered on the player, facing the player's direction, lifted slightly off the ground
            if (debugVFX) Debug.Log("[CombatHitbox] AOE branch used by attack: " + currentHit + " (targets hit: " + validHitCount + ")", this);
            SpawnVFX(currentHit.customVFX, player.transform.position, player.transform.forward, aoeVfxHeightOffset, false);

            // Sweep the grass away in the attack's arc, starting at the player and moving outward
            if (currentHit.cutsGrass)
                GrassCutManager.Cut(player.transform.position, player.transform.forward, currentHit.aoeRadius, currentHit.coneAngle);

            if (validHitCount > 0) TriggerJuice(currentHit);
            StopHitDetection();
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

                    // glow + distortion + debris + soul wisps on the enemy (skill preview uses its fixed hit point)
                    Vector3 feedbackPoint = (previewAttack != null && previewHitPoint != null) ? previewHitPoint.position : exactHitPoint;
                    PlayEnemyHitFeedback(damageable as Component, feedbackPoint, hitDirection, finalDamage);

                    if (debugVFX)
                    {
                        string previewInfo = previewAttack == null ? "" :
                            (previewHitPoint != null ? " | PREVIEW POINT USED" : " | PREVIEW POINT MISSING (not assigned)");
                        Debug.Log("[CombatHitbox] Single-target hit by attack: " + currentHit + " on " + enemyCol.name +
                                  " | anchor: " + vfxAnchor + previewInfo, this);
                    }

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

                    // Targets like the beacon shield want the VFX exactly where the fist touched them
                    BeaconHitRelay hitRelay = enemyCol.GetComponent<BeaconHitRelay>();
                    if (hitRelay != null && hitRelay.hitVfxAtContactPoint)
                    {
                        vfxPoint = hitRelay.GetSurfacePoint(currentActiveLimb.position);
                        followTarget = enemyCol.transform;
                    }

                    // Skill-preview screen: always play the VFX at the fixed point on the preview enemy
                    if (previewAttack != null && previewHitPoint != null)
                    {
                        vfxPoint = previewHitPoint.position;
                        followTarget = previewHitPoint;
                    }

                    // vfxTowardCameraOffset should stay at 0 unless you really need it (see notes)
                    SpawnVFX(currentHit.customVFX, vfxPoint, player.transform.forward, vfxHeightOffset, true,
                             vfxFollowsTarget ? followTarget : null);

                    TriggerJuice(currentHit);
                    StopHitDetection();
                    break;
                }
            }
        }
    }

    // Works out which attack is currently being thrown
    private AttackData ResolveCurrentAttack()
    {
        // Skill-preview screen: use the attack being previewed
        if (previewAttack != null) return previewAttack;

        if (player == null) player = GetComponentInParent<PlayerController>();
        if (player == null) return null;

        if (player.CurrentState == player.AOEAttackState)
            return player.specialAttackY;

        if (player.CurrentState == player.PowerPunchState)
            return player.equippedStyle != null ? player.equippedStyle.GetActiveChargeAttack() : null;

        if (player.equippedStyle != null && player.equippedStyle.lightComboSequence.Length > player.CurrentComboIndex)
            return player.equippedStyle.lightComboSequence[player.CurrentComboIndex];

        return null;
    }

    // Attaches the attack's swing VFX (trail / motion lines) to the given limb
    private void StartSwingVFX(Transform limb)
    {
        StopSwingVFX(); // end any previous swing first

        if (limb == null) return;

        AttackData attack = ResolveCurrentAttack();
        if (attack == null) return;

        if (attack.swingVFX == null)
        {
            if (debugVFX) Debug.Log("[CombatHitbox] No swingVFX assigned on attack: " + attack, this);
            return;
        }

        // SwooshStamp prefabs are NOT attached to the limb: they are placed once and play their own timed animation
        if (attack.swingVFX.GetComponentInChildren<SwooshStamp>(true) != null)
        {
            Transform stampOwner = player != null ? player.transform : transform;
            GameObject stampObject = Instantiate(attack.swingVFX, limb.position, Quaternion.identity);
            SwooshStamp stamp = stampObject.GetComponentInChildren<SwooshStamp>(true);
            stamp.Play(stampOwner, limb, IsLeftLimb(limb));
            swingStampStarted = true;

            if (debugVFX) Debug.Log("[CombatHitbox] Swoosh stamp started: " + attack.swingVFX.name + " on " + limb.name, this);
            return;
        }

        activeSwing = Instantiate(attack.swingVFX, limb.position, limb.rotation, limb);
        swingStopTime = Time.unscaledTime + swingMaxDuration;

        // Trail Renderer prefabs get the speed helper automatically (SmoothSwingRibbon prefabs already react to speed)
        bool hasRibbon = activeSwing.GetComponentInChildren<SmoothSwingRibbon>() != null;
        if (!hasRibbon && activeSwing.GetComponentInChildren<TrailRenderer>() != null
                       && activeSwing.GetComponent<SwingTrailMotion>() == null)
        {
            activeSwing.AddComponent<SwingTrailMotion>();
        }

        if (debugVFX) Debug.Log("[CombatHitbox] Swing VFX started: " + attack.swingVFX.name + " on " + limb.name, this);
    }

    private bool IsLeftLimb(Transform limb)
    {
        return limb == leftFist || limb == leftFoot || limb == leftElbow || limb == leftKnee;
    }

    // Stops emitting and lets the trail fade out where it is, then cleans up
    private void StopSwingVFX()
    {
        // A SwooshStamp plays out by itself (time-based), so we only forget about it here
        swingStampStarted = false;

        if (activeSwing == null) return;

        GameObject swing = activeSwing;
        activeSwing = null;

        // Detach from the limb so trails and particles can finish fading in place
        swing.transform.SetParent(null, true);

        float linger = 0.1f;

        foreach (TrailRenderer tr in swing.GetComponentsInChildren<TrailRenderer>())
        {
            tr.emitting = false;
            linger = Mathf.Max(linger, tr.time);
        }

        foreach (SmoothSwingRibbon ribbon in swing.GetComponentsInChildren<SmoothSwingRibbon>())
        {
            ribbon.StopEmitting();
            linger = Mathf.Max(linger, ribbon.lifetime);
        }

        // Fist / kick speed streaks: freeze where they are and break up over time
        foreach (FistStreak streak in swing.GetComponentsInChildren<FistStreak>())
        {
            streak.Release();
            linger = Mathf.Max(linger, streak.ReleaseDuration);
        }

        foreach (ParticleSystem ps in swing.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            ParticleSystem.MainModule m = ps.main;
            linger = Mathf.Max(linger, m.startLifetime.constantMax);
        }

        Destroy(swing, linger + 0.1f);
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

    // Enemy-side feedback: energy ripple on the body, local distortion, alien shards and soul wisps
    private void PlayEnemyHitFeedback(Component enemy, Vector3 point, Vector3 direction, int damage)
    {
        if (!enemyHitFeedback || enemy == null) return;

        HitFeedbackManager manager = HitFeedbackManager.Get();
        float intensity = Mathf.Clamp01(damage / Mathf.Max(1f, heavyHitDamage));
        manager.PlayHit(enemy.gameObject, point, direction, intensity, player != null ? player.transform : transform);
    }

    // Makes the swing ribbon flash bright and fat for a moment when a hit lands (visible during hit stop too)
    private void PulseSwing()
    {
        if (activeSwing == null) return;
        foreach (SmoothSwingRibbon ribbon in activeSwing.GetComponentsInChildren<SmoothSwingRibbon>())
            ribbon.Pulse();

        // speed streaks freeze right at the impact
        foreach (FistStreak streak in activeSwing.GetComponentsInChildren<FistStreak>())
            streak.Pulse();
    }

    private void TriggerJuice(AttackData hitData)
    {
        PulseSwing();

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