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
        arriveRadius = m.wispArriveRadius;
        orbSize = m.wispOrbSize * Random.Range(0.8f, 1.2f);

        age = 0f;
        phase = Phase.Flying;

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

    private void Update()
    {
        if (phase == Phase.Idle) return;

        float dt = Time.deltaTime;

        if (phase == Phase.Fading)
        {
            // wait until the trail has faded away, then go back to the pool
            fadeTimer -= Time.unscaledDeltaTime;
            if (fadeTimer <= 0f) Recycle();
            return;
        }

        if (dt <= 0f) return;
        age += dt;

        Vector3 pos = transform.position;
        Vector3 targetPos = target != null ? target.position + Vector3.up * targetHeight : pos;

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
            if (dist <= arriveRadius || age > maxLife)
            {
                Arrive(pos);
                return;
            }

            Vector3 desired = to / dist * speed;
            velocity = Vector3.Lerp(velocity, desired, 1f - Mathf.Exp(-turnRate * (0.5f + t) * dt));
        }

        transform.position = pos + velocity * dt;
    }

    private void LateUpdate()
    {
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
        orb.localScale = new Vector3(mgr.wispHeadStretch.x, mgr.wispHeadStretch.y, 1f) * (orbSize * pulse * speedSwell);
    }

    private void Arrive(Vector3 position)
    {
        phase = Phase.Fading;
        trail.emitting = false;
        orb.gameObject.SetActive(false);
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing sparks finish on their own
        fadeTimer = Mathf.Max(trail.time, sparks != null ? sparks.main.startLifetime.constantMax : 0f) + 0.05f;

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