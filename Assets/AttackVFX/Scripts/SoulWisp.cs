using UnityEngine;

/// <summary>
/// One soul wisp: it bursts out of the enemy, drifts and swirls for a moment, then accelerates in a curve
/// into the player (like picking up runes in a Souls game) and leaves a glowing trail.
/// Created and pooled by HitFeedbackManager. You never add this yourself.
/// </summary>
public class SoulWisp : MonoBehaviour
{
    private enum Phase { Idle, Flying, Fading }

    private HitFeedbackManager mgr;
    private TrailRenderer trail;
    private Transform orb;
    private ParticleSystem sparks;
    private float headAngle;
    private bool hasAngle;

    private Phase phase = Phase.Idle;
    private Vector3 velocity;
    private Transform target;
    private float targetHeight;

    private float age;
    private float homingStart;
    private float maxLife;
    private float drag;
    private float swirlAmount;
    private float swirlFreq;
    private float swirlPhase;
    private float startSpeed;
    private float maxSpeed;
    private float rampTime;
    private float turnRate;
    private float arriveRadius;
    private float orbSize;
    private float fadeTimer;
    private float distToTarget = 99f;
    private float retractTimer;
    private float trailBaseTime;
    private const float RetractTime = 0.18f;

    public void Init(HitFeedbackManager manager, TrailRenderer trailRenderer, Transform orbTransform, ParticleSystem sparkSystem)
    {
        mgr = manager;
        trail = trailRenderer;
        orb = orbTransform;
        sparks = sparkSystem;
        gameObject.SetActive(false);
    }

    public void Launch(Vector3 origin, Vector3 initialVelocity, Transform targetTransform, float height,
                       float burstTime, float homingDelay)
    {
        HitFeedbackManager m = mgr;

        target = targetTransform;
        targetHeight = height;
        velocity = initialVelocity;

        homingStart = burstTime + homingDelay;
        maxLife = homingStart + 4f;
        drag = m.wispDrag;
        swirlAmount = m.wispSwirl * Random.Range(0.6f, 1.4f);
        swirlFreq = Random.Range(7f, 13f);
        swirlPhase = Random.value * 6.28f;
        startSpeed = m.wispHomeStartSpeed;
        maxSpeed = m.wispHomeMaxSpeed * Random.Range(0.9f, 1.15f);
        rampTime = m.wispHomeRampTime;
        turnRate = m.wispTurnRate;
        arriveRadius = m.wispEnterDistance;
        distToTarget = 99f;
        orbSize = m.wispOrbSize * Random.Range(0.8f, 1.2f);

        age = 0f;
        phase = Phase.Flying;

        trailBaseTime = m.wispTrailTime;
        trail.time = trailBaseTime;
        trail.widthMultiplier = m.wispTrailWidth;

        transform.position = origin;
        gameObject.SetActive(true);
        orb.gameObject.SetActive(true);
        trail.Clear();
        trail.emitting = true;

        hasAngle = false;
        if (sparks != null)
        {
            sparks.Clear();
            sparks.Play();
        }
    }

    // Runs from LateUpdate, AFTER the animation of this frame, so the aim point inside the player is current
    private void Move()
    {
        if (phase == Phase.Idle) return;

        // a long frame must not make the wisp skip through the last stretch before the body
        float dt = Mathf.Min(Time.deltaTime, 0.033f);

        if (phase == Phase.Fading)
        {
            float udt = Time.unscaledDeltaTime;

            if (retractTimer > 0f)
            {
                // the trail is sucked into the player: the head stays glued inside the moving body
                // while the tail quickly retracts toward it
                retractTimer -= udt;
                float k = 1f - Mathf.Clamp01(retractTimer / RetractTime);
                if (target != null) transform.position = mgr.GetAimPoint(target, targetHeight);
                trail.time = Mathf.Lerp(trailBaseTime, 0.08f, k);

                if (retractTimer <= 0f)
                {
                    trail.emitting = false;
                    float sparkLife = sparks != null ? sparks.main.startLifetime.constantMax : 0f;
                    fadeTimer = Mathf.Max(0.12f, sparkLife) + 0.05f;
                }
                return;
            }

            // wait until the last sparks and trail pieces have faded away, then go back to the pool
            fadeTimer -= udt;
            if (fadeTimer <= 0f) Recycle();
            return;
        }

        if (dt <= 0f) return;
        age += dt;

        Vector3 pos = transform.position;
        Vector3 targetPos = target != null ? mgr.GetAimPoint(target, targetHeight) : pos;

        if (age < homingStart)
        {
            // burst phase: slows down, swirls sideways, floats a little
            velocity *= Mathf.Exp(-drag * dt);

            Vector3 dir = velocity.sqrMagnitude > 0.001f ? velocity.normalized : Vector3.up;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;
            velocity += side.normalized * (Mathf.Sin(age * swirlFreq + swirlPhase) * swirlAmount * dt);
        }
        else
        {
            // homing phase: speeds up and steers toward the player, which makes a curved path
            float t = Mathf.Clamp01((age - homingStart) / Mathf.Max(rampTime, 0.05f));
            float speed = Mathf.Lerp(startSpeed, maxSpeed, t * t);

            Vector3 to = targetPos - pos;
            float dist = to.magnitude;
            distToTarget = dist;

            Vector3 desired = to / Mathf.Max(dist, 0.0001f) * speed;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-turnRate * (0.5f + t) * dt));

            // final approach: steer straight into the body so the curve ends INSIDE the player
            if (dist < 1.5f)
                velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-25f * dt));

            // never overshoot or stop short in the air: if this step reaches the target, finish exactly on it
            Vector3 step = velocity * dt;
            if (dist <= step.magnitude + arriveRadius || age > maxLife)
            {
                transform.position = targetPos;
                Arrive(targetPos);
                return;
            }
        }

        transform.position = pos + velocity * dt;
    }

    private void LateUpdate()
    {
        Move();
        if (phase != Phase.Flying) return;

        Camera cam = HitFeedbackManager.PickCamera(transform.position);
        if (cam != null)
        {
            Quaternion face = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

            // comet / flame / diamond heads point along the direction of flight, as seen on screen
            if (mgr.wispAlignHeadToMotion)
            {
                float sx = Vector3.Dot(velocity, cam.transform.right);
                float sy = Vector3.Dot(velocity, cam.transform.up);
                if (sx * sx + sy * sy > 0.09f)
                {
                    float targetAngle = Mathf.Atan2(sy, sx) * Mathf.Rad2Deg;
                    headAngle = hasAngle
                        ? Mathf.LerpAngle(headAngle, targetAngle, 1f - Mathf.Exp(-20f * Time.deltaTime))
                        : targetAngle;
                    hasAngle = true;
                }
                face *= Quaternion.Euler(0f, 0f, headAngle);
            }
            orb.rotation = face;
        }

        // the head pulses a little, and swells while it travels fast
        float pulse = 1f + 0.18f * Mathf.Sin(age * 24f + swirlPhase);
        float speedSwell = 1f + Mathf.Clamp01(velocity.magnitude / 14f) * 0.35f;
        // the head shrinks as it sinks into the body
        float sink = age >= homingStart ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(distToTarget / 0.6f)) : 1f;
        trail.widthMultiplier = mgr.wispTrailWidth * Mathf.Lerp(0.4f, 1f, sink);
        orb.localScale = new Vector3(mgr.wispHeadStretch.x, mgr.wispHeadStretch.y, 1f) * (orbSize * pulse * speedSwell * sink);
    }

    private void Arrive(Vector3 position)
    {
        phase = Phase.Fading;
        orb.gameObject.SetActive(false);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing sparks finish on their own
        retractTimer = RetractTime;   // the trail keeps emitting for a moment while it is pulled into the player
        fadeTimer = 0f;

        mgr.OnWispArrived(position, target);
    }

    private void Recycle()
    {
        phase = Phase.Idle;
        if (sparks != null) sparks.Clear();
        gameObject.SetActive(false);
        mgr.ReturnWisp(this);
    }
}