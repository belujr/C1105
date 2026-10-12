using UnityEngine;
using TMPro;

/// <summary>
/// "What Remains of Edith Finch"-style text reveal for any TextMeshPro text.
///
/// Letters appear left to right. Each letter starts as a tiny, faint, flickering speck
/// (slightly below its final position, slightly rotated and jittering), then grows into the
/// full letter, settles, and fades from a brighter tint to its real colour. Several letters
/// are mid-morph at once, which gives the "front" of half-formed text seen in the game.
///
/// The effect is done purely by editing the TMP mesh vertices, so it works the same on a
/// world-space canvas, a screen-space canvas, or a 3D TextMeshPro object. No shader needed.
///
/// Usage: call Play("some text"). HubStatue adds and drives this for you automatically.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public class MorphTypewriterText : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds between each character starting to appear.")]
    [Min(0.001f)] public float charInterval = 0.055f;

    [Tooltip("How long one character takes to go from a faint speck to a fully formed letter. " +
             "Longer than Char Interval = a wider band of half-formed letters.")]
    [Min(0.01f)] public float morphDuration = 0.35f;

    [Tooltip("Extra pause after , ; :")]
    [Min(0f)] public float commaPause = 0.12f;

    [Tooltip("Extra pause after . ! ? and line breaks")]
    [Min(0f)] public float sentencePause = 0.3f;

    [Tooltip("Seconds to wait before the first letter starts.")]
    [Min(0f)] public float startDelay = 0f;

    [Tooltip("Keep animating even if Time.timeScale is 0 (e.g. the game is paused while reading).")]
    public bool useUnscaledTime = true;

    [Header("Morph Look")]
    [Tooltip("Size of a letter when it first appears, as a fraction of its final size.")]
    [Range(0f, 1f)] public float startScale = 0.2f;

    [Tooltip("How far below its final spot a letter starts, as a fraction of line height.")]
    [Range(0f, 1f)] public float riseAmount = 0.12f;

    [Tooltip("Random positional wobble while a letter is forming, as a fraction of line height.")]
    [Range(0f, 0.5f)] public float jitterAmount = 0.06f;

    [Tooltip("Random rotation wobble (degrees) while a letter is forming.")]
    [Range(0f, 45f)] public float rotationJitter = 12f;

    [Tooltip("How strongly forming letters flicker / break up. 0 = none, 1 = very glitchy.")]
    [Range(0f, 1f)] public float flickerAmount = 0.7f;

    [Tooltip("How many times per second the flicker and wobble re-roll.")]
    [Min(1f)] public float flickerRate = 24f;

    [Tooltip("Colour a letter starts with before settling into its real colour.")]
    [ColorUsage(false, false)]
    public Color revealTint =new Color(1f, 1f, 0.85f, 1f);

    [Tooltip("0 = no tint, 1 = fully tinted at the start of the morph.")]
    [Range(0f, 1f)] public float tintStrength = 0.6f;

    /// <summary>True while the reveal is running.</summary>
    public bool IsPlaying { get; private set; }

    private TMP_Text tmp;
    private float[] revealTimes = new float[0];
    private float totalDuration;
    private float elapsed;
    private float speed = 1f;

    private void Awake()
    {
        tmp = GetComponent<TMP_Text>();
    }

    private void OnDisable()
    {
        // The canvas was closed mid-animation; don't leave a stale "playing" state behind.
        IsPlaying = false;
    }

    /// <summary>Sets the text and starts revealing it from the beginning.</summary>
    /// <param name="speedMultiplier">1 = normal, 2 = twice as fast, 0.5 = half speed.</param>
    public void Play(string text, float speedMultiplier = 1f)
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();

        speed = Mathf.Max(0.01f, speedMultiplier);
        tmp.text = text ?? string.Empty;
        tmp.ForceMeshUpdate();

        BuildTimings();

        if (tmp.textInfo.characterCount == 0)
        {
            IsPlaying = false;
            return;
        }

        elapsed = -startDelay;
        IsPlaying = true;

        // Apply the very first frame immediately so the full text never flashes on screen.
        ApplyFrame();
    }

    /// <summary>Sets the text and shows it fully formed, with no animation.</summary>
    public void ShowInstant(string text)
    {
        if (tmp == null) tmp = GetComponent<TMP_Text>();

        IsPlaying = false;
        tmp.text = text ?? string.Empty;
        tmp.ForceMeshUpdate(); // forces a clean mesh even if the string is unchanged
    }

    /// <summary>Stops animating and leaves the text fully formed.</summary>
    public void Complete()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        tmp.ForceMeshUpdate();
    }

    /// <summary>Stops animating without touching the mesh.</summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    // Runs in LateUpdate so we modify the mesh after TMP has done its own updates for the frame.
    private void LateUpdate()
    {
        if (!IsPlaying) return;

        elapsed += (useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime) * speed;

        if (elapsed >= totalDuration)
        {
            IsPlaying = false;
            tmp.ForceMeshUpdate(); // restore the pristine, final mesh
            return;
        }

        tmp.ForceMeshUpdate(); // regenerate the untouched mesh, then bend it for this frame
        ApplyFrame();
    }

    private void BuildTimings()
    {
        TMP_TextInfo info = tmp.textInfo;
        int count = info.characterCount;

        if (revealTimes.Length < count)
            revealTimes = new float[count];

        float t = 0f;
        for (int i = 0; i < count; i++)
        {
            revealTimes[i] = t;
            t += charInterval;

            switch (info.characterInfo[i].character)
            {
                case ',':
                case ';':
                case ':':
                    t += commaPause;
                    break;
                case '.':
                case '!':
                case '?':
                case '…': // ellipsis character
                case '\n':
                    t += sentencePause;
                    break;
            }
        }

        totalDuration = (count > 0 ? revealTimes[count - 1] : 0f) + morphDuration;
    }

    private void ApplyFrame()
    {
        TMP_TextInfo info = tmp.textInfo;
        int count = info.characterCount;
        int flickerStep = Mathf.FloorToInt(Mathf.Max(0f, elapsed) * flickerRate);

        for (int i = 0; i < count; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible) continue;

            float p = Mathf.Clamp01((elapsed - revealTimes[i]) / morphDuration);
            if (p >= 1f) continue; // fully formed: leave the pristine vertices alone

            int vi = ci.vertexIndex;
            Vector3[] verts = info.meshInfo[ci.materialReferenceIndex].vertices;
            Color32[] cols = info.meshInfo[ci.materialReferenceIndex].colors32;

            // Not started yet: invisible.
            if (p <= 0f)
            {
                for (int k = 0; k < 4; k++) cols[vi + k].a = 0;
                continue;
            }

            float inv = 1f - p;
            float eased = 1f - inv * inv * inv;                 // ease-out cubic
            float lineUnit = ci.ascender - ci.descender;       // text-local height of a line

            // --- geometry: scale (with a little overshoot), rise, wobble, rotate ---
            float back = p - 1f;
            float easeBack = 1f + 2.70158f * back * back * back + 1.70158f * back * back;
            float scale = Mathf.LerpUnclamped(startScale, 1f, easeBack);

            Vector3 offset = new Vector3(
                (Hash01(i, flickerStep * 3 + 0) - 0.5f) * 2f * jitterAmount * lineUnit * inv,
                (Hash01(i, flickerStep * 3 + 1) - 0.5f) * 2f * jitterAmount * lineUnit * inv
                    - riseAmount * lineUnit * (1f - eased),
                0f);

            float angle = (Hash01(i, flickerStep * 3 + 2) - 0.5f) * 2f * rotationJitter * inv;

            Matrix4x4 m = Matrix4x4.TRS(offset, Quaternion.Euler(0f, 0f, angle), new Vector3(scale, scale, 1f));

            // Vertex order in TMP quads: 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right.
            Vector3 center = (verts[vi] + verts[vi + 2]) * 0.5f;
            for (int k = 0; k < 4; k++)
                verts[vi + k] = center + m.MultiplyPoint3x4(verts[vi + k] - center);

            // --- colour: faint speck -> solid, bright tint -> real colour, with flicker dips ---
            float alpha = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(p / 0.4f));
            if (Hash01(i + 1013, flickerStep) < inv * flickerAmount)
                alpha *= 0.2f;

            float tint = tintStrength * (1f - eased);
            for (int k = 0; k < 4; k++)
            {
                Color32 c = cols[vi + k];
                cols[vi + k] = new Color32(
                    (byte)Mathf.Lerp(c.r, revealTint.r * 255f, tint),
                    (byte)Mathf.Lerp(c.g, revealTint.g * 255f, tint),
                    (byte)Mathf.Lerp(c.b, revealTint.b * 255f, tint),
                    (byte)(c.a * alpha));
            }
        }

        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
    }

    // Cheap deterministic pseudo-random value in [0,1) from two integers.
    private static float Hash01(int a, int b)
    {
        float s = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}
