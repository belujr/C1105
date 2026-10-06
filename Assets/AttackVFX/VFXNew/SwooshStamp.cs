using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A painted swoosh "stamp": one or more quads that play a short, TIME-BASED animation
///   1) Reveal : the stroke is painted from its tail to its head (fast, snappy)
///   2) Erode  : noise eats the tail first, so it breaks apart unevenly (never the same twice)
///   3) Fade   : whatever is left shrinks away
/// It does NOT depend on limb speed, so it never wiggles, stretches or vanishes all at once.
///
/// Prefab layout (SwooshStamp must be on the prefab ROOT):
///   Swoosh_Roundhouse            <- SwooshStamp component here
///     Quad_Main                  <- Unity Quad (3D Object > Quad), material M_Swoosh_Main, rotation X = 90
///     Quad_Thin                  <- optional second layer, material M_Swoosh_Thin, rotation X = 90
///
/// The controller calls Play(...) for you. If you drop the prefab into the scene as a child of the player
/// during play mode, it replays in a loop so you can tune it live (Loop For Tuning).
/// </summary>
[DisallowMultipleComponent]
public class SwooshStamp : MonoBehaviour
{
    public enum AnchorMode
    {
        PlayerRoot,          // at the player's feet position (use Height Offset to lift it)
        LimbPosition,        // at the limb's position at the moment the swing starts
        PlayerAtLimbHeight   // above the player's centre, but at the height of the limb
    }

    public enum MirrorAxis
    {
        QuadX, // arcs: flips left/right, so left limbs swing the other way
        QuadY  // thrusts: flips top/bottom, the head keeps pointing forward
    }

    [System.Serializable]
    public class Layer
    {
        public string name = "Layer";
        [Tooltip("The quad's renderer (Mesh Renderer with a SG_VFX_Swoosh material).")]
        public Renderer quad;
        [Tooltip("Seconds before this layer starts. Use a small delay on the thin bright arc so it follows the main arc.")]
        [Min(0f)] public float startDelay = 0f;

        [Header("Reveal (stroke is painted tail -> head)")]
        [Min(0.01f)] public float revealTime = 0.10f;

        [Header("Erode (tail breaks up first, head last)")]
        [Min(0f)] public float erodeDelay = 0.08f;
        [Min(0.01f)] public float erodeTime = 0.36f;

        [Header("Fade (leftovers shrink away)")]
        [Min(0f)] public float fadeDelay = 0.34f;
        [Min(0.01f)] public float fadeTime = 0.14f;

        [Header("Size")]
        [Range(0.5f, 1.5f)] public float sizeMultiplier = 1f;

        [System.NonSerialized] public Quaternion baseRotation;
        [System.NonSerialized] public Vector3 baseScale;
        [System.NonSerialized] public float seed;

        public float Duration()
        {
            return startDelay + Mathf.Max(revealTime, Mathf.Max(erodeDelay + erodeTime, fadeDelay + fadeTime));
        }
    }

    [Header("Layers (one per quad)")]
    public List<Layer> layers = new List<Layer>();

    [Header("Placement")]
    public AnchorMode anchor = AnchorMode.PlayerRoot;
    [Tooltip("Lifts the stamp above the anchor (world units).")]
    public float heightOffset = 0.8f;
    [Tooltip("Pushes the stamp forward from the anchor, in the direction the player faces.")]
    public float forwardOffset = 0f;
    [Tooltip("0 = lies flat on the ground plane. 1 = faces the camera. 0.2 to 0.4 reads best from a 3/4 camera.")]
    [Range(0f, 1f)] public float cameraFacing = 0.3f;
    [Tooltip("The stamp keeps following the player's position (not rotation) while it plays, so lunges don't leave it behind.")]
    public bool followOwner = true;

    [Header("Randomness (changes every play)")]
    [Tooltip("Random roll around the quad's own normal, in degrees (plus or minus).")]
    [Range(0f, 25f)] public float randomRoll = 6f;
    [Tooltip("Random size change (plus or minus, as a fraction).")]
    [Range(0f, 0.3f)] public float randomSize = 0.08f;
    [Tooltip("Left limbs mirror the stamp.")]
    public bool mirrorForLeftLimbs = true;
    public MirrorAxis mirrorAxis = MirrorAxis.QuadX;

    [Header("Timing")]
    [Tooltip("Off = the stamp slows down with hit stop (it freezes on the hit, which looks great). On = ignores hit stop.")]
    public bool useUnscaledTime = false;

    [Header("Tuning")]
    [Tooltip("PLAY MODE: if this prefab is placed in the scene (as a child of the player), it replays in a loop.")]
    public bool loopForTuning = true;
    public float loopGap = 0.6f;
    [Tooltip("EDIT MODE (also inside Prefab Mode): show a single frame of the animation so you can design the look without pressing Play. Turn OFF when done.")]
    public bool scrubPreview = false;
    [Range(0f, 1f)] public float scrubTime = 0.3f;

    // ---------------------------------------------------------------- runtime
    private static readonly int ID_Reveal = Shader.PropertyToID("_Reveal");
    private static readonly int ID_Erode = Shader.PropertyToID("_Erode");
    private static readonly int ID_Fade = Shader.PropertyToID("_Fade");
    private static readonly int ID_Seed = Shader.PropertyToID("_Seed");

    private MaterialPropertyBlock mpb;
    private bool cached;
    private bool playing;
    private bool started;
    private bool external;      // spawned by the controller (self-destroys) vs placed in the scene for tuning
    private float time;
    private float gapTimer;
    private Transform owner;
    private Vector3 followOffset;
    private bool warnedMissingProps;

    private static Camera[] camBuffer = new Camera[8];

    // ---------------------------------------------------------------- setup
    private void Reset()
    {
        // When the component is added in the editor: pick up every Mesh Renderer below as a layer
        layers.Clear();
        foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>())
            layers.Add(new Layer { name = r.name, quad = r });
    }

    private void Awake()
    {
        CacheLayers();
        SetAllEnabled(false); // hidden until Play() is called
    }

    private void CacheLayers()
    {
        if (cached) return;
        cached = true;

        if (mpb == null) mpb = new MaterialPropertyBlock();

        if (layers.Count == 0)
        {
            foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>())
                layers.Add(new Layer { name = r.name, quad = r });
        }

        foreach (Layer l in layers)
        {
            if (l.quad == null) continue;
            Transform q = l.quad.transform;
            l.baseRotation = q.localRotation;
            l.baseScale = q.localScale;
        }
    }

    private void Start()
    {
        // Placed in the scene for tuning (nobody called Play): play by ourselves, relative to the parent (the player)
        if (!started)
        {
            started = true;
            owner = transform.parent;
            Begin(null, false);
        }
    }

    // ---------------------------------------------------------------- public API
    /// <summary>Called by CombatHitboxController. owner = the player, limb = the striking limb.</summary>
    public void Play(Transform ownerTransform, Transform limb, bool leftLimb)
    {
        CacheLayers();
        started = true;
        external = true;
        owner = ownerTransform;
        Begin(limb, leftLimb);
    }

    private void Begin(Transform limb, bool leftLimb)
    {
        CacheLayers();
        time = 0f;
        gapTimer = 0f;
        playing = true;

        // ---- placement (skipped if there is no owner, e.g. a stamp sitting alone in the scene)
        if (owner != null)
        {
            Vector3 forward = owner.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 anchorPos = owner.position;
            if (anchor == AnchorMode.LimbPosition && limb != null) anchorPos = limb.position;
            else if (anchor == AnchorMode.PlayerAtLimbHeight && limb != null) anchorPos.y = limb.position.y;

            Vector3 pos = anchorPos + Vector3.up * heightOffset + forward * forwardOffset;
            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);

            // tilt the plane toward the camera so a flat arc doesn't look squashed
            if (cameraFacing > 0f)
            {
                Camera cam = PickCamera(pos);
                if (cam != null)
                {
                    Vector3 toCam = (cam.transform.position - pos).normalized;
                    Vector3 n = Vector3.Slerp(Vector3.up, toCam, cameraFacing);
                    rot = Quaternion.FromToRotation(Vector3.up, n) * rot;
                }
            }

            transform.SetPositionAndRotation(pos, rot);
            followOffset = pos - owner.position;
        }

        // ---- per-play randomness
        bool flip = mirrorForLeftLimbs && leftLimb;
        float roll = Random.Range(-randomRoll, randomRoll);
        float size = 1f + Random.Range(-randomSize, randomSize);

        foreach (Layer l in layers)
        {
            if (l.quad == null) continue;

            l.seed = Random.value;

            Transform q = l.quad.transform;
            q.localRotation = l.baseRotation * Quaternion.Euler(0f, 0f, roll); // roll around the quad's own normal

            Vector3 s = l.baseScale * (size * l.sizeMultiplier);
            if (flip)
            {
                if (mirrorAxis == MirrorAxis.QuadX) s.x = -s.x;
                else s.y = -s.y;
            }
            q.localScale = s;

            if (!warnedMissingProps && l.quad.sharedMaterial != null && !l.quad.sharedMaterial.HasProperty(ID_Reveal))
            {
                warnedMissingProps = true;
                Debug.LogWarning("[SwooshStamp] Material '" + l.quad.sharedMaterial.name +
                                 "' has no property with Reference '_Reveal'. Check the Reference names in SG_VFX_Swoosh (Node Settings).", this);
            }
        }

        Apply(0f, true);
    }

    // ---------------------------------------------------------------- update
    private void LateUpdate()
    {
        if (!Application.isPlaying) return;

        if (!playing)
        {
            // tuning mode: wait a moment, then play again
            if (!external && loopForTuning && started)
            {
                gapTimer += Time.unscaledDeltaTime;
                if (gapTimer >= loopGap) Begin(null, false);
            }
            return;
        }

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        time += dt;

        if (followOwner && owner != null)
            transform.position = owner.position + followOffset;

        Apply(time, true);

        if (time >= TotalDuration()) Finish();
    }

    private void Finish()
    {
        playing = false;
        gapTimer = 0f;
        SetAllEnabled(false);

        if (external) Destroy(transform.root.gameObject);
    }

    public float TotalDuration()
    {
        float d = 0.01f;
        foreach (Layer l in layers) d = Mathf.Max(d, l.Duration());
        return d;
    }

    // ---------------------------------------------------------------- animation
    private void Apply(float t, bool allowToggleRenderer)
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();

        foreach (Layer l in layers)
        {
            if (l.quad == null) continue;

            float lt = t - l.startDelay;
            bool visible = lt >= 0f && lt <= l.Duration() - l.startDelay;

            if (allowToggleRenderer) l.quad.enabled = visible;

            float reveal = 0f, erode = 0f, fade = 0f;
            if (visible)
            {
                reveal = EaseOutCubic(Mathf.Clamp01(lt / l.revealTime));
                erode = Smooth01(Mathf.Clamp01((lt - l.erodeDelay) / l.erodeTime));
                fade = 1f - Smooth01(Mathf.Clamp01((lt - l.fadeDelay) / l.fadeTime));
            }

            l.quad.GetPropertyBlock(mpb);
            mpb.SetFloat(ID_Reveal, reveal);
            mpb.SetFloat(ID_Erode, erode);
            mpb.SetFloat(ID_Fade, fade);
            mpb.SetFloat(ID_Seed, l.seed);
            l.quad.SetPropertyBlock(mpb);
        }
    }

    private void SetAllEnabled(bool on)
    {
        foreach (Layer l in layers)
            if (l.quad != null) l.quad.enabled = on;
    }

    private static float EaseOutCubic(float x)
    {
        float inv = 1f - x;
        return 1f - inv * inv * inv;
    }

    private static float Smooth01(float x)
    {
        return x * x * (3f - 2f * x);
    }

    // The stamp faces whichever camera is closest, so it works for the game camera and the skill-preview camera
    private static Camera PickCamera(Vector3 pos)
    {
        int count = Camera.allCamerasCount;
        if (count > camBuffer.Length) camBuffer = new Camera[count];
        count = Camera.GetAllCameras(camBuffer);

        Camera best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Camera c = camBuffer[i];
            if (c == null || !c.isActiveAndEnabled) continue;

            float d = (c.transform.position - pos).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

#if UNITY_EDITOR
    // Edit-mode preview: shows one frame of the animation (never touches the quads' enabled state or transforms,
    // so it can't accidentally modify your prefab).
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (mpb == null) mpb = new MaterialPropertyBlock();

        if (scrubPreview)
        {
            foreach (Layer l in layers) if (l.quad != null) l.seed = 0.37f;
            Apply(scrubTime * TotalDuration(), false);
        }
        else
        {
            foreach (Layer l in layers)
                if (l.quad != null) l.quad.SetPropertyBlock(null);
        }
    }
#endif
}