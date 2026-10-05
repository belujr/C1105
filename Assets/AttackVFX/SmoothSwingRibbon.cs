using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A smooth ribbon trail for swings (motion lines / swoosh).
///
/// Why this exists instead of a Trail Renderer:
///  - It rebuilds the path as a smooth curve (Catmull-Rom), so animation jitter and lag spikes do
///    not turn into kinks, zig-zags or stretched ribbons.
///  - Its length reacts to limb speed: slow wind-up = nothing, fast strike = full ribbon,
///    and when the limb slows the tail catches up and the ribbon retracts smoothly.
///  - The ribbon always faces the camera that is looking at it (works in the skill preview too).
///
/// Setup: empty GameObject -> add this component (MeshFilter and MeshRenderer are added for you)
/// -> assign a material that uses SG_VFX_Trail -> save as a prefab -> put it in AttackData.swingVFX.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SmoothSwingRibbon : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Seconds a point of the ribbon lives while the limb is moving fast.")]
    public float lifetime = 0.25f;
    [Tooltip("Ribbon width in world units at full width.")]
    public float width = 1.2f;
    [Tooltip("Width along the ribbon. Left (0) = at the limb, right (1) = oldest part / tail.")]
    public AnimationCurve widthOverAge = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
    [Tooltip("Opacity along the ribbon (vertex alpha). The shader turns this into strokes that thin out.")]
    public AnimationCurve alphaOverAge = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));
    [Tooltip("Vertex color. The material's Tint is multiplied with this.")]
    public Color color = Color.white;

    [Header("Smoothness")]
    [Range(8, 64)] public int segments = 32;
    [Tooltip("How strongly animation jitter is smoothed away. 0 = follow the raw path, 3 = very smooth. Too high rounds off sharp turns.")]
    [Range(0, 6)] public int smoothingPasses = 3;
    [Tooltip("A new point is recorded only after the limb moved this far.")]
    public float minSampleDistance = 0.02f;
    [Tooltip("Frame times longer than this are treated as this long, so lag spikes don't wipe or stretch the ribbon.")]
    public float maxFrameTime = 0.033f;

    [Header("Speed reaction")]
    [Tooltip("At or below this limb speed the ribbon retracts quickly.")]
    public float minSpeed = 2f;
    [Tooltip("At or above this limb speed the ribbon is at full length.")]
    public float fullSpeed = 8f;
    [Tooltip("How much faster the ribbon ages when the limb is slow. Higher = the tail catches up with the limb faster.")]
    public float slowAgeRate = 6f;
    [Tooltip("How quickly the speed estimate reacts. Lower = smoother.")]
    public float speedResponse = 18f;

    private struct Sample
    {
        public Vector3 pos;
        public float age;
    }

    private readonly List<Sample> samples = new List<Sample>(64);

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private Vector3[] verts;
    private Vector2[] uvs;
    private Color[] cols;
    private int[] tris;
    private bool trianglesValid;

    private Vector3[] pts = new Vector3[128];
    private float[] ages = new float[128];
    private Vector3[] outPos;
    private float[] outAge;
    private Vector3[] outSide;
    private Vector3[] outSideSmooth;

    private bool emitting = true;
    private Vector3 lastHeadPos;
    private float smoothedSpeed;

    private static Camera[] camBuffer = new Camera[8];

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        mesh = new Mesh();
        mesh.name = "SmoothSwingRibbon";
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

        AllocateBuffers();
    }

    private void OnEnable()
    {
        emitting = true;
        samples.Clear();
        lastHeadPos = transform.position;
        smoothedSpeed = 0f;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    /// <summary>Stop adding new points. The ribbon retracts and then removes itself.</summary>
    public void StopEmitting()
    {
        emitting = false;
    }

    private void AllocateBuffers()
    {
        int pointCount = segments + 1;

        verts = new Vector3[pointCount * 2];
        uvs = new Vector2[pointCount * 2];
        cols = new Color[pointCount * 2];
        tris = new int[segments * 6];

        for (int j = 0; j < segments; j++)
        {
            int v0 = j * 2;
            int v1 = v0 + 1;
            int v2 = v0 + 2;
            int v3 = v0 + 3;
            int t = j * 6;

            tris[t + 0] = v0; tris[t + 1] = v2; tris[t + 2] = v1;
            tris[t + 3] = v1; tris[t + 4] = v2; tris[t + 5] = v3;
        }

        outPos = new Vector3[pointCount];
        outAge = new float[pointCount];
        outSide = new Vector3[pointCount];
        outSideSmooth = new Vector3[pointCount];
    }

    private void LateUpdate()
    {
        float realDt = Time.deltaTime;
        if (realDt <= 0f) return; // fully paused: leave the ribbon exactly as it is

        float dt = Mathf.Min(realDt, maxFrameTime);
        Vector3 head = transform.position;

        // ---- how fast is the limb moving? ----
        if (emitting)
        {
            float instantSpeed = (head - lastHeadPos).magnitude / realDt;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, instantSpeed, 1f - Mathf.Exp(-speedResponse * dt));
        }
        lastHeadPos = head;

        float k = emitting ? Mathf.InverseLerp(minSpeed, fullSpeed, smoothedSpeed) : 0f;
        float ageRate = Mathf.Lerp(slowAgeRate, 1f, k);

        // ---- age and remove old points (continuous, so nothing pops) ----
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            Sample s = samples[i];
            s.age += dt * ageRate;

            if (s.age >= lifetime) samples.RemoveAt(i);
            else samples[i] = s;
        }

        // ---- record a new point if the limb moved enough ----
        if (emitting)
        {
            float minSq = minSampleDistance * minSampleDistance;
            if (samples.Count == 0 || (head - samples[samples.Count - 1].pos).sqrMagnitude >= minSq)
            {
                samples.Add(new Sample { pos = head, age = 0f });
            }
        }
        else if (samples.Count == 0)
        {
            Destroy(gameObject); // finished retracting
            return;
        }

        // ---- gather points, newest first ----
        int n = 0;

        if (emitting)
        {
            pts[n] = head;
            ages[n] = 0f;
            n++;
        }

        for (int i = samples.Count - 1; i >= 0 && n < pts.Length; i--)
        {
            Vector3 p = samples[i].pos;
            if (n > 0 && (p - pts[n - 1]).sqrMagnitude < 1e-8f) continue; // same spot as the previous point

            pts[n] = p;
            ages[n] = samples[i].age;
            n++;
        }

        if (n < 2)
        {
            ClearMesh();
            return;
        }

        // ---- remove animation jitter: blur the points a little (the newest and oldest points stay fixed) ----
        for (int pass = 0; pass < smoothingPasses; pass++)
        {
            Vector3 prev = pts[0];
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 current = pts[i];
                pts[i] = (prev + 2f * current + pts[i + 1]) * 0.25f;
                prev = current;
            }
        }

        Camera cam = PickCamera(head);
        if (cam == null)
        {
            ClearMesh();
            return;
        }

        BuildMesh(n, cam);
    }

    private void BuildMesh(int n, Camera cam)
    {
        int pointCount = segments + 1;
        Vector3 camPos = cam.transform.position;

        // ---- resample the raw points as a smooth curve ----
        for (int j = 0; j < pointCount; j++)
        {
            float s = (float)j / segments * (n - 1);
            int i = Mathf.Min((int)s, n - 2);
            float t = s - i;

            Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Mathf.Min(i + 2, n - 1)];

            outPos[j] = CatmullRom(p0, p1, p2, p3, t);
            outAge[j] = Mathf.Lerp(ages[i], ages[i + 1], t);
        }

        // ---- sideways direction (faces the camera), kept consistent along the ribbon ----
        Vector3 prevSide = Vector3.zero;
        for (int j = 0; j < pointCount; j++)
        {
            Vector3 a = outPos[Mathf.Max(j - 1, 0)];
            Vector3 b = outPos[Mathf.Min(j + 1, pointCount - 1)];
            Vector3 tangent = b - a;
            Vector3 view = outPos[j] - camPos;

            Vector3 side = Vector3.Cross(tangent, view);

            if (side.sqrMagnitude < 1e-10f)
                side = prevSide.sqrMagnitude > 0f ? prevSide : cam.transform.right;
            else
                side.Normalize();

            if (prevSide.sqrMagnitude > 0f && Vector3.Dot(side, prevSide) < 0f)
                side = -side;

            outSide[j] = side;
            prevSide = side;
        }

        // one smoothing pass so the width never twists suddenly
        for (int j = 0; j < pointCount; j++)
        {
            Vector3 sum = outSide[j] * 2f;
            if (j > 0) sum += outSide[j - 1];
            if (j < pointCount - 1) sum += outSide[j + 1];

            outSideSmooth[j] = sum.sqrMagnitude > 1e-10f ? sum.normalized : outSide[j];
        }

        // ---- fill the mesh ----
        for (int j = 0; j < pointCount; j++)
        {
            float ageN = Mathf.Clamp01(outAge[j] / Mathf.Max(lifetime, 0.0001f));
            float half = 0.5f * width * Mathf.Max(0f, widthOverAge.Evaluate(ageN));

            Vector3 world = outPos[j];
            Vector3 side = outSideSmooth[j];

            verts[j * 2] = transform.InverseTransformPoint(world + side * half);
            verts[j * 2 + 1] = transform.InverseTransformPoint(world - side * half);

            uvs[j * 2] = new Vector2(ageN, 0f);
            uvs[j * 2 + 1] = new Vector2(ageN, 1f);

            Color c = color;
            c.a *= Mathf.Clamp01(alphaOverAge.Evaluate(ageN));
            cols[j * 2] = c;
            cols[j * 2 + 1] = c;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = cols;

        if (!trianglesValid)
        {
            mesh.triangles = tris;
            trianglesValid = true;
        }

        mesh.RecalculateBounds();
    }

    private void ClearMesh()
    {
        if (mesh != null && mesh.vertexCount > 0) mesh.Clear();
        trianglesValid = false;
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * ((2f * p1)
                     + (-p0 + p2) * t
                     + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                     + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    // The ribbon faces whichever camera is closest to it, so it works for the gameplay camera and the skill-preview camera
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
}