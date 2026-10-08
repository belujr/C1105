using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RangedMinionBrain : MinionEnemyBrain
{
    [Header("Ranged Distance Band")]
    public float minSafeDistance = 6.0f;
    public float preferredDistance = 10.0f;
    public float maxDistance = 14.0f;
    public float moveSpeedMultiplier = 1.3f;

    [Header("Ranged Attack & Patterns")]
    public BulletPattern[] bulletPatterns;
    public Transform firePoint;
    public float telegraphTime = 0.5f;
    public float postAttackBreather = 1.2f;

    [Header("Dedicated Attack Animation")]
    [Tooltip("Drag your attack/throw AnimationClip file directly here.")]
    public AnimationClip attackClip;

    [Tooltip("Delay in seconds after animation starts before bullets spawn (adjust to match the release frame).")]
    public float bulletFireDelay = 0.2f;

    private float lastFireTime;
    private Vector3 candidatePosition;
    private bool hasCandidatePosition;
    private float repositionTimer;

    protected float RangedSpeed => MoveSpd * moveSpeedMultiplier;

    protected override void PlayAttackAnimation()
    {
        if (attackClip != null && minionAnim != null)
        {
            animLocked = true;
            currentAnim = AnimKey.Attack;
            lastLocoClip = null;
            minionAnim.PlayAnimation(attackClip, 0.05f, 1f);
        }
        else
        {
            base.PlayAttackAnimation();
        }
    }

    protected override void HandleChase(float dt)
    {
        if (target == null) return;
        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;

        // 1. Kiting fallback: player closed in past safe threshold
        if (dist < minSafeDistance)
        {
            Vector3 retreatDir = AvoidObstacles(-toTarget.normalized);
            desiredVelocity = Vector3.ClampMagnitude(retreatDir * RangedSpeed + GetSeparation(), RangedSpeed);
            desiredFacing = Flat(PerceivedTargetPosition - transform.position);
            return;
        }

        // 2. Repositioning: out of max band or line-of-sight blocked
        if (dist > maxDistance || !seesTarget)
        {
            if (!hasCandidatePosition || Time.time > repositionTimer)
            {
                FindCandidateFiringPosition();
            }

            if (hasCandidatePosition)
            {
                SteerTo(candidatePosition, RangedSpeed, true);
                if (Flat(candidatePosition - transform.position).sqrMagnitude < 0.5f)
                {
                    hasCandidatePosition = false;
                }
            }
            else
            {
                SteerTo(ChaseGoal, RangedSpeed, true);
            }
            return;
        }

        // 3. Optimal distance band & LOS confirmed -> Orbit / Strafe
        TransitionToState(MinionState.Orbit);
    }

    protected override void HandleOrbit(float dt)
    {
        if (target == null) return;
        Vector3 toTarget = Flat(target.position - transform.position);
        float dist = toTarget.magnitude;

        if (dist < minSafeDistance || dist > maxDistance || !seesTarget)
        {
            TransitionToState(MinionState.Chase);
            return;
        }

        TryAcquireRangedToken();

        // Strafe facing player within preferred band
        Vector3 dirTo = toTarget / Mathf.Max(dist, 0.001f);
        Vector3 ringTangent = Vector3.Cross(Vector3.up, -dirTo);

        float radialErr = dist - preferredDistance;
        float radialVel = Mathf.Clamp(radialErr * 1.5f, -RangedSpeed * 0.5f, RangedSpeed * 0.5f);

        Vector3 vel = dirTo * radialVel + ringTangent * (orbitSign * orbitSpeed * 0.8f);
        desiredVelocity = Vector3.ClampMagnitude(AvoidObstacles(vel.normalized) * vel.magnitude + GetSeparation(), RangedSpeed * 0.85f);
        desiredFacing = Flat(PerceivedTargetPosition - transform.position);
    }

    private void TryAcquireRangedToken()
    {
        if (GlobalTokenManager.Instance == null || Time.time < nextTokenRequestTime) return;
        if (Time.time < lastFireTime + reEngageCooldown.x) return;

        nextTokenRequestTime = Time.time + 0.25f;

        if (!holdsEngageToken && IsNextInLine())
        {
            if (GlobalTokenManager.Instance.RequestToken(transform, TokenType.Ranged))
            {
                holdsEngageToken = true;
                TransitionToState(MinionState.Engage);
            }
        }
    }

    protected override IEnumerator EngageRoutine()
    {
        if (bulletPatterns == null || bulletPatterns.Length == 0)
        {
            GiveUpEngage();
            yield break;
        }

        BulletPattern selectedPattern = bulletPatterns[Random.Range(0, bulletPatterns.Length)];

        // Telegraph phase: aim tracks perceived delayed player position
        float t = 0f;
        while (t < telegraphTime)
        {
            if (target == null) { ReturnToPatrol(); yield break; }
            t += Time.deltaTime;
            HoldAndFace();
            yield return null;
        }

        // Lock aiming direction
        Vector3 aimDir = Flat(PerceivedTargetPosition - transform.position).normalized;
        if (aimDir.sqrMagnitude < 0.001f) aimDir = transform.forward;
        desiredFacing = aimDir;

        // Play attack clip via EnemyAnimationEngine
        PlayAttackAnimation();

        // Delay to sync bullet instantiation with the throw frame
        if (bulletFireDelay > 0f)
        {
            float delayTimer = 0f;
            while (delayTimer < bulletFireDelay)
            {
                delayTimer += Time.deltaTime;
                HoldAndFace();
                yield return null;
            }
        }

        // Cast bullet pattern via BulletManager
        Vector3 spawnPoint = firePoint != null ? firePoint.position : transform.position + Vector3.up * 1.2f;
        if (BulletManager.Instance != null)
        {
            BulletManager.Instance.FirePattern(selectedPattern, spawnPoint, aimDir);
        }

        lastFireTime = Time.time;

        // Breather phase
        float breatherTimer = 0f;
        while (breatherTimer < postAttackBreather)
        {
            breatherTimer += Time.deltaTime;
            HoldAndFace();
            yield return null;
        }

        CleanupTokens();
        engageReadyTime = Time.time + Random.Range(reEngageCooldown.x, reEngageCooldown.y);
        waitingSince = engageReadyTime;
        TransitionToState(MinionState.Recover);
    }

    private void FindCandidateFiringPosition()
    {
        if (target == null) return;
        repositionTimer = Time.time + 2.5f;
        hasCandidatePosition = false;

        Vector3 center = target.position;
        float bestScore = -999f;
        Vector3 bestPos = transform.position;

        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * preferredDistance;

            if (Physics.Linecast(probe + Vector3.up * 1.5f, center + Vector3.up * 1.0f, obstacleMask, QueryTriggerInteraction.Ignore))
                continue;

            float score = 10f - Vector3.Distance(transform.position, probe) * 0.2f;
            if (score > bestScore)
            {
                bestScore = score;
                bestPos = probe;
                hasCandidatePosition = true;
            }
        }

        if (hasCandidatePosition) candidatePosition = bestPos;
    }

    protected override void CleanupTokens()
    {
        if (GlobalTokenManager.Instance != null && holdsEngageToken)
        {
            GlobalTokenManager.Instance.ReleaseToken(transform, TokenType.Ranged);
        }
        base.CleanupTokens();
    }
}