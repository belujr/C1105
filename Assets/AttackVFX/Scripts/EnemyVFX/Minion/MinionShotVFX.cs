using UnityEngine;

/// <summary>
/// The attack of the Ranged Minion (added automatically by RangedMinionBrain, you don't have to add it).
///   Telegraph  while it aims: a magic circle spins up at the muzzle, an orb swells, sparks are sucked in, and a DANGER ZONE
///              shows on the floor: a fan for a fan pattern, a full circle for a ring, a lane for a single shot.
///              A bright front moves outward while it charges and the zone flashes when the aim LOCKS.
///              The ring under the floating minion glows stronger while it charges.
///   Fire       a bright muzzle flash, a ring and shards of light along every bullet direction, and a surge in the ring below.
///   Cancel     the attack was interrupted: it fizzles out.
/// Everything is built in code on first use.
/// </summary>
public class MinionShotVFX : MonoBehaviour
{
    public enum OriginMode { FirePoint, BodyCenter }

    [Header("Look")]
    public Color mainColor = new Color(0.55f, 0.5f, 1f, 1f);
    public Color coreColor = new Color(0.96f, 0.96f, 1f, 1f);
    public Color accentColor = new Color(0.5f, 0.82f, 1f, 1f);
    [Tooltip("HDR brightness multiplier. With Bloom on, higher = bigger glow.")]
    public float brightness = 2.5f;

    [Header("Where the shot starts (the effects AND the bullets use this point)")]
    [Tooltip("FirePoint = the Fire Point of the minion. BodyCenter = the middle of the minion's body. If the shot starts a bit to the side of the enemy, the Fire Point is not in the middle: use BodyCenter, or move it with 'Muzzle Offset'.")]
    public OriginMode originMode = OriginMode.FirePoint;
    [Tooltip("Height of the body center above the minion's position (BodyCenter only).")]
    public float bodyHeight = 1.2f;
    [Tooltip("Moves the start of the shot, relative to the minion (it turns with the minion): x = right, y = up, z = forward.")]
    public Vector3 muzzleOffset = Vector3.zero;

    [Header("Danger zone (shows where the bullets will fly)")]
    public bool showZone = true;
    [Tooltip("The zone reaches the range of the bullets (speed x lifetime), up to this length.")]
    public float zoneMaxRange = 14f;
    [Range(0f, 1f)] public float zoneFill = 0.35f;
    [Tooltip("Lifts the zone above the floor.")]
    public float zoneHeight = 0.08f;
    [Tooltip("Optional. Drag the VFX_DangerZone shader here so it is always included in builds. Empty = it is found by name.")]
    public Shader zoneShader;
    [Tooltip("Set this to your ground layer.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Small stars rise from the danger zone while the minion charges, and burst when the aim locks.")]
    public bool zoneDust = true;
    public float zoneDustRate = 45f;

    [Header("Telegraph (while it aims)")]
    public float orbSize = 0.7f;
    [Tooltip("Sparks start this far away from the orb and fly into it.")]
    public float convergeRadius = 0.9f;
    public float convergeRate = 60f;
    [Tooltip("A magic circle that spins at the muzzle while it aims.")]
    public bool showCastCircle = true;
    public float castCircleSize = 1.4f;

    [Header("Muzzle flash (when it fires)")]
    public int muzzleSparks = 18;
    [Tooltip("Half angle of the cone of extra streaks (degrees).")]
    public float muzzleCone = 35f;
    [Tooltip("Size of the ring at the muzzle.")]
    public float muzzleRingSize = 0.9f;
    [Tooltip("Size of the flash at the muzzle.")]
    public float muzzleFlashSize = 1f;

    private const int MaxDirs = 24;   // a pattern with more bullets uses a sample of its directions for the muzzle streaks

    private Transform anchor, point;
    private Vector3 fallbackOffset;
    private ParticleSystem orbPS, convergePS, castPS, flashPS, burstPS, ringPS;
    private bool built, telegraphing, locked;
    private float start, dur, zoneFlashUntil, lockBlend;
    private Vector3 aimDir = Vector3.forward;
    private BulletPattern pattern;
    private MinionHoverVFX hover;
    private readonly Vector3[] dirBuf = new Vector3[MaxDirs];
    private readonly int[] waveBuf = new int[MaxDirs];

    // the danger zone
    private Transform zoneTf;
    private MeshRenderer zoneMr;
    private MeshFilter zoneMf;
    private Material zoneMat;
    private Mesh zoneMesh;
    private BulletPattern zoneBuiltFor;
    private bool zoneBuilt;
    private bool dirty;
    private ParticleSystem dustPS;
    private float lockStart;

    // ------------------------------------------------------------------ API (called by RangedMinionBrain)
    /// <param name="muzzle">The fire point (or the minion itself if there is none).</param>
    /// <param name="offsetIfNoPoint">Used when 'muzzle' is the minion itself: offset from it.</param>
    public void BeginTelegraph(Transform muzzle, Vector3 offsetIfNoPoint, float duration)
    {
        EnsureBuilt();
        point = muzzle;
        fallbackOffset = offsetIfNoPoint;
        telegraphing = true;
        locked = false;
        lockBlend = 0f;
        start = Time.time;
        dur = Mathf.Max(0.1f, duration);
        anchor.position = Origin();
        aimDir = transform.forward;
        if (hover == null) hover = GetComponent<MinionHoverVFX>();

        // the magic circle at the muzzle lives exactly as long as the charge (local space: it follows the muzzle)
        if (showCastCircle)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = Vector3.zero;
            ep.velocity = Vector3.zero;
            ep.startSize = castCircleSize;
            ep.startLifetime = dur + 0.05f;
            ep.startColor = mainColor;
            castPS.Emit(ep, 1);
        }
    }

    /// <summary>The pattern that is about to be fired: the danger zone and the muzzle streaks follow its real shape.</summary>
    public void SetPattern(BulletPattern p)
    {
        pattern = p;
    }

    /// <summary>Call every frame while aiming (the zone follows).</summary>
    public void SetAim(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f) aimDir = direction.normalized;
    }

    /// <summary>The aim is fixed: the zone flashes.</summary>
    public void LockAim(Vector3 direction)
    {
        SetAim(direction);
        locked = true;
        lockStart = Time.time;
        if (dustPS != null && zoneMr != null && zoneMr.enabled) dustPS.Emit(30);   // a burst of stars over the zone
    }

    /// <summary>The shot leaves: muzzle flash, ring, shards along every bullet direction.</summary>
    public void Fire(Vector3 spawnPoint, Vector3 direction)
    {
        EnsureBuilt();
        SetAim(direction);
        telegraphing = false;
        zoneFlashUntil = Time.time + 0.14f;
        StopCharge();
        if (hover != null) hover.Surge();

        // The bullets may start lower than the minion (they start at the player's height). A bright streak falls from the
        // minion's muzzle to that point, so the shot looks like it is thrown down into the lane.
        Vector3 from = Origin();
        float drop = Vector3.Distance(from, spawnPoint);
        if (drop > 0.5f)
        {
            Vector3 dn = (spawnPoint - from) / drop;
            for (int i = 0; i < 3; i++)
                EmitOne(burstPS, from + dn * HalfLength(0.12f, 45f), dn * 45f, drop / 45f, 0.12f, i == 0 ? coreColor : mainColor);
        }

        EmitOne(flashPS, spawnPoint, Vector3.zero, 0.15f, muzzleFlashSize, coreColor);
        EmitOne(ringPS, spawnPoint, Vector3.zero, 0.25f, muzzleRingSize, mainColor);

        // Shards along every bullet direction of the pattern. A stretched particle is centered on its position and reaches
        // as far backward as forward, so each one is started half its length AHEAD of the muzzle: nothing sticks out behind it.
        int dirs = ComputeDirections(aimDir);
        for (int i = 0; i < dirs; i++)
        {
            if (waveBuf[i] != 0) continue;
            float speed = Random.Range(8f, 14f);
            float size = Random.Range(0.05f, 0.09f);
            EmitOne(burstPS, spawnPoint + dirBuf[i] * HalfLength(size, speed), dirBuf[i] * speed, Random.Range(0.2f, 0.35f),
                    size, Random.value < 0.5f ? coreColor : mainColor);
        }

        for (int i = 0; i < muzzleSparks; i++)
        {
            Vector3 dir = Vector3.Slerp(aimDir, Random.onUnitSphere, Mathf.Clamp01(muzzleCone / 180f) * Random.value).normalized;
            float r = Random.value;
            float speed = Random.Range(4f, 10f);
            float size = Random.Range(0.04f, 0.09f);
            EmitOne(burstPS, spawnPoint + dir * HalfLength(size, speed), dir * speed, Random.Range(0.2f, 0.45f),
                    size, r < 0.45f ? mainColor : (r < 0.8f ? coreColor : accentColor));
        }
    }

    // how far a stretched shard reaches to each side of its position
    private const float BurstLengthScale = 6f;
    private const float BurstVelocityScale = 0.05f;
    private static float HalfLength(float size, float speed)
    {
        return 0.5f * (size * BurstLengthScale + speed * BurstVelocityScale);
    }

    /// <summary>The attack was interrupted: it fizzles out.</summary>
    public void Cancel()
    {
        if (!built) return;
        bool was = telegraphing;
        telegraphing = false;
        StopCharge();
        zoneFlashUntil = 0f;
        if (zoneMr != null) zoneMr.enabled = false;
        if (hover != null) hover.SetCharge(0f);
        if (was)
            for (int i = 0; i < 8; i++)
                EmitOne(burstPS, Origin(), Random.onUnitSphere * Random.Range(1f, 3f), Random.Range(0.2f, 0.4f),
                        Random.Range(0.03f, 0.07f), mainColor);
    }

    private void OnDisable()
    {
        // pooled / disabled: clean up without playing anything
        telegraphing = false;
        if (!built) return;
        StopCharge();
        if (zoneMr != null) zoneMr.enabled = false;
    }

    private void OnDestroy()
    {
        if (zoneMat != null) Destroy(zoneMat);
        if (zoneMesh != null) Destroy(zoneMesh);
    }

    // editing a value in Play mode rebuilds the effect, so every setting works live
    private void OnValidate()
    {
        if (Application.isPlaying && built) dirty = true;
    }

    private void Rebuild()
    {
        telegraphing = false;
        if (anchor != null) Destroy(anchor.gameObject);
        if (zoneTf != null) Destroy(zoneTf.gameObject);
        if (zoneMat != null) Destroy(zoneMat);
        if (zoneMesh != null) Destroy(zoneMesh);
        zoneBuilt = false;
        zoneBuiltFor = null;
        zoneMesh = null; zoneMat = null; zoneMr = null; zoneMf = null; zoneTf = null; dustPS = null;
        built = false;
        EnsureBuilt();
    }

    // ------------------------------------------------------------------ helpers
    private Vector3 Origin()
    {
        return MuzzlePosition();
    }

    /// <summary>Where the shot starts. The bullets are spawned here too, so the effects and the bullets always line up.</summary>
    public Vector3 MuzzlePosition()
    {
        Vector3 basePos;
        if (originMode == OriginMode.FirePoint && point != null && point != transform) basePos = point.position;
        else if (originMode == OriginMode.FirePoint) basePos = transform.position + fallbackOffset;
        else basePos = transform.position + Vector3.up * bodyHeight;
        return basePos + transform.TransformVector(muzzleOffset);
    }

    private void StopCharge()
    {
        HealVFXAssets.SetRate(orbPS, 0f);
        HealVFXAssets.SetRate(convergePS, 0f);
        orbPS.Clear();
        convergePS.Clear();
        castPS.Clear();
    }

    private static float S(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    private void EmitOne(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size, Color color)
    {
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        ep.position = pos;
        ep.velocity = vel;
        ep.startLifetime = life;
        ep.startSize = size;
        ep.startColor = color;
        ps.Emit(ep, 1);
    }

    // The directions of the bullets, worked out exactly like BulletManager does it (fan, ring, waves).
    // Without a pattern it is one direction: straight ahead.
    private int ComputeDirections(Vector3 fwd)
    {
        if (pattern == null)
        {
            dirBuf[0] = fwd;
            waveBuf[0] = 0;
            return 1;
        }

        int count = Mathf.Max(1, pattern.count);
        int waves = Mathf.Max(1, pattern.waves);
        float start0 = count > 1 ? -pattern.arc * 0.5f + pattern.angleOffset : pattern.angleOffset;
        float step = count > 1 ? pattern.arc / (count - 1) : 0f;
        if (pattern.mode == FireMode.Ring && count > 1)
        {
            start0 = pattern.angleOffset;
            step = 360f / count;
        }

        int total = count * waves;
        int stride = Mathf.Max(1, Mathf.CeilToInt(total / (float)MaxDirs));
        int n = 0, idx = 0;
        for (int w = 0; w < waves; w++)
        {
            for (int i = 0; i < count; i++, idx++)
            {
                if (idx % stride != 0) continue;
                if (n >= MaxDirs) return n;
                dirBuf[n] = Quaternion.Euler(0f, start0 + step * i + w * pattern.perWaveRotationDelta, 0f) * fwd;
                waveBuf[n] = w;
                n++;
            }
        }
        return n;
    }

    // ------------------------------------------------------------------ the danger zone mesh
    // Built in meters, pointing along +Z, and turned to the aim direction each frame.
    //   uv0 = (distance 0..1, angle 0..1)   uv1 = (meters to the nearest side edge, meters to the far rim)
    private void BuildZone()
    {
        zoneBuiltFor = pattern;
        zoneBuilt = true;

        float range = pattern != null ? Mathf.Min(zoneMaxRange, pattern.bulletSpeed * pattern.lifetime) : zoneMaxRange;
        range = Mathf.Max(2f, range);

        // the angles the bullets cover: fan, full circle or a lane
        bool full = false, lane = false;
        float aMin = 0f, aMax = 0f, half = 0.3f;
        if (pattern == null)
        {
            lane = true;
        }
        else
        {
            int count = Mathf.Max(1, pattern.count);
            int waves = Mathf.Max(1, pattern.waves);
            float s0 = count > 1 ? -pattern.arc * 0.5f + pattern.angleOffset : pattern.angleOffset;
            float e0 = count > 1 ? s0 + pattern.arc : pattern.angleOffset;
            float rotEnd = (waves - 1) * pattern.perWaveRotationDelta;
            aMin = s0 + Mathf.Min(0f, rotEnd);
            aMax = e0 + Mathf.Max(0f, rotEnd);

            // a little wider than the bullets, so the zone covers their size
            float pad = Mathf.Clamp(Mathf.Rad2Deg * (pattern.hitRadius / (0.5f * range)), 1.5f, 10f);
            aMin -= pad;
            aMax += pad;

            if (pattern.mode == FireMode.Ring && count > 1) full = true;
            if (aMax - aMin >= 359f) full = true;
            if (!full && (aMax - aMin) < 4f + 2f * pad)
            {
                lane = true;
                half = Mathf.Max(pattern.hitRadius, 0.25f);
            }
        }
        if (full) { aMin = -180f; aMax = 180f; }

        const int rings = 8;
        System.Collections.Generic.List<Vector3> verts = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<Vector2> uv0 = new System.Collections.Generic.List<Vector2>();
        System.Collections.Generic.List<Vector2> uv1 = new System.Collections.Generic.List<Vector2>();
        System.Collections.Generic.List<int> tris = new System.Collections.Generic.List<int>();

        int cols;
        if (lane)
        {
            cols = 2;   // three columns: left edge, middle, right edge
            for (int j = 0; j <= rings; j++)
            {
                float z = range * j / rings;
                for (int k = 0; k <= cols; k++)
                {
                    float x = Mathf.Lerp(-half, half, k / (float)cols);
                    verts.Add(new Vector3(x, 0f, z));
                    uv0.Add(new Vector2(z / range, k / (float)cols));
                    uv1.Add(new Vector2(half - Mathf.Abs(x), range - z));
                }
            }
        }
        else
        {
            cols = full ? 72 : Mathf.Clamp(Mathf.CeilToInt((aMax - aMin) / 5f), 6, 72);
            for (int j = 0; j <= rings; j++)
            {
                float r = range * j / rings;
                for (int k = 0; k <= cols; k++)
                {
                    float f = k / (float)cols;
                    float ang = Mathf.Lerp(aMin, aMax, f);
                    float rad = ang * Mathf.Deg2Rad;
                    verts.Add(new Vector3(Mathf.Sin(rad) * r, 0f, Mathf.Cos(rad) * r));
                    uv0.Add(new Vector2(r / range, f));
                    // meters to the nearest side edge (measured along the arc); a full circle has no side edges
                    float edge = full ? 99f : r * Mathf.Min((ang - aMin), (aMax - ang)) * Mathf.Deg2Rad;
                    uv1.Add(new Vector2(edge, range - r));
                }
            }
        }

        int stride = cols + 1;
        for (int j = 0; j < rings; j++)
        {
            for (int k = 0; k < cols; k++)
            {
                int a = j * stride + k;
                int b = a + 1;
                int c = a + stride;
                int d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        if (zoneMesh == null)
        {
            zoneMesh = new Mesh();
            zoneMesh.name = "MinionDangerZone";
        }
        zoneMesh.Clear();
        zoneMesh.SetVertices(verts);
        zoneMesh.SetUVs(0, uv0);
        zoneMesh.SetUVs(1, uv1);
        zoneMesh.SetTriangles(tris, 0);
        zoneMesh.bounds = new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
        zoneMf.sharedMesh = zoneMesh;
        if (dustPS != null)
        {
            ParticleSystem.ShapeModule dsh = dustPS.shape;
            dsh.mesh = zoneMesh;
        }
    }

    // ------------------------------------------------------------------ build
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        HealVFXAssets.EnsureHitFX(gameObject);   // before any child mesh, see HealVFXAssets.EnsureHitFX

        anchor = new GameObject("MinionShotAnchor").transform;
        anchor.SetParent(transform, false);

        Material dotMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Dot, brightness);
        Material ringMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ring, brightness);
        Material circleMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Summon, brightness);

        // the orb swells at the muzzle (local space: it follows the muzzle)
        orbPS = HealVFXAssets.MakeSystem(anchor, "ShotOrb", dotMat, 40, false);
        ParticleSystem.MainModule om = orbPS.main;
        om.startLifetime = 0.35f;
        om.startColor = new ParticleSystem.MinMaxGradient(coreColor, mainColor);
        HealVFXAssets.FadeInOut(orbPS, 0.3f, 0.5f, 0.9f);

        // sparks are sucked into the orb: they spawn on a sphere and fly inward (negative speed)
        convergePS = HealVFXAssets.MakeSystem(anchor, "ShotConverge", dotMat, 120, false);
        ParticleSystem.MainModule cm = convergePS.main;
        cm.startSpeed = -(convergeRadius / 0.35f);
        cm.startLifetime = 0.35f;
        cm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
        cm.startColor = new ParticleSystem.MinMaxGradient(mainColor, accentColor);
        ParticleSystem.ShapeModule cs = convergePS.shape;
        cs.enabled = true;
        cs.shapeType = ParticleSystemShapeType.Sphere;
        cs.radius = convergeRadius;
        cs.radiusThickness = 0f;
        HealVFXAssets.FadeInOut(convergePS, 0.1f, 0.6f, 1f);
        HealVFXAssets.SizeCurve(convergePS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));

        // the spinning magic circle at the muzzle, facing the camera
        castPS = HealVFXAssets.MakeSystem(anchor, "ShotCastCircle", circleMat, 4, false);
        ParticleSystem.RotationOverLifetimeModule rot = castPS.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-150f * Mathf.Deg2Rad);
        HealVFXAssets.FadeInOut(castPS, 0.2f, 0.85f, 0.85f);
        HealVFXAssets.SizeCurve(castPS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.05f)));

        // muzzle flash (world space)
        flashPS = HealVFXAssets.MakeSystem(anchor, "ShotFlash", dotMat, 4, true);
        HealVFXAssets.FadeInOut(flashPS, 0.08f, 0.25f, 1f);
        HealVFXAssets.SizeCurve(flashPS, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.4f)));

        burstPS = HealVFXAssets.MakeSystem(anchor, "ShotBurst", dotMat, 150, true);
        ParticleSystemRenderer br = burstPS.GetComponent<ParticleSystemRenderer>();
        br.renderMode = ParticleSystemRenderMode.Stretch;
        br.velocityScale = BurstVelocityScale;
        br.lengthScale = BurstLengthScale;
        HealVFXAssets.FadeInOut(burstPS, 0.3f, 0.6f, 1f);   // fades in, so the first frame is never a hard white burst
        HealVFXAssets.SizeCurve(burstPS, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        ringPS = HealVFXAssets.MakeSystem(anchor, "ShotRing", ringMat, 4, true);   // faces the camera
        HealVFXAssets.FadeInOut(ringPS, 0.05f, 0.3f, 1f);
        HealVFXAssets.SizeCurve(ringPS, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 1f)));

        // the danger zone on the floor
        Shader zs = zoneShader != null ? zoneShader : Shader.Find("VFX/DangerZone");
        if (zs == null)
        {
            Debug.LogWarning("[MinionShotVFX] Shader 'VFX/DangerZone' not found, so there is no danger zone. " +
                             "Add VFX_DangerZone.shader to the project and drag it into 'Zone Shader'.", this);
        }
        else
        {
            GameObject zg = new GameObject("MinionDangerZone");
            zoneTf = zg.transform;
            zoneTf.SetParent(transform, false);
            zoneMf = zg.AddComponent<MeshFilter>();
            zoneMr = zg.AddComponent<MeshRenderer>();
            zoneMat = new Material(zs);
            zoneMr.sharedMaterial = zoneMat;
            zoneMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            zoneMr.receiveShadows = false;
            zoneMr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            zoneMr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            zoneMr.enabled = false;
            HealVFXAssets.ProtectFromHitFX(gameObject, zoneMr);

            // small stars that rise from the whole surface of the zone (the mesh is the emitter)
            if (zoneDust)
            {
                Material starMat = HealVFXAssets.CreateMaterial(HealVFXAssets.BulletStar, brightness);
                dustPS = HealVFXAssets.MakeSystem(zoneTf, "ZoneDust", starMat, 200, true);
                ParticleSystem.MainModule dm = dustPS.main;
                dm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                dm.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
                dm.startColor = new ParticleSystem.MinMaxGradient(mainColor, coreColor);
                ParticleSystem.ShapeModule dsh = dustPS.shape;
                dsh.enabled = true;
                dsh.shapeType = ParticleSystemShapeType.Mesh;
                dsh.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                ParticleSystem.VelocityOverLifetimeModule dv = dustPS.velocityOverLifetime;
                dv.enabled = true;
                dv.space = ParticleSystemSimulationSpace.World;
                dv.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                dv.y = new ParticleSystem.MinMaxCurve(0.4f, 1f);
                dv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                ParticleSystem.RotationOverLifetimeModule dr = dustPS.rotationOverLifetime;
                dr.enabled = true;
                dr.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                HealVFXAssets.FadeInOut(dustPS, 0.15f, 0.5f, 1f);
                HealVFXAssets.SizeCurve(dustPS, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
            }
        }
    }

    // ------------------------------------------------------------------ update
    private void LateUpdate()
    {
        if (!built) return;
        if (dirty)
        {
            dirty = false;
            Rebuild();
            return;
        }

        anchor.position = Origin();

        if (telegraphing && Time.time - start > dur + 0.6f)
        {
            // safety: the attack ended without telling us (for example the minion was stunned)
            Cancel();
            return;
        }

        float p = telegraphing ? Mathf.Clamp01((Time.time - start) / dur) : 0f;

        // the orb swells, the sparks come faster and faster
        ParticleSystem.MainModule om = orbPS.main;
        float size = Mathf.Lerp(0.1f, orbSize, S(p));
        om.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.1f);
        HealVFXAssets.SetRate(orbPS, telegraphing ? 12f : 0f);
        HealVFXAssets.SetRate(convergePS, telegraphing ? convergeRate * (0.3f + 0.7f * p) : 0f);

        // the ring under the floating minion glows stronger while it charges
        if (hover != null) hover.SetCharge(telegraphing ? S(p) : 0f);

        UpdateZone(p);
    }

    private void UpdateZone(float p)
    {
        if (zoneMr == null) return;

        bool flash = Time.time < zoneFlashUntil;
        bool show = showZone && (telegraphing || flash);
        if (!show)
        {
            if (zoneMr.enabled) zoneMr.enabled = false;
            if (dustPS != null) HealVFXAssets.SetRate(dustPS, 0f);
            return;
        }

        // (re)build the mesh when the pattern changed
        if (!zoneBuilt || zoneBuiltFor != pattern) BuildZone();

        // on the floor under the muzzle, turned toward the aim
        Vector3 o = anchor.position;
        Vector3 g = HealVFXAssets.GroundPoint(transform, groundMask, zoneHeight, 12f);
        zoneTf.position = new Vector3(o.x, g.y, o.z);
        zoneTf.rotation = Quaternion.Euler(0f, Mathf.Atan2(aimDir.x, aimDir.z) * Mathf.Rad2Deg, 0f);
        zoneTf.localScale = Vector3.one / Mathf.Max(0.0001f, transform.lossyScale.x);

        lockBlend = Mathf.MoveTowards(lockBlend, locked || flash ? 1f : 0f, Time.deltaTime * 10f);
        float alpha = flash ? Mathf.Clamp01((zoneFlashUntil - Time.time) / 0.14f) : Mathf.Lerp(0.55f, 1f, S(p));

        if (!zoneMr.enabled) zoneMr.enabled = true;
        zoneMat.SetColor("_Color", mainColor);
        zoneMat.SetColor("_EdgeColor", new Color(coreColor.r * 1.6f, coreColor.g * 1.6f, coreColor.b * 1.8f, 1f));
        zoneMat.SetFloat("_Fill", zoneFill);
        zoneMat.SetFloat("_Progress", flash ? 1f : p);
        zoneMat.SetFloat("_Lock", lockBlend);
        zoneMat.SetFloat("_Alpha", alpha);

        // a wave races across the zone when the aim locks; stars rise from it while the minion charges
        zoneMat.SetFloat("_LockT", locked ? Mathf.Clamp01((Time.time - lockStart) / 0.35f) : (flash ? 1f : 0f));
        if (dustPS != null) HealVFXAssets.SetRate(dustPS, zoneDust && telegraphing ? zoneDustRate * (0.3f + 0.7f * p) : 0f);
    }
}