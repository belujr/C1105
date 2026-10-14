using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cinematic intro for the chamber selection map:
///   1. the forest rises out of the dark and settles (position + scale)
///   2. the camera glides in
///   3. the chamber icons drop in one after another with a spin and an overshoot "pop"
///   4. the dotted paths draw themselves from icon to icon
///
/// Put this on the SAME GameObject as MapController (not on the MapBoard, because the
/// MapBoard gets switched off when a chamber is entered and the animation must keep running).
/// All distances are relative to the size of the map, so it works at any scale.
/// </summary>
[RequireComponent(typeof(MapController))]
public class MapIntroAnimator : MonoBehaviour
{
    [Header("What to animate")]
    [Tooltip("The forest / terrain object. If empty, a child of the MapBoard named 'ChamberSelection' is used.")]
    public Transform terrainRoot;

    [Header("Timing (seconds)")]
    [Tooltip("Wait before anything moves, so the screen fade-in finishes first.")]
    public float startDelay = 0.25f;
    public float terrainDuration = 2.0f;
    public float cameraDuration = 3.2f;
    [Tooltip("Seconds after the intro starts when the first chamber icon appears.")]
    public float nodesStartAt = 1.4f;
    [Tooltip("Gap between one icon appearing and the next.")]
    public float nodeStagger = 0.14f;
    public float nodePopDuration = 0.6f;
    public float lineDrawDuration = 0.9f;

    [Header("Motion (relative to map size)")]
    [Tooltip("How far below its final position the forest starts.")]
    [Range(0f, 0.5f)] public float terrainRise = 0.18f;
    [Tooltip("Scale the forest starts at (1 = no scaling).")]
    [Range(0.3f, 1f)] public float terrainStartScale = 0.82f;
    [Tooltip("How far back the camera starts before gliding in.")]
    [Range(0f, 0.6f)] public float cameraPullBack = 0.22f;
    [Tooltip("How high above its spot each icon starts before dropping.")]
    [Range(0f, 0.3f)] public float nodeDrop = 0.1f;
    [Tooltip("Degrees an icon spins while it pops in. 0 = no spin.")]
    public float nodeSpin = 180f;
    [Tooltip("Softness of the glowing head of a path as it draws.")]
    [Range(0.01f, 0.2f)] public float lineSoftEdge = 0.06f;

    public bool IsPlaying { get; private set; }

    // ------------------------------------------------------------------ data

    private class NodeAnim
    {
        public MapNode node;
        public Transform tf;
        public Vector3 restPos;
        public Quaternion restRot;
        public Vector3 restScale;
        public float start;
    }

    private class LineAnim
    {
        public LineRenderer lr;
        public Gradient original;
        public GradientColorKey[] colorKeys;
        public float baseAlpha;
        public float start;
        public bool restored;
    }

    private static readonly Gradient scratch = new Gradient();

    private readonly List<NodeAnim> nodes = new List<NodeAnim>();
    private readonly List<LineAnim> lines = new List<LineAnim>();

    private bool prepared;
    private bool hasTerrain;
    private Vector3 terrainRestPos;
    private Vector3 terrainRestScale;
    private float mapSize = 10f;

    private Camera cam;
    private Vector3 camRestPos;
    private float camRestOrtho;
    private float camPull;

    private float total;
    private Coroutine routine;

    // ------------------------------------------------------------------ public API

    /// <summary>Start the intro. Call right after the map board and map camera were switched on.</summary>
    public void Play()
    {
        if (IsPlaying) Finish();

        MapController mc = GetComponent<MapController>();
        if (mc == null || mc.mapBoardRoot == null) return;

        Collect(mc);
        IsPlaying = true;
        // NOTE: no pose is applied here. MapNode builds its path lines in Start(), so the icons
        // must still be at their real positions until that has happened (see Run()).
        routine = StartCoroutine(Run());
    }

    /// <summary>Jump straight to the finished map.</summary>
    public void Skip()
    {
        if (IsPlaying) Finish();
    }

    // ------------------------------------------------------------------ setup

    private void Collect(MapController mc)
    {
        Transform board = mc.mapBoardRoot.transform;

        // terrain
        if (terrainRoot == null) terrainRoot = board.Find("ChamberSelection");
        hasTerrain = terrainRoot != null;
        mapSize = 10f;

        if (hasTerrain)
        {
            terrainRestPos = terrainRoot.position;
            terrainRestScale = terrainRoot.localScale;

            bool any = false;
            Bounds b = new Bounds(terrainRoot.position, Vector3.zero);
            foreach (Renderer r in terrainRoot.GetComponentsInChildren<Renderer>())
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (any) mapSize = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        }

        // camera
        cam = mc.mapCamera;
        camPull = 0f;
        if (cam != null)
        {
            camRestPos = cam.transform.position;
            camRestOrtho = cam.orthographicSize;
            float dist = hasTerrain ? Vector3.Distance(camRestPos, terrainRoot.position) : mapSize;
            camPull = dist * cameraPullBack;
        }

        // chamber icons: bottom of the screen first (lowest Z), so the path "grows" upward
        nodes.Clear();
        lines.Clear();
        List<MapNode> found = new List<MapNode>(board.GetComponentsInChildren<MapNode>(true));
        found.Sort((a, c) => a.transform.position.z.CompareTo(c.transform.position.z));

        for (int i = 0; i < found.Count; i++)
        {
            Transform t = found[i].transform;
            nodes.Add(new NodeAnim
            {
                node = found[i],
                tf = t,
                restPos = t.position,
                restRot = t.rotation,
                restScale = t.localScale,
                start = nodesStartAt + i * nodeStagger
            });
        }

        total = Mathf.Max(terrainDuration, cameraDuration);
        prepared = true;
    }

    // Paths are collected one frame later, because MapNode may build them in Start().
    private void CollectLines()
    {
        lines.Clear();
        HashSet<LineRenderer> seen = new HashSet<LineRenderer>();

        Dictionary<MapNode, float> startOf = new Dictionary<MapNode, float>();
        foreach (NodeAnim n in nodes) startOf[n.node] = n.start;

        foreach (NodeAnim n in nodes)
        {
            if (n.node == null || n.node.pathLines == null) continue;

            foreach (var kv in n.node.pathLines)
            {
                LineRenderer lr = kv.Value;
                if (lr == null || !seen.Add(lr)) continue;

                float dstStart = n.start;
                if (kv.Key != null && startOf.TryGetValue(kv.Key, out float s)) dstStart = s;

                Gradient g = lr.colorGradient;
                lines.Add(new LineAnim
                {
                    lr = lr,
                    original = g,
                    colorKeys = g.colorKeys,
                    baseAlpha = Mathf.Clamp01(g.Evaluate(0.5f).a),
                    // a path starts drawing once both of its icons have landed (paths are fixed in
                    // the world, so the icons must be at rest or the ends would not line up)
                    start = Mathf.Max(n.start, dstStart) + nodePopDuration * 0.85f
                });
            }
        }

        foreach (NodeAnim n in nodes) total = Mathf.Max(total, n.start + nodePopDuration);
        foreach (LineAnim l in lines) total = Mathf.Max(total, l.start + lineDrawDuration);
    }

    // ------------------------------------------------------------------ playback

    private IEnumerator Run()
    {
        // Let MapNode.Start() build the path lines while the icons are still at their real
        // positions (the screen is still black from the transition, so nobody sees this).
        yield return null;
        yield return null;
        CollectLines();
        Apply(0f);                  // now hide everything: the intro starts from here

        float t = -startDelay;
        while (t < total)
        {
            // clamp dt so a loading hitch can't skip half the animation
            t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            Apply(Mathf.Max(0f, t));
            yield return null;
        }

        routine = null;
        Settle();
    }

    private void Finish()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; }
        Settle();
    }

    private void Settle()
    {
        if (prepared) Apply(1000000f);   // everything at its final, resting state
        IsPlaying = false;
    }

    // ------------------------------------------------------------------ one frame

    private void Apply(float t)
    {
        // forest rises and settles
        if (hasTerrain && terrainRoot != null)
        {
            float e = EaseOutCubic(Mathf.Clamp01(t / Mathf.Max(0.01f, terrainDuration)));
            terrainRoot.position = terrainRestPos + Vector3.down * (mapSize * terrainRise * (1f - e));
            terrainRoot.localScale = terrainRestScale * Mathf.Lerp(terrainStartScale, 1f, e);
        }

        // camera glides in
        if (cam != null)
        {
            float e = EaseOutCubic(Mathf.Clamp01(t / Mathf.Max(0.01f, cameraDuration)));
            if (cam.orthographic)
                cam.orthographicSize = camRestOrtho * Mathf.Lerp(1f + cameraPullBack, 1f, e);
            else
                cam.transform.position = camRestPos - cam.transform.forward * (camPull * (1f - e));
        }

        // icons drop in, spin and pop
        float drop = mapSize * nodeDrop;
        foreach (NodeAnim n in nodes)
        {
            if (n.tf == null) continue;

            float p = Mathf.Clamp01((t - n.start) / Mathf.Max(0.01f, nodePopDuration));
            float pop = EaseOutBack(p);
            float fall = 1f - EaseOutCubic(p);
            float spin = nodeSpin * (1f - EaseOutCubic(p));

            n.tf.localScale = n.restScale * Mathf.Max(0.001f, pop);
            n.tf.position = n.restPos + Vector3.up * (drop * fall);
            n.tf.rotation = n.restRot * Quaternion.AngleAxis(spin, Vector3.up);
        }

        // dotted paths draw themselves
        foreach (LineAnim l in lines) ApplyLine(l, t);
    }

    private void ApplyLine(LineAnim l, float t)
    {
        if (l.lr == null) return;

        float p = Mathf.Clamp01((t - l.start) / Mathf.Max(0.01f, lineDrawDuration));

        if (p >= 1f)
        {
            if (!l.restored) { l.lr.colorGradient = l.original; l.restored = true; }
            return;
        }
        l.restored = false;

        float e = p * p * (3f - 2f * p);   // smoothstep
        GradientAlphaKey[] alpha;

        if (e <= 0f)
        {
            alpha = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) };
        }
        else
        {
            float head = e * (1f + lineSoftEdge);
            float solid = Mathf.Clamp01(head - lineSoftEdge);
            float tip = Mathf.Clamp01(head);
            alpha = new[]
            {
                new GradientAlphaKey(l.baseAlpha, 0f),
                new GradientAlphaKey(l.baseAlpha, solid),
                new GradientAlphaKey(0f, tip),
                new GradientAlphaKey(0f, 1f)
            };
        }

        scratch.SetKeys(l.colorKeys, alpha);
        l.lr.colorGradient = scratch;
    }

    // ------------------------------------------------------------------ easing

    private static float EaseOutCubic(float p)
    {
        p = 1f - p;
        return 1f - p * p * p;
    }

    // overshoots past 1 and settles back: the "pop"
    private static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}