using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lives on the Healer (added automatically by HealerController, you don't have to add it).
/// While he heals:
///   - a pulsing orb and converging sparks gather at the staff tip
///   - a slow, gentle stream of glowing orbs, "+" signs, sparkles and a thin light thread drifts in an arc to the ally
///   - a small rune spins on the floor under the healer (and clears the grass under it)
/// HealerController calls Cast(point) every frame while healing and StopCasting() when he stops.
/// Everything is built in code on first use, no materials or prefabs needed.
/// </summary>
public class HealerHealVFX : MonoBehaviour
{
    [Header("Where the energy leaves the healer")]
    [Tooltip("Set automatically by HealerController.")]
    public Transform staff;
    [Tooltip("Optional. Drag an empty child placed at the staff tip. If empty, the staff position + offset below is used.")]
    public Transform castPoint;
    [Tooltip("Offset from the staff transform (in its local space) to the glowing tip.")]
    public Vector3 tipLocalOffset = Vector3.zero;
    [Tooltip("Used only if there is no staff and no cast point (local to the healer).")]
    public Vector3 fallbackOffset = new Vector3(0f, 1.4f, 0.5f);

    [Header("Look")]
    public Color mainColor = new Color(0.30f, 1f, 0.50f, 1f);
    public Color accentColor = new Color(0.85f, 1f, 0.90f, 1f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.5f;

    [Header("Energy stream (rides on a soft rope that bends smoothly when the ally moves)")]
    [Tooltip("Seconds a particle needs to flow from the staff to the ally. Higher = slower.")]
    public float travelTime = 1.1f;
    [Tooltip("How high the rope arcs above the straight line (world units).")]
    public float arcHeight = 0.7f;
    [Tooltip("How wide the stream is. Everything scales with this.")]
    public float streamWidth = 0.35f;
    [Tooltip("How many times particles circle the center line on the way. Low = calm.")]
    public float twists = 0.5f;
    [Tooltip("How fast particles circle and the stream sways.")]
    public float swaySpeed = 1.2f;
    [Header("Rope (how it follows a moving ally)")]
    [Tooltip("Seconds the rope takes to follow near its ends. Lower = tighter.")]
    public float ropeLagEnds = 0.08f;
    [Tooltip("Extra seconds of lag in the middle of the rope. Higher = a looser, more bendy rope.")]
    public float ropeLagMiddle = 0.2f;
    public float threadPerSecond = 14f;
    public float orbsPerSecond = 9f;
    public float plusPerSecond = 5f;
    public float sparklesPerSecond = 24f;

    [Header("Staff glow")]
    public float staffOrbSize = 0.6f;
    public float chargeRadius = 0.6f;

    [Header("Rune on the floor under the healer")]
    public bool showFootRune = true;
    public float footRuneRadius = 1.1f;
    [Tooltip("Set this to your ground layer so the rune sits on the floor and not on other enemies.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Small lift above the floor so the rune does not flicker against the ground.")]
    public float groundOffset = 0.05f;
    [Tooltip("Clears grass and flowers under the rune (needs the HealAuraGrassMask shader).")]
    public bool clearGrassUnderRune = true;
    [Tooltip("Size of the cleared grass area compared to the rune (1 = same size, 1.3 = a bit bigger).")]
    public float grassClearScale = 1.3f;

    private class Stream
    {
        public ParticleSystem ps;
        public int kind;            // 0 = light thread, 1 = orbs, 2 = plus signs, 3 = sparkles
        public Vector2 size;
        public float accentMix;
        public float amp;
        public float twist;
        public float accum;
    }

    private const int RopePoints = 8;
    private readonly Vector3[] rope = new Vector3[RopePoints];
    private readonly Vector3[] ropeVel = new Vector3[RopePoints];
    private bool ropeLive;

    private static readonly ParticleSystem.Particle[] buf = new ParticleSystem.Particle[512];

    private readonly List<Material> mats = new List<Material>();
    private readonly List<Stream> streams = new List<Stream>();
    private Transform anchor;
    private ParticleSystem orbPS, chargePS;
    private HealGroundRune foot;
    private bool built;
    private bool casting;
    private float lastCastTime = -10f;
    private Vector3 targetWorld;
    private HealReceiveFX followFX;

    // ------------------------------------------------------------------ API
    /// <summary>Call every frame while healing. 'targetPoint' is where the energy lands (the ally's chest).</summary>
    public void Cast(Vector3 targetPoint)
    {
        EnsureBuilt();
        targetWorld = targetPoint;
        lastCastTime = Time.time;
        if (!casting)
        {
            casting = true;
            chargePS.Emit(14);   // small pop when the cast starts
        }
    }

    /// <summary>
    /// Call every frame while healing. The stream follows this ally live (even while particles are still flying
    /// after the cast stopped), and the ally's aura only starts when the first particles arrive.
    /// </summary>
    public void Cast(HealReceiveFX target)
    {
        if (target == null) return;
        Cast(target.AimPoint);
        followFX = target;
    }

    public void StopCasting()
    {
        casting = false;
    }

    private void OnDisable()
    {
        casting = false;
        ropeLive = false;
        if (foot != null) foot.Reset();
    }

    private void OnDestroy()
    {
        for (int i = 0; i < mats.Count; i++) if (mats[i] != null) Destroy(mats[i]);
        if (foot != null) foot.Dispose();
    }

    // ------------------------------------------------------------------ build
    private Material Mat(Texture2D tex)
    {
        Material m = HealVFXAssets.CreateMaterial(tex, brightness);
        mats.Add(m);
        return m;
    }

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        // must happen before we create any child mesh (see HealVFXAssets.EnsureHitFX)
        HealVFXAssets.EnsureHitFX(gameObject);

        anchor = new GameObject("HealCastAnchor").transform;
        anchor.SetParent(transform, false);

        Material dotMat = Mat(HealVFXAssets.Dot);
        Material plusMat = Mat(HealVFXAssets.Plus);
        Material ribbonMat = Mat(HealVFXAssets.Ribbon);

        // staff orb: soft pulsing glow at the tip
        orbPS = HealVFXAssets.MakeSystem(anchor, "StaffOrb", dotMat, 40, false);
        ParticleSystem.MainModule om = orbPS.main;
        om.startLifetime = 0.7f;
        om.startSize = new ParticleSystem.MinMaxCurve(staffOrbSize * 0.8f, staffOrbSize * 1.1f);
        om.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
        HealVFXAssets.FadeInOut(orbPS, 0.4f, 0.5f, 0.9f);
        HealVFXAssets.SizeCurve(orbPS, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.7f)));

        // converging sparks: spawn on a sphere, fly inward (negative speed) into the tip
        chargePS = HealVFXAssets.MakeSystem(anchor, "StaffCharge", dotMat, 80, false);
        ParticleSystem.MainModule cm = chargePS.main;
        cm.startSpeed = -1.5f;
        cm.startLifetime = Mathf.Max(0.1f, chargeRadius / 1.5f);
        cm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        cm.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
        ParticleSystem.ShapeModule cs = chargePS.shape;
        cs.enabled = true;
        cs.shapeType = ParticleSystemShapeType.Sphere;
        cs.radius = chargeRadius;
        cs.radiusThickness = 0f;
        HealVFXAssets.FadeInOut(chargePS, 0.15f, 0.6f, 1f);
        HealVFXAssets.SizeCurve(chargePS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

        // the stream: a thin thread of light, soft orbs, "+" signs and sparkles
        streams.Add(MakeStream("HealThread", 0, 128, dotMat, ribbonMat, new Vector2(0.09f, 0.13f), 0.5f, 0.12f, 0.4f));
        streams.Add(MakeStream("HealOrbs", 1, 64, dotMat, null, new Vector2(0.22f, 0.34f), 0.6f, 0.22f, 0.5f));
        streams.Add(MakeStream("HealPlus", 2, 48, plusMat, null, new Vector2(0.22f, 0.32f), 0.7f, 0.25f, 0.6f));
        streams.Add(MakeStream("HealSparkles", 3, 160, dotMat, null, new Vector2(0.04f, 0.08f), 0.8f, 0.5f, 1.5f));

        if (showFootRune)
        {
            foot = new HealGroundRune(transform, "HealFootRune", HealVFXAssets.Rune, brightness * 0.8f);
            foot.clearGrass = clearGrassUnderRune;
            HealVFXAssets.ProtectFromHitFX(gameObject, foot.Renderer);
            HealVFXAssets.ProtectFromHitFX(gameObject, foot.MaskRenderer);
        }
    }

    private Stream MakeStream(string name, int kind, int max, Material mat, Material trailMat,
                              Vector2 size, float accentMix, float amp, float twist)
    {
        Stream s = new Stream();
        s.kind = kind;
        s.size = size;
        s.accentMix = accentMix;
        s.amp = amp;
        s.twist = twist;
        s.ps = HealVFXAssets.MakeSystem(transform, name, mat, max, true);

        // alpha in/out + the particles get small right before they land
        HealVFXAssets.FadeInOut(s.ps, 0.15f, 0.8f, 1f);
        HealVFXAssets.SizeCurve(s.ps, new AnimationCurve(
            new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.35f)));

        if (trailMat != null)
        {
            ParticleSystem.TrailModule tr = s.ps.trails;
            tr.enabled = true;
            tr.mode = ParticleSystemTrailMode.PerParticle;
            tr.ratio = 1f;
            tr.lifetime = 0.3f;
            tr.minVertexDistance = 0.05f;
            tr.worldSpace = true;
            tr.dieWithParticles = true;
            tr.sizeAffectsWidth = true;
            tr.sizeAffectsLifetime = false;
            tr.inheritParticleColor = true;
            tr.textureMode = ParticleSystemTrailTextureMode.Stretch;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            tr.colorOverLifetime = new ParticleSystem.MinMaxGradient(g);
            s.ps.GetComponent<ParticleSystemRenderer>().trailMaterial = trailMat;
        }
        return s;
    }

    // ------------------------------------------------------------------ update
    private Vector3 TipPosition()
    {
        if (castPoint != null) return castPoint.position;
        if (staff != null) return staff.TransformPoint(tipLocalOffset);
        return transform.TransformPoint(fallbackOffset);
    }

    private float RateOf(Stream s)
    {
        if (s.kind == 0) return threadPerSecond;
        if (s.kind == 1) return orbsPerSecond;
        if (s.kind == 2) return plusPerSecond;
        return sparklesPerSecond;
    }

    private void LateUpdate()
    {
        if (!built) return;
        float dt = Time.deltaTime;

        if (casting && Time.time - lastCastTime > 0.3f) casting = false;

        // keep aiming at the live ally, so particles already in flight still end up on it
        if (followFX != null)
        {
            if (followFX.isActiveAndEnabled) targetWorld = followFX.AimPoint;
            else followFX = null;
        }

        Vector3 tip = TipPosition();
        anchor.position = tip;
        HealVFXAssets.SetRate(orbPS, casting ? 7f : 0f);
        HealVFXAssets.SetRate(chargePS, casting ? 30f : 0f);

        if (foot != null && (casting || foot.IsVisible))
        {
            foot.clearGrass = clearGrassUnderRune;
            foot.clearScale = grassClearScale;
            foot.Tick(dt, casting && showFootRune, HealVFXAssets.GroundPoint(transform, groundMask, groundOffset),
                      footRuneRadius, mainColor, 40f, 0.25f, 0.4f);
        }

        // the rope: end points stick to the staff and the ally, the middle follows with a soft lag
        bool anyParticles = false;
        for (int i = 0; i < streams.Count; i++) if (streams[i].ps.particleCount > 0) { anyParticles = true; break; }

        if (!anyParticles && !casting) ropeLive = false;
        if (anyParticles || casting) UpdateRope(tip, targetWorld, dt);

        bool arrived = false;
        for (int i = 0; i < streams.Count; i++)
        {
            Stream s = streams[i];
            if (casting) EmitStream(s, tip, dt);
            if (s.ps.particleCount > 0 && Reposition(s, Time.time)) arrived = true;
        }

        // the aura on the ally only shows while stream particles are actually reaching it
        if (arrived && followFX != null) followFX.Ping();
    }

    // ------------------------------------------------------------------ rope
    private Vector3 RopeRest(Vector3 a, Vector3 b, float u, float arc)
    {
        return Vector3.Lerp(a, b, u) + Vector3.up * (arc * Mathf.Sin(Mathf.PI * u));
    }

    private void UpdateRope(Vector3 a, Vector3 b, float dt)
    {
        float arc = arcHeight * Mathf.Min(1f, Vector3.Distance(a, b) / 6f);

        if (!ropeLive)
        {
            // a fresh stream starts exactly on its rest shape
            for (int i = 0; i < RopePoints; i++)
            {
                rope[i] = RopeRest(a, b, i / (float)(RopePoints - 1), arc);
                ropeVel[i] = Vector3.zero;
            }
            ropeLive = true;
            return;
        }

        rope[0] = a;
        for (int i = 1; i < RopePoints; i++)
        {
            float u = i / (float)(RopePoints - 1);
            Vector3 rest = RopeRest(a, b, u, arc);
            float lag = Mathf.Max(0.01f, ropeLagEnds + ropeLagMiddle * Mathf.Sin(Mathf.PI * u));
            if (dt > 0f) rope[i] = Vector3.SmoothDamp(rope[i], rest, ref ropeVel[i], lag, Mathf.Infinity, dt);
        }
    }

    // Catmull-Rom curve through the rope points. u = 0 (staff) .. 1 (ally)
    private void EvalRope(float u, out Vector3 pos, out Vector3 tangent)
    {
        float f = Mathf.Clamp01(u) * (RopePoints - 1);
        int i = Mathf.Min((int)f, RopePoints - 2);
        float t = f - i;

        Vector3 p0 = rope[Mathf.Max(i - 1, 0)];
        Vector3 p1 = rope[i];
        Vector3 p2 = rope[i + 1];
        Vector3 p3 = rope[Mathf.Min(i + 2, RopePoints - 1)];

        float t2 = t * t, t3 = t2 * t;
        pos = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        tangent = 0.5f * ((-p0 + p2) + (4f * p0 - 10f * p1 + 8f * p2 - 2f * p3) * t + (-3f * p0 + 9f * p1 - 9f * p2 + 3f * p3) * t2);
    }

    private void EmitStream(Stream s, Vector3 origin, float dt)
    {
        s.accum += RateOf(s) * dt;
        int n = (int)s.accum;
        s.accum -= n;

        for (; n > 0; n--)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = origin;
            ep.velocity = Vector3.zero;
            ep.startLifetime = travelTime * Random.Range(0.9f, 1.1f);
            ep.startSize = Random.Range(s.size.x, s.size.y);
            ep.startColor = Color.Lerp(mainColor, accentColor, Random.value * s.accentMix);
            ep.randomSeed = (uint)Random.Range(0, 100000);
            s.ps.Emit(ep, 1);
        }
    }

    // Particles flow along the rope curve. The rope bends smoothly, so the stream does too.
    // Returns true when particles are reaching the ally.
    private bool Reposition(Stream s, float time)
    {
        int n = s.ps.GetParticles(buf);
        bool arrived = false;
        float w = streamWidth / 0.35f;   // 0.35 = the width the amounts were designed for

        for (int i = 0; i < n; i++)
        {
            ParticleSystem.Particle p = buf[i];
            float t = Mathf.Clamp01(1f - p.remainingLifetime / Mathf.Max(0.0001f, p.startLifetime));
            // gentle ease: a little slower at the staff and when arriving, never stops
            float te = Mathf.Lerp(t, t * t * (3f - 2f * t), 0.35f);
            if (te > 0.85f) arrived = true;

            float r01 = (p.randomSeed % 1000u) / 1000f;
            float pinch = Mathf.Pow(Mathf.Sin(Mathf.PI * te), 0.7f);   // 0 at both ends: starts and ends in a point

            Vector3 pos, tan;
            EvalRope(te, out pos, out tan);
            if (tan.sqrMagnitude < 0.0001f) tan = transform.forward;
            tan.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tan);
            if (right.sqrMagnitude < 0.0001f) right = transform.right;
            right.Normalize();
            Vector3 up2 = Vector3.Cross(tan, right);

            // each particle circles the rope a little (own radius + phase); the whole stream sways softly
            float radius = s.amp * w * pinch * (0.4f + 0.6f * r01);
            float angle = r01 * Mathf.PI * 2f + te * s.twist * Mathf.PI * 2f + time * swaySpeed;
            float sway = Mathf.Sin(te * Mathf.PI * 2f - time * swaySpeed * 1.2f) * 0.2f * w * pinch;
            float bob = Mathf.Sin(time * 2.5f + r01 * 20f) * 0.04f * pinch;

            pos += right * (Mathf.Cos(angle) * radius + sway) + up2 * (Mathf.Sin(angle) * radius) + Vector3.up * bob;

            p.position = pos;
            p.velocity = Vector3.zero;
            if (s.kind == 2) p.rotation = r01 * 360f + t * 140f;   // "+" signs slowly turn
            buf[i] = p;
        }
        s.ps.SetParticles(buf, n);
        return arrived;
    }
}