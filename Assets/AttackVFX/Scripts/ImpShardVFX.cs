using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visual effect for the Imp's single homing projectile: a spinning faceted ICE SHARD with a long glowing trail,
/// like the reference image, but one projectile.
///
///   SHARD         a faceted crystal that points in the direction of travel and spins; every face glitters
///   TRAIL         a long energy ribbon that bends with the homing path + two thin spiral ribbons around the shard
///   SPARKLES      star glints and small crystal fragments shed along the path
///   GLOW + LIGHT  a soft halo around the shard and a small light that lights up the ground it flies over
///   SPAWN / IMPACT  a flash when it is fired; when it hits (or fizzles out) the shard shatters into fragments,
///                   sparkles and a flash, and the trails stay behind and fade out by themselves
///
/// SETUP: add this component to the projectile prefab (RangedEnemyBullet) and assign the three materials
/// (see the slots below). ImpFireball starts the effect, hides the old Line Renderer and reports the impact.
/// If the component is missing, ImpFireball adds it by itself (works in the editor; for builds use the prefab + materials).
/// </summary>
public class ImpShardVFX : MonoBehaviour
{
    [Header("Materials (assign them for builds; in the editor they are created from the shaders if empty)")]
    [Tooltip("Material using shader VFX/ShardCrystal (colors of the shard are set on this material)")]
    public Material crystalMaterial;
    [Tooltip("Material using shader VFX/ShardTrail")]
    public Material trailMaterial;
    [Tooltip("Material using shader VFX/ShardSparkle")]
    public Material sparkleMaterial;

    [Header("Switches")]
    public bool enableShard = true;
    public bool enableTrail = true;
    public bool enableSpiralTrails = true;
    public bool enableSparkles = true;
    public bool enableFragments = true;
    public bool enableGlow = true;
    public bool enableLight = true;
    public bool enableImpact = true;

    [Header("Shard")]
    [Tooltip("Size of the shard. 1 = about 1 unit long.")]
    public float shardSize = 0.75f;
    public float spinSpeed = 240f;
    [Tooltip("Seconds the shard takes to grow to full size when it is fired.")]
    public float popInTime = 0.1f;

    [Header("Colors")]
    public Color trailHeadColor = new Color(0.85f, 0.95f, 1f, 1f);
    public Color trailMidColor = new Color(0.35f, 0.65f, 1f, 1f);
    public Color trailTailColor = new Color(0.5f, 0.3f, 1f, 1f);
    [Tooltip("Sparkles and the flash on impact")]
    public Color sparkColorA = new Color(0.8f, 0.92f, 1f, 1f);
    public Color sparkColorB = new Color(0.65f, 0.55f, 1f, 1f);
    [ColorUsage(true, true)] public Color glowColor = new Color(0.35f, 0.6f, 1f, 1f);
    [ColorUsage(true, true)] public Color lightColor = new Color(0.45f, 0.65f, 1f, 1f);

    [Header("Trail")]
    public float trailTime = 0.4f;
    public float trailWidth = 0.34f;
    public float spiralRadius = 0.13f;
    public float spiralWidth = 0.07f;

    [Header("Sparkles and fragments")]
    [Tooltip("Star glints shed per unit of distance flown.")]
    public float sparklesPerUnit = 9f;
    [Tooltip("Small crystal fragments shed per unit of distance flown.")]
    public float fragmentsPerUnit = 1.2f;

    [Header("Glow and light")]
    public float glowSize = 0.75f;
    public float lightIntensity = 1.8f;
    public float lightRange = 4f;

    [Header("Impact")]
    public int impactSparkles = 24;
    public int impactFragments = 12;
    public float impactFlashSize = 2.2f;
    public float impactLightIntensity = 5f;

    // ------------------------------------------------------------------ runtime
    private static bool appQuitting;
    private static Material fallbackCrystal, fallbackTrail, fallbackSparkle;
    private static Mesh crystalMesh;
    private static Mesh quadMesh;

    private bool started, impacted;
    private float age, spin, invScale = 1f;
    private Vector3 lastPos;
    private Camera cam;

    private LineRenderer oldLine;
    private Transform spinTf, shardTf, haloTf;
    private MeshRenderer shardRenderer, haloRenderer;
    private TrailRenderer mainTrail, spiralA, spiralB;
    private ImpShardFXRoot fxRoot;
    private ParticleSystem sparkles, fragments;
    private MaterialPropertyBlock block;
    private float sparkleCarry, fragmentCarry;

    private void OnApplicationQuit() { appQuitting = true; }

    // ------------------------------------------------------------------ public API
    /// <summary>Starts the effect. Called by ImpFireball.Initialize.</summary>
    public void Begin(Vector3 travelDirection)
    {
        if (started) return;
        started = true;
        block = new MaterialPropertyBlock();

        float s = Mathf.Abs(transform.lossyScale.x);
        invScale = 1f / Mathf.Max(0.0001f, s);   // keeps everything at world size even though the projectile prefab is scaled

        oldLine = GetComponent<LineRenderer>();
        if (oldLine != null)
        {
            oldLine.enabled = false;   // replaced by the new trail
            Destroy(oldLine);          // its old "Toon/Toon" material cannot render in URP (it shows pink)
        }

        BuildEffect();
        lastPos = transform.position;

        // the flash when it is fired
        if (sparkles != null && enableSparkles)
        {
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            ep.position = transform.position;
            ep.velocity = Vector3.zero;
            ep.startSize = 1.1f;
            ep.startLifetime = 0.14f;
            ep.startColor = Color.Lerp(sparkColorA, Color.white, 0.5f);
            sparkles.Emit(ep, 1);
        }
    }

    /// <summary>The projectile hit something (or fizzled out). awayDirection points back where it came from / away from the surface.</summary>
    public void Impact(Vector3 point, Vector3 awayDirection, bool hit)
    {
        if (!started || impacted) return;
        impacted = true;

        awayDirection.y = Mathf.Max(awayDirection.y, 0f);
        if (awayDirection.sqrMagnitude < 0.001f) awayDirection = Vector3.up;
        awayDirection.Normalize();

        // the trails stay where they are and fade out by themselves
        DetachTrail(mainTrail);
        DetachTrail(spiralA);
        DetachTrail(spiralB);

        if (shardRenderer != null) shardRenderer.enabled = false;
        if (haloRenderer != null) haloRenderer.enabled = false;

        if (enableImpact)
        {
            float strength = hit ? 1f : 0.5f;   // a fizzle is smaller than a real hit

            if (sparkles != null)
            {
                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();

                // big star flash
                ep.position = point;
                ep.velocity = Vector3.zero;
                ep.startSize = impactFlashSize * strength;
                ep.startLifetime = 0.16f;
                ep.startColor = Color.Lerp(sparkColorA, Color.white, 0.6f);
                sparkles.Emit(ep, 1);

                // star glints flying out
                int n = Mathf.RoundToInt(impactSparkles * strength);
                for (int i = 0; i < n; i++)
                {
                    Vector3 dir = Vector3.Slerp(awayDirection, Random.onUnitSphere, 0.7f).normalized;
                    ep.position = point + Random.insideUnitSphere * 0.1f;
                    ep.velocity = dir * Random.Range(2f, 6f);
                    ep.startSize = Random.Range(0.1f, 0.3f);
                    ep.startLifetime = Random.Range(0.35f, 0.8f);
                    ep.startColor = Color.Lerp(sparkColorA, sparkColorB, Random.value);
                    sparkles.Emit(ep, 1);
                }
            }

            if (fragments != null && enableFragments)
            {
                ParticleSystem.EmitParams fp = new ParticleSystem.EmitParams();
                int n = Mathf.RoundToInt(impactFragments * strength);
                for (int i = 0; i < n; i++)
                {
                    Vector3 dir = Vector3.Slerp(awayDirection, Random.onUnitSphere, 0.6f).normalized;
                    fp.position = point + Random.insideUnitSphere * 0.1f;
                    fp.velocity = dir * Random.Range(3f, 8f);
                    fp.startSize = Random.Range(0.1f, 0.2f);
                    fp.startLifetime = Random.Range(0.6f, 1.1f);
                    fp.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                    fp.angularVelocity3D = new Vector3(Random.Range(-400f, 400f), Random.Range(-400f, 400f), Random.Range(-400f, 400f));
                    fragments.Emit(fp, 1);
                }
            }
        }

        if (fxRoot != null) fxRoot.StartImpact(point, enableLight && enableImpact ? impactLightIntensity : 0f, 0.3f, 2.2f);
    }

    // ------------------------------------------------------------------ build
    private Material Get(Material assigned, string shaderName, ref Material cache)
    {
        if (assigned != null) return assigned;
        if (cache != null) return cache;

        Shader s = Shader.Find(shaderName);
        if (s == null)
        {
            Debug.LogError("[ImpShardVFX] Shader '" + shaderName + "' not found. Add the shader to the project, create a material from it " +
                "and assign it in the ImpShardVFX component on the projectile prefab (builds only include shaders that a material uses).", this);
            return null;
        }
        cache = new Material(s);
        return cache;
    }

    private bool IsUsable(Material m, string label)
    {
        if (m == null) return false;

        Shader sh = m.shader;
        if (sh == null || !sh.isSupported || sh.name.StartsWith("Hidden/InternalErrorShader"))
        {
            Debug.LogError("[ImpShardVFX] The " + label + " material '" + m.name + "' uses shader '" + (sh != null ? sh.name : "(none)") +
                "', which cannot render in this project (it would show as pink). Click the material and check its Shader dropdown, " +
                "and look in the Console for a red 'Shader error' message about it.", this);
            return false;
        }
        return true;
    }

    private void BuildEffect()
    {
        Material crystalMat = Get(crystalMaterial, "VFX/ShardCrystal", ref fallbackCrystal);
        Material trailMat = Get(trailMaterial, "VFX/ShardTrail", ref fallbackTrail);
        Material sparkleMat = Get(sparkleMaterial, "VFX/ShardSparkle", ref fallbackSparkle);

        // a material whose shader cannot render here would show as PINK: skip that part and say which material it is
        if (!IsUsable(crystalMat, "Crystal")) crystalMat = null;
        if (!IsUsable(trailMat, "Trail")) trailMat = null;
        if (!IsUsable(sparkleMat, "Sparkle")) sparkleMat = null;

        if (crystalMesh == null) crystalMesh = BuildCrystalMesh();
        if (quadMesh == null) quadMesh = BuildQuad();

        // ---- everything that spins lives under 'Spin' (scaled so its children are measured in world units)
        GameObject spinGo = new GameObject("Spin");
        spinTf = spinGo.transform;
        spinTf.SetParent(transform, false);
        spinTf.localScale = Vector3.one * invScale;

        float tailBack = 0.4f * shardSize;   // the back tip of the shard, in world units

        // ---- the shard
        if (enableShard && crystalMat != null)
        {
            GameObject shardGo = new GameObject("Shard");
            shardTf = shardGo.transform;
            shardTf.SetParent(spinTf, false);
            shardGo.AddComponent<MeshFilter>().sharedMesh = crystalMesh;
            shardRenderer = shardGo.AddComponent<MeshRenderer>();
            shardRenderer.sharedMaterial = crystalMat;
            shardRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shardRenderer.receiveShadows = false;
            shardRenderer.lightProbeUsage = LightProbeUsage.Off;
            shardRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shardTf.localScale = Vector3.one * shardSize * 0.2f;
        }

        // ---- soft glow around the shard (a camera-facing quad)
        if (enableGlow && sparkleMat != null)
        {
            GameObject haloGo = new GameObject("Halo");
            haloTf = haloGo.transform;
            haloTf.SetParent(transform, false);
            haloTf.localScale = Vector3.one * invScale * glowSize;
            haloGo.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            haloRenderer = haloGo.AddComponent<MeshRenderer>();
            haloRenderer.sharedMaterial = sparkleMat;
            haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            haloRenderer.receiveShadows = false;
            haloRenderer.lightProbeUsage = LightProbeUsage.Off;
            haloRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            haloRenderer.GetPropertyBlock(block);
            block.SetColor("_Tint", glowColor);
            block.SetFloat("_StarStrength", 0f);   // soft round glow, no star
            block.SetFloat("_Intensity", 1.4f);
            haloRenderer.SetPropertyBlock(block);
        }

        // ---- trails
        if (enableTrail && trailMat != null)
        {
            mainTrail = MakeTrail("Trail", transform, new Vector3(0f, 0f, -tailBack * invScale), trailWidth, true, trailMat);

            if (enableSpiralTrails)
            {
                spiralA = MakeTrail("SpiralA", spinTf, new Vector3(spiralRadius, 0f, -tailBack), spiralWidth, false, trailMat);
                spiralB = MakeTrail("SpiralB", spinTf, new Vector3(-spiralRadius, 0f, -tailBack), spiralWidth, false, trailMat);
            }
        }

        // ---- particles + light live in their own object so they can outlive the projectile
        GameObject fx = new GameObject("ImpShardFX");
        fxRoot = fx.AddComponent<ImpShardFXRoot>();
        fxRoot.follow = transform;
        fx.transform.position = transform.position;

        if (sparkleMat != null && (enableSparkles || enableImpact))
        {
            sparkles = MakeSystem(fx.transform, "Sparkles", sparkleMat, ParticleSystemRenderMode.Billboard, 300);
            var rot = sparkles.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var sz = sparkles.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
            FadeOverLife(sparkles);
            sparkles.Play();
        }

        if (crystalMat != null && (enableFragments || enableImpact))
        {
            fragments = MakeSystem(fx.transform, "Fragments", crystalMat, ParticleSystemRenderMode.Mesh, 100);
            fragments.GetComponent<ParticleSystemRenderer>().mesh = crystalMesh;
            var m = fragments.main;
            m.startRotation3D = true;
            m.gravityModifier = 0.6f;
            var sz = fragments.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
            fragments.Play();
        }

        if (enableLight)
        {
            GameObject lightGo = new GameObject("Light");
            lightGo.transform.SetParent(fx.transform, false);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.shadows = LightShadows.None;
            l.color = lightColor;
            l.intensity = lightIntensity;
            l.range = lightRange;
            fxRoot.fxLight = l;
        }
    }

    private TrailRenderer MakeTrail(string name, Transform parent, Vector3 localPos, float width, bool main, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.sharedMaterial = mat;
        tr.time = trailTime;
        tr.minVertexDistance = 0.04f;
        tr.widthMultiplier = width;
        tr.widthCurve = main
            ? new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.15f, 0.8f), new Keyframe(1f, 0f))
            : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(trailHeadColor, 0f), new GradientColorKey(trailMidColor, 0.35f), new GradientColorKey(trailTailColor, 1f) },
            new[] { new GradientAlphaKey(main ? 1f : 0.85f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = g;

        tr.alignment = LineAlignment.View;
        tr.textureMode = LineTextureMode.Stretch;
        tr.numCapVertices = 3;
        tr.numCornerVertices = 2;
        tr.shadowCastingMode = ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.lightProbeUsage = LightProbeUsage.Off;
        tr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        tr.autodestruct = false;
        tr.emitting = true;
        tr.Clear();
        return tr;
    }

    private static void DetachTrail(TrailRenderer tr)
    {
        if (tr == null) return;
        tr.emitting = false;
        tr.transform.SetParent(null, true);
        Destroy(tr.gameObject, tr.time + 0.3f);
    }

    private static ParticleSystem MakeSystem(Transform parent, string name, Material mat, ParticleSystemRenderMode mode, int maxParticles)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;
        main.maxParticles = maxParticles;

        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = mode;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    private static void FadeOverLife(ParticleSystem ps)
    {
        var c = ps.colorOverLifetime;
        c.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        c.color = g;
    }

    // ------------------------------------------------------------------ update
    private void Update()
    {
        if (!started || impacted) return;

        float dt = Time.deltaTime;
        age += dt;

        // the shard grows in when it is fired and spins around its direction of travel
        spin += spinSpeed * dt;
        if (spinTf != null) spinTf.localRotation = Quaternion.AngleAxis(spin, Vector3.forward);
        if (shardTf != null)
        {
            float pop = Mathf.Clamp01(age / Mathf.Max(0.01f, popInTime));
            pop = 1f - (1f - pop) * (1f - pop) * (1f - pop);
            shardTf.localScale = Vector3.one * shardSize * Mathf.Lerp(0.2f, 1f, pop);
        }

        // sparkles and fragments are shed along the path, with no gaps between frames
        Vector3 pos = transform.position;
        float dist = Vector3.Distance(pos, lastPos);
        if (dist > 0.0001f)
        {
            Vector3 back = (lastPos - pos).normalized;
            EmitAlongPath(pos, lastPos, dist, back);
        }
        lastPos = pos;
    }

    private void LateUpdate()
    {
        if (!started || impacted || haloTf == null) return;

        if (cam == null) cam = Camera.main;
        if (cam != null) haloTf.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);

        float pulse = 1f + 0.08f * Mathf.Sin(age * 28f);
        haloTf.localScale = Vector3.one * invScale * glowSize * pulse;
    }

    private void EmitAlongPath(Vector3 from, Vector3 to, float dist, Vector3 back)
    {
        if (sparkles != null && enableSparkles)
        {
            sparkleCarry += dist * sparklesPerUnit;
            int n = Mathf.FloorToInt(sparkleCarry);
            sparkleCarry -= n;

            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = Vector3.Lerp(from, to, Random.value) + Random.insideUnitSphere * 0.12f;
                ep.velocity = Random.insideUnitSphere * 0.8f + back * Random.Range(0.1f, 0.6f);
                ep.startSize = Random.Range(0.12f, 0.3f);
                ep.startLifetime = Random.Range(0.3f, 0.7f);
                ep.startColor = Color.Lerp(sparkColorA, sparkColorB, Random.value);
                sparkles.Emit(ep, 1);
            }
        }

        if (fragments != null && enableFragments)
        {
            fragmentCarry += dist * fragmentsPerUnit;
            int n = Mathf.FloorToInt(fragmentCarry);
            fragmentCarry -= n;

            ParticleSystem.EmitParams fp = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                fp.position = Vector3.Lerp(from, to, Random.value) + Random.insideUnitSphere * 0.1f;
                fp.velocity = Random.insideUnitSphere * 0.6f + back * Random.Range(0.3f, 1.2f);
                fp.startSize = Random.Range(0.06f, 0.12f);
                fp.startLifetime = Random.Range(0.5f, 0.9f);
                fp.rotation3D = new Vector3(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
                fp.angularVelocity3D = new Vector3(Random.Range(-500f, 500f), Random.Range(-500f, 500f), Random.Range(-500f, 500f));
                fragments.Emit(fp, 1);
            }
        }
    }

    private void OnDestroy()
    {
        // destroyed by something else without an impact (scene change, pooling...): still let the trails and particles finish
        if (!started || impacted || appQuitting || !gameObject.scene.isLoaded) return;
        Impact(transform.position, -transform.forward, false);
    }

    // ------------------------------------------------------------------ meshes
    // An elongated six-sided crystal: a pointed front tip (+Z), two rings, a short pointed back. Every face is flat.
    // uv.x runs from 0 at the tail to 1 at the tip. About 1 unit long.
    private static Mesh BuildCrystalMesh()
    {
        const int sides = 6;
        float zFront = 0.60f, zRingA = 0.14f, zRingB = -0.12f, zBack = -0.40f;
        float rA = 0.095f, rB = 0.085f;

        System.Random rng = new System.Random(7);
        Vector3 tip = new Vector3(0f, 0f, zFront);
        Vector3 tail = new Vector3(0f, 0f, zBack);
        Vector3[] a = new Vector3[sides];
        Vector3[] b = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float ang = i * Mathf.PI * 2f / sides;
            float j1 = 0.85f + (float)rng.NextDouble() * 0.3f;
            float j2 = 0.85f + (float)rng.NextDouble() * 0.3f;
            a[i] = new Vector3(Mathf.Cos(ang) * rA * j1, Mathf.Sin(ang) * rA * j1, zRingA);
            float ang2 = ang + Mathf.PI / sides * 0.6f;   // the back ring is slightly twisted, like a real crystal
            b[i] = new Vector3(Mathf.Cos(ang2) * rB * j2, Mathf.Sin(ang2) * rB * j2, zRingB);
        }

        System.Collections.Generic.List<Vector3> verts = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<int> tris = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Vector2> uvs = new System.Collections.Generic.List<Vector2>();

        for (int i = 0; i < sides; i++)
        {
            int n = (i + 1) % sides;
            AddTriangle(verts, tris, uvs, tip, a[i], a[n], zBack, zFront);            // front cone
            AddTriangle(verts, tris, uvs, a[i], b[i], b[n], zBack, zFront);           // sides, two triangles per face
            AddTriangle(verts, tris, uvs, a[i], b[n], a[n], zBack, zFront);
            AddTriangle(verts, tris, uvs, tail, b[n], b[i], zBack, zFront);           // back cone
        }

        Mesh mesh = new Mesh();
        mesh.name = "ImpShardCrystal";
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uvs);
        mesh.RecalculateNormals();   // every triangle has its own vertices, so the faces stay flat
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddTriangle(System.Collections.Generic.List<Vector3> verts, System.Collections.Generic.List<int> tris,
        System.Collections.Generic.List<Vector2> uvs, Vector3 p0, Vector3 p1, Vector3 p2, float zBack, float zFront)
    {
        // make sure the face points outward from the shard's axis
        Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
        Vector3 centroid = (p0 + p1 + p2) / 3f;
        Vector3 radial = new Vector3(centroid.x, centroid.y, 0f);
        if (Vector3.Dot(normal, radial) < 0f) { Vector3 t = p1; p1 = p2; p2 = t; }

        int start = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2);
        float range = zFront - zBack;
        uvs.Add(new Vector2((p0.z - zBack) / range, 0f));
        uvs.Add(new Vector2((p1.z - zBack) / range, 0f));
        uvs.Add(new Vector2((p2.z - zBack) / range, 0f));
        tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
    }

    private static Mesh BuildQuad()
    {
        Mesh m = new Mesh();
        m.name = "ImpShardQuad";
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}

/// <summary>
/// Lives in its own object next to the projectile: follows it while it flies (keeping the light on it),
/// then stays at the impact point, flashes the light and cleans everything up after the particles are gone.
/// </summary>
public class ImpShardFXRoot : MonoBehaviour
{
    public Transform follow;
    public Light fxLight;

    private bool hadFollow;
    private float flashT0 = -10f, flashDur = 0.3f, flashPeak;
    private float dieAt = -1f;

    private void Awake() { hadFollow = true; }

    private void Update()
    {
        if (follow != null)
        {
            transform.position = follow.position;
        }
        else if (hadFollow && dieAt < 0f && flashPeak <= 0f)
        {
            dieAt = Time.time + 2.2f;   // the projectile vanished without telling us: clean up soon
            if (fxLight != null) fxLight.enabled = false;
        }

        if (fxLight != null && fxLight.enabled && flashPeak > 0f)
        {
            float t = (Time.time - flashT0) / Mathf.Max(0.01f, flashDur);
            if (t >= 1f) fxLight.enabled = false;
            else fxLight.intensity = flashPeak * (1f - t) * (1f - t);
        }

        if (dieAt > 0f && Time.time >= dieAt) Destroy(gameObject);
    }

    public void StartImpact(Vector3 point, float lightPeak, float lightDuration, float lifetimeAfter)
    {
        follow = null;
        transform.position = point;
        dieAt = Time.time + lifetimeAfter;

        if (fxLight != null)
        {
            if (lightPeak > 0f)
            {
                flashT0 = Time.time;
                flashDur = lightDuration;
                flashPeak = lightPeak;
                fxLight.enabled = true;
                fxLight.intensity = lightPeak;
            }
            else
            {
                fxLight.enabled = false;
            }
        }
    }
}