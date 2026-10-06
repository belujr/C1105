using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A smooth ribbon that follows a limb (kicks, punches) and is drawn as a painted brush stroke.
///
/// What changed from the old version:
///  - Points age at a CONSTANT rate. The trail no longer retracts all at once when the limb slows down.
///    The tail is eaten by the shader (noise), so it breaks apart unevenly.
///  - Every point remembers how fast the limb was when it was created ("strength"). A slow wind-up makes a
///    thin line, a fast strike makes a fat stroke. Nothing shrinks globally.
///  - Writes extra mesh data for the VFX/InkRibbon shader: stable distance along the path (so noise stays glued
///    to each part of the trail instead of crawling) and strength.
///  - Picks a random shader seed per swing, so no two swings look the same.
///
/// Setup: empty GameObject -> add this component (MeshFilter and MeshRenderer are added for you) ->
/// assign a material that uses the VFX/InkRibbon shader -> save as a prefab -> put it in AttackData.swingVFX.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SmoothSwingRibbon : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Seconds a point of the ribbon lives. Longer = longer trail and more time for the tail to break up.")]
    public float lifetime = 0.55f;
    [Tooltip("Ribbon width in world units at full width and full speed.")]
    public float width = 1.5f;
    [Tooltip("Width along the ribbon. Keep this FLAT at 1: the VFX/InkRibbon shader shapes the pointed head and the thin tail, and the extra width is used by the strands.")]
    public AnimationCurve widthOverAge = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
    [Tooltip("Opacity along the ribbon (vertex alpha).")]
    public AnimationCurve alphaOverAge = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.92f, 1f), new Keyframe(1f, 0f));
    [Tooltip("Vertex color. Multiplied with the material colors.")]
    public Color color = Color.white;

    [Header("Variation (changes every swing)")]
    [Range(0f, 0.4f)]
    [Tooltip("Random change of width and lifetime for every swing (plus or minus, as a fraction).")]
    public float variation = 0.15f;

    [Header("Smoothness")]
    [Range(8, 64)] public int segments = 32;
    [Tooltip("How strongly animation jitter is smoothed away. 0 = raw path, 3 = very smooth.")]
    [Range(0, 6)] public int smoothingPasses = 3;
    [Tooltip("A new point is recorded only after the limb moved this far.")]
    public float minSampleDistance = 0.02f;
    [Tooltip("Frame times longer than this are treated as this long, so lag spikes don't wipe or stretch the ribbon.")]
    public float maxFrameTime = 0.033f;

    [Header("Speed reaction (only changes how thick each part is, never how long it lives)")]
    [Tooltip("At or below this limb speed the stroke is at its thinnest.")]
    public float minSpeed = 2f;
    [Tooltip("At or above this limb speed the stroke is at full width.")]
    public float fullSpeed = 9f;
    [Range(0f, 1f)]
    [Tooltip("Width factor while the limb is slow (0 = invisible wind-up, 1 = ignore speed).")]
    public float minWidthFactor = 0.1f;
    [Tooltip("How quickly the speed estimate reacts. Lower = smoother.")]
    public float speedResponse = 10f;
    [Range(0, 12)]
    [Tooltip("Blurs the thickness along the ribbon so it grows gradually instead of jumping from thin to thick.")]
    public int strengthSmoothing = 5;

    [Header("Impact flash (called when a hit lands)")]
    [Tooltip("Seconds the ribbon stays bright and fat after a hit. Uses real time, so it is visible during hit stop.")]
    public float flashDuration = 0.2f;

    private struct Sample
    {
        public Vector3 pos;
        public float age;
        public float dist;      // distance travelled along the path when this point was made (stable)
        public float strength;  // 0 slow .. 1 fast
    }

    private readonly List<Sample> samples = new List<Sample>(64);

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    private Vector3[] verts;
    private Vector2[] uvs;
    private Vector2[] uvs2;
    private Vector2[] uvs3;
    private Color[] cols;
    private int[] tris;
    private bool trianglesValid;

    private Vector3[] pts = new Vector3[128];
    private float[] ages = new float[128];
    private float[] dists = new float[128];
    private float[] strengths = new float[128];
    private Vector3[] outPos;
    private float[] outAge;
    private float[] outDist;
    private float[] outStrength;
    private Vector3[] outSide;
    private Vector3[] outSideSmooth;

    private bool emitting = true;
    private Vector3 lastHeadPos;
    private float smoothedSpeed;
    private float pathDist;

    private static Camera[] camBuffer = new Camera[8];
    private static readonly int ID_Seed = Shader.PropertyToID("_Seed");
    private static readonly int ID_Flash = Shader.PropertyToID("_Flash");

    private MaterialPropertyBlock block;
    private float seedValue;
    private float flash;

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

        // every swing gets a slightly different size and length
        width *= 1f + Random.Range(-variation, variation);
        lifetime *= 1f + Random.Range(-variation, variation);

        // a different noise pattern for every swing
        block = new MaterialPropertyBlock();
        seedValue = Random.value * 10f;
        block.SetFloat(ID_Seed, seedValue);
        block.SetFloat(ID_Flash, 0f);
        meshRenderer.SetPropertyBlock(block);

        AllocateBuffers();
    }

    private void OnEnable()
    {
        emitting = true;
        samples.Clear();
        lastHeadPos = transform.position;
        smoothedSpeed = 0f;
        pathDist = 0f;
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }

    /// <summary>Flashes the ribbon bright and fat for a moment. The controller calls this when a hit lands.</summary>
    public void Pulse()
    {
        flash = 1f;
        ApplyFlash();
    }

    private void ApplyFlash()
    {
        if (block == null) return;
        block.SetFloat(ID_Seed, seedValue);
        block.SetFloat(ID_Flash, flash);
        meshRenderer.SetPropertyBlock(block);
    }

    /// <summary>Stop adding new points. The ribbon keeps aging, breaks up, and then removes itself.</summary>
    public void StopEmitting()
    {
        emitting = false;
    }

    private void AllocateBuffers()
    {
        int pointCount = segments + 1;

        verts = new Vector3[pointCount * 2];
        uvs = new Vector2[pointCount * 2];
        uvs2 = new Vector2[pointCount * 2];
        uvs3 = new Vector2[pointCount * 2];
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
        outDist = new float[pointCount];
        outStrength = new float[pointCount];
        outSide = new Vector3[pointCount];
        outSideSmooth = new Vector3[pointCount];
    }

    private void LateUpdate()
    {
        // the flash fades in REAL time, so it stays visible while hit stop freezes the game
        if (flash > 0f)
        {
            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime / Mathf.Max(flashDuration, 0.01f));
            ApplyFlash();
        }

        float realDt = Time.deltaTime;
        if (realDt <= 0f) return; // fully paused (hit stop): leave the ribbon exactly as it is

        float dt = Mathf.Min(realDt, maxFrameTime);
        Vector3 head = transform.position;

        // ---- how fast is the limb moving? ----
        if (emitting)
        {
            float instantSpeed = (head - lastHeadPos).magnitude / realDt;
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, instantSpeed, 1f - Mathf.Exp(-speedResponse * dt));
        }
        lastHeadPos = head;

        float strengthNow = Mathf.InverseLerp(minSpeed, fullSpeed, smoothedSpeed);

        // ---- age and remove old points (constant rate: nothing retracts, nothing pops) ----
        for (int i = samples.Count - 1; i >= 0; i--)
        {
            Sample s = samples[i];
            s.age += dt;

            if (s.age >= lifetime) samples.RemoveAt(i);
            else samples[i] = s;
        }

        // ---- record a new point if the limb moved enough ----
        if (emitting)
        {
            float minSq = minSampleDistance * minSampleDistance;
            if (samples.Count == 0)
            {
                samples.Add(new Sample { pos = head, age = 0f, dist = pathDist, strength = strengthNow });
            }
            else
            {
                Vector3 lastPos = samples[samples.Count - 1].pos;
                if ((head - lastPos).sqrMagnitude >= minSq)
                {
                    pathDist += (head - lastPos).magnitude;
                    samples.Add(new Sample { pos = head, age = 0f, dist = pathDist, strength = strengthNow });
                }
            }
        }
        else if (samples.Count == 0)
        {
            Destroy(gameObject); // finished breaking up
            return;
        }

        // ---- gather points, newest first ----
        int n = 0;

        if (emitting)
        {
            float headDist = pathDist;
            if (samples.Count > 0) headDist += (head - samples[samples.Count - 1].pos).magnitude;

            pts[n] = head;
            ages[n] = 0f;
            dists[n] = headDist;
            strengths[n] = strengthNow;
            n++;
        }

        for (int i = samples.Count - 1; i >= 0 && n < pts.Length; i--)
        {
            Vector3 p = samples[i].pos;
            if (n > 0 && (p - pts[n - 1]).sqrMagnitude < 1e-8f) continue; // same spot as the previous point

            pts[n] = p;
            ages[n] = samples[i].age;
            dists[n] = samples[i].dist;
            strengths[n] = samples[i].strength;
            n++;
        }

        if (n < 2)
        {
            ClearMesh();
            return;
        }

        // ---- smooth the thickness along the ribbon (the newest and oldest values stay fixed) ----
        for (int pass = 0; pass < strengthSmoothing; pass++)
        {
            float prevStrength = strengths[0];
            for (int i = 1; i < n - 1; i++)
            {
                float cur = strengths[i];
                strengths[i] = (prevStrength + 2f * cur + strengths[i + 1]) * 0.25f;
                prevStrength = cur;
            }
        }

        // ---- remove animation jitter: blur the points a little (newest and oldest stay fixed) ----
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
            outDist[j] = Mathf.Lerp(dists[i], dists[i + 1], t);
            outStrength[j] = Mathf.Lerp(strengths[i], strengths[i + 1], t);
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
            float st = Mathf.Clamp01(outStrength[j]);
            st = st * st * (3f - 2f * st); // ease in and out
            float speedFactor = Mathf.Lerp(minWidthFactor, 1f, st);
            float half = 0.5f * width * Mathf.Max(0f, widthOverAge.Evaluate(ageN)) * speedFactor;

            Vector3 world = outPos[j];
            Vector3 side = outSideSmooth[j];

            verts[j * 2] = transform.InverseTransformPoint(world + side * half);
            verts[j * 2 + 1] = transform.InverseTransformPoint(world - side * half);

            uvs[j * 2] = new Vector2(ageN, 0f);
            uvs[j * 2 + 1] = new Vector2(ageN, 1f);

            Vector2 extra = new Vector2(outDist[j], outStrength[j]);
            uvs2[j * 2] = extra;
            uvs2[j * 2 + 1] = extra;

            // distance from the front end and from the back end (world units), so the shader can keep both tips pointed
            Vector2 ends = new Vector2(outDist[0] - outDist[j], outDist[j] - outDist[pointCount - 1]);
            uvs3[j * 2] = ends;
            uvs3[j * 2 + 1] = ends;

            Color c = color;
            c.a *= Mathf.Clamp01(alphaOverAge.Evaluate(ageN));
            cols[j * 2] = c;
            cols[j * 2 + 1] = c;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.uv2 = uvs2;
        mesh.uv3 = uvs3;
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