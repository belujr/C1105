using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The "I just took in a soul" reaction of the player. Added to the player automatically by HitFeedbackManager.
///
/// Every soul that arrives:
///   1. sends a thin RING of energy that starts wide and CONTRACTS into the chest (it disappears inside the body)
///   2. raises an energy level that makes the body glow (rim + flowing veins) and sparks orbit the torso
/// Several souls in a row stack up. Everything runs in real time, so it also plays during hit stop.
///
/// Where souls fly to (in this order):
///   a) a child object of the player named "SoulTarget" (create an empty at the chest for exact control)
///   b) the Chest / Spine bone, if the player has a Humanoid rig
///   c) the middle of the player's visible meshes
/// </summary>
public class PlayerSoulAbsorbFX : MonoBehaviour
{
    private class Ring
    {
        public Transform tf;
        public MeshRenderer mr;
        public float t0;
        public float seed;
        public float heightJitter;
        public Quaternion tilt;
        public bool active;
    }

    private HitFeedbackManager mgr;
    private bool built;

    private readonly List<Renderer> shells = new List<Renderer>();
    private readonly List<SkinnedMeshRenderer> shellSources = new List<SkinnedMeshRenderer>();
    private readonly List<SkinnedMeshRenderer> shellSkinned = new List<SkinnedMeshRenderer>();
    private readonly List<Renderer> bodyRenderers = new List<Renderer>();
    private readonly List<Ring> rings = new List<Ring>();

    private MaterialPropertyBlock block;
    private ParticleSystem sparks;
    private Transform aimAnchor;

    private float level;
    private float scroll;
    private bool active;

    private Vector3 cachedChest;

    /// <summary>Where souls should fly to: inside the player's chest (always the current position of the SoulTarget / bone).</summary>
    public Vector3 ChestPoint
    {
        get { return aimAnchor != null ? aimAnchor.position : cachedChest; }
    }

    public void Init(HitFeedbackManager manager)
    {
        mgr = manager;
        if (built) return;
        built = true;

        block = new MaterialPropertyBlock();

        foreach (SkinnedMeshRenderer r in GetComponentsInChildren<SkinnedMeshRenderer>(false))
            if (IsVisible(r) && r.sharedMesh != null) bodyRenderers.Add(r);
        if (bodyRenderers.Count == 0)
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(false))
                if (IsVisible(r)) bodyRenderers.Add(r);

        FindAimAnchor();
        cachedChest = ComputeChest();

        BuildShells();
        BuildRings();
        BuildSparks();
        SetActive(false);
    }

    /// <summary>One soul arrived.</summary>
    public void Absorb()
    {
        if (!built || mgr == null) return;
        level = Mathf.Min(mgr.absorbMax, level + mgr.absorbPerSoul);
        SpawnRing();
    }

    // ------------------------------------------------------------------ setup
    private static bool IsVisible(Renderer r)
    {
        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
        if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
        if (r.sharedMaterial == null) return false;
        if (r.gameObject.name == "SoulBodyShell") return false;
        return true;
    }

    private void FindAimAnchor()
    {
        // a) an empty named "SoulTarget" somewhere below the player
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.Trim() == "SoulTarget")
            {
                aimAnchor = t;
                Debug.Log("[PlayerSoulAbsorbFX] Souls aim at your SoulTarget: " + GetPath(t), t);
                return;
            }
        }

        // b) the chest bone of a Humanoid rig
        Animator anim = GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            Transform bone = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Chest);
            if (bone == null) bone = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (bone != null)
            {
                aimAnchor = bone;
                Debug.Log("[PlayerSoulAbsorbFX] No object named SoulTarget found under " + name +
                          ". Souls aim at the humanoid chest bone: " + GetPath(bone), bone);
                return;
            }
        }

        Debug.LogWarning("[PlayerSoulAbsorbFX] No object named SoulTarget found under " + name +
                         " (the empty must be a CHILD of the player object the controller uses and be named exactly SoulTarget). " +
                         "Souls aim at the middle of the player's meshes instead.", this);
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !built || mgr == null || !mgr.showAimPoint) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(ChestPoint, 0.15f);
        Gizmos.DrawLine(ChestPoint + Vector3.left * 0.3f, ChestPoint + Vector3.right * 0.3f);
        Gizmos.DrawLine(ChestPoint + Vector3.forward * 0.3f, ChestPoint + Vector3.back * 0.3f);
    }

    private Vector3 ComputeChest()
    {
        if (aimAnchor != null) return aimAnchor.position;
        if (bodyRenderers.Count == 0) return transform.position + Vector3.up;

        Bounds b = new Bounds(transform.position + Vector3.up, Vector3.zero);
        bool first = true;
        for (int i = 0; i < bodyRenderers.Count; i++)
        {
            if (bodyRenderers[i] == null) continue;
            if (first) { b = bodyRenderers[i].bounds; first = false; }
            else b.Encapsulate(bodyRenderers[i].bounds);
        }
        return b.center;
    }

    private void BuildShells()
    {
        Material mat = mgr.GetAbsorbBodyMaterial();
        if (mat == null) return;

        for (int i = 0; i < bodyRenderers.Count; i++)
        {
            SkinnedMeshRenderer src = bodyRenderers[i] as SkinnedMeshRenderer;
            if (src != null)
            {
                GameObject go = new GameObject("SoulBodyShell");
                go.transform.SetParent(src.transform, false);

                SkinnedMeshRenderer shell = go.AddComponent<SkinnedMeshRenderer>();
                shell.sharedMesh = src.sharedMesh;
                shell.bones = src.bones;
                shell.rootBone = src.rootBone;
                shell.localBounds = src.localBounds;
                shell.updateWhenOffscreen = src.updateWhenOffscreen;
                shell.quality = src.quality;

                int sub = Mathf.Max(1, src.sharedMesh.subMeshCount);
                Material[] mats = new Material[sub];
                for (int m = 0; m < sub; m++) mats[m] = mat;
                shell.sharedMaterials = mats;

                SetupShell(shell);
                shells.Add(shell);
                shellSources.Add(src);
                shellSkinned.Add(shell);
                continue;
            }

            MeshRenderer mr = bodyRenderers[i] as MeshRenderer;
            MeshFilter mf = mr != null ? mr.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null)
            {
                GameObject go = new GameObject("SoulBodyShell");
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                MeshRenderer shell = go.AddComponent<MeshRenderer>();

                int sub = Mathf.Max(1, mf.sharedMesh.subMeshCount);
                Material[] mats = new Material[sub];
                for (int m = 0; m < sub; m++) mats[m] = mat;
                shell.sharedMaterials = mats;

                SetupShell(shell);
                shells.Add(shell);
            }
        }
    }

    private static void SetupShell(Renderer shell)
    {
        shell.shadowCastingMode = ShadowCastingMode.Off;
        shell.receiveShadows = false;
        shell.lightProbeUsage = LightProbeUsage.Off;
        shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
        shell.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        shell.enabled = false;
    }

    private void BuildRings()
    {
        Material mat = mgr.GetAbsorbRingMaterial();
        if (mat == null) return;

        Mesh ringMesh = BuildRingMesh(64, 0.8f, 1f);
        for (int i = 0; i < 4; i++)
        {
            GameObject go = new GameObject("SoulRing");
            go.transform.SetParent(null);
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);

            rings.Add(new Ring { tf = go.transform, mr = mr });
        }
    }

    private void BuildSparks()
    {
        Material mat = mgr.GetSoulOrbMaterial();
        if (mat == null) return;

        GameObject go = new GameObject("SoulAbsorbSparks");
        go.transform.SetParent(null);
        sparks = go.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = sparks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local; // the sparks orbit around this object
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = Color.white;
        main.maxParticles = 160;
        main.useUnscaledTime = true;

        ParticleSystem.EmissionModule em = sparks.emission;
        em.enabled = true;
        em.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.5f;
        shape.radiusThickness = 0.2f;
        shape.rotation = new Vector3(90f, 0f, 0f);   // a horizontal ring

        // spiral inward and up, so the sparks look like they are being drawn into the body
        ParticleSystem.VelocityOverLifetimeModule vol = sparks.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.x = 0f; vol.y = 0.8f; vol.z = 0f;
        vol.orbitalX = 0f; vol.orbitalY = 6f; vol.orbitalZ = 0f;
        vol.radial = -0.55f;

        ParticleSystem.SizeOverLifetimeModule size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystem.ColorOverLifetimeModule colLife = sparks.colorOverLifetime;
        colLife.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colLife.color = fade;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        MaterialPropertyBlock b = new MaterialPropertyBlock();
        r.GetPropertyBlock(b);
        b.SetFloat("_Intensity", mgr.sparkIntensity);
        r.SetPropertyBlock(b);
    }

    // flat ring in the XZ plane. U goes around, V goes from the inner edge (0) to the outer edge (1)
    private static Mesh BuildRingMesh(int segments, float innerRadius, float outerRadius)
    {
        Vector3[] verts = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[verts.Length];
        int[] tris = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float u = (float)i / segments;
            float ang = u * Mathf.PI * 2f;
            float x = Mathf.Cos(ang), z = Mathf.Sin(ang);
            verts[i * 2] = new Vector3(x * innerRadius, 0f, z * innerRadius);
            verts[i * 2 + 1] = new Vector3(x * outerRadius, 0f, z * outerRadius);
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            int t = i * 6;
            tris[t] = a; tris[t + 1] = b; tris[t + 2] = c;
            tris[t + 3] = c; tris[t + 4] = b; tris[t + 5] = d;
        }

        Mesh m = new Mesh();
        m.name = "SoulRingMesh";
        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ------------------------------------------------------------------ rings
    private void SpawnRing()
    {
        if (rings.Count == 0) return;

        // use a free ring, or take over the oldest one
        Ring pick = null;
        float oldest = float.MaxValue;
        for (int i = 0; i < rings.Count; i++)
        {
            Ring r = rings[i];
            if (!r.active) { pick = r; break; }
            if (r.t0 < oldest) { oldest = r.t0; pick = r; }
        }

        pick.active = true;
        pick.t0 = Time.unscaledTime;
        pick.seed = Random.value * 10f;
        pick.heightJitter = Random.Range(-0.15f, 0.2f);
        pick.tilt = Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(0f, 360f), Random.Range(-14f, 14f));
        pick.tf.gameObject.SetActive(true);
    }

    private void UpdateRings()
    {
        float dur = Mathf.Max(0.1f, mgr.absorbRingDuration);

        for (int i = 0; i < rings.Count; i++)
        {
            Ring r = rings[i];
            if (!r.active) continue;

            float p = (Time.unscaledTime - r.t0) / dur;
            if (p >= 1f)
            {
                r.active = false;
                r.tf.gameObject.SetActive(false);
                continue;
            }

            // starts wide and fast, slows as it sinks into the chest
            float e = 1f - (1f - p) * (1f - p) * (1f - p);
            float radius = Mathf.Lerp(mgr.absorbRingStartRadius, mgr.absorbRingEndRadius, e);

            r.tf.position = ChestPoint + Vector3.up * (r.heightJitter + p * 0.15f);
            r.tf.rotation = r.tilt;
            r.tf.localScale = new Vector3(radius, 1f, radius);

            float envelope = Mathf.Pow(Mathf.Sin(p * Mathf.PI), 0.6f);

            block.Clear();
            block.SetFloat("_Intensity", envelope * mgr.absorbRingIntensity);
            block.SetFloat("_Rotate", p * mgr.absorbRingSpin + r.seed);
            block.SetFloat("_Seed", r.seed);
            block.SetColor("_ColorHot", mgr.soulColor);
            block.SetColor("_ColorEdge", mgr.absorbEdgeColor);
            r.mr.SetPropertyBlock(block);
        }
    }

    // ------------------------------------------------------------------ run
    private void SetActive(bool on)
    {
        active = on;

        for (int i = 0; i < shells.Count; i++)
            if (shells[i] != null) shells[i].enabled = on;

        if (sparks != null)
        {
            if (on) { if (!sparks.isPlaying) sparks.Play(); }
            else
            {
                ParticleSystem.EmissionModule em = sparks.emission;
                em.rateOverTime = 0f;
            }
        }
    }

    private void LateUpdate()
    {
        if (!built || mgr == null) return;

        cachedChest = ComputeChest();
        UpdateRings();

        float dt = Time.unscaledDeltaTime;
        level = Mathf.Max(0f, level - dt / Mathf.Max(0.05f, mgr.absorbDecay));

        if (level <= 0.001f)
        {
            if (active) SetActive(false);
            return;
        }
        if (!active) SetActive(true);

        scroll += dt * mgr.absorbScrollSpeed * (0.6f + level);

        // blend shapes (face, cloth) must follow the original
        for (int i = 0; i < shellSkinned.Count; i++)
        {
            SkinnedMeshRenderer src = shellSources[i];
            SkinnedMeshRenderer dst = shellSkinned[i];
            if (src == null || dst == null || src.sharedMesh == null) continue;
            int count = src.sharedMesh.blendShapeCount;
            for (int b = 0; b < count; b++) dst.SetBlendShapeWeight(b, src.GetBlendShapeWeight(b));
        }

        block.Clear();
        block.SetFloat("_Intensity", level * mgr.absorbBodyIntensity);
        block.SetFloat("_Scroll", scroll);
        block.SetColor("_ColorHot", mgr.soulColor);
        block.SetColor("_ColorEdge", mgr.absorbEdgeColor);
        for (int i = 0; i < shells.Count; i++)
            if (shells[i] != null) shells[i].SetPropertyBlock(block);

        if (sparks != null)
        {
            sparks.transform.position = ChestPoint;
            ParticleSystem.EmissionModule em = sparks.emission;
            em.rateOverTime = level * mgr.absorbSparkRate;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < rings.Count; i++)
            if (rings[i].tf != null) Destroy(rings[i].tf.gameObject);
        if (sparks != null) Destroy(sparks.gameObject);
    }
}