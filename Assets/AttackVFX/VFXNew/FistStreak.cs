using UnityEngine;

/// <summary>
/// A speed streak for punches and straight kicks.
///
///  1. While the limb accelerates, the streak is STRETCHED BEHIND it. It faces the camera and always points along
///     the direction the limb really moves on screen (no guessing from the player's facing).
///  2. When the limb reaches its peak and slows down (or the hit lands), the streak FREEZES where it was and breaks up
///     over time: the tail erodes first and the thin strands outlive the body.
///
/// It works with the SG_VFX_Swoosh shader (properties _Reveal, _Erode, _Fade, _Seed) and the swoosh_thrust texture.
///
/// Prefab layout:
///   FistStreak_Punch      <- this component (root scale must be 1,1,1)
///     Quad_Main           <- Unity Quad: Rotation 0,0,0, Scale 1,1,1, material with SG_VFX_Swoosh
///
/// Put the prefab in AttackData.swingVFX. The controller attaches it to the limb and releases it for you.
/// </summary>
public class FistStreak : MonoBehaviour
{
    [Header("Quad")]
    [Tooltip("The child quad. Leave empty to use the first renderer below this object.")]
    public Renderer quad;

    [Header("Speed to size (world units per second)")]
    [Tooltip("Below this limb speed nothing is shown.")]
    public float minSpeed = 2.5f;
    [Tooltip("At or above this limb speed the streak is at full length and thickness.")]
    public float fullSpeed = 9f;
    [Tooltip("Streak length at full speed (world units).")]
    public float maxLength = 2.0f;
    [Tooltip("Streak thickness at full speed (world units).")]
    public float maxThickness = 0.5f;
    [Range(0f, 1f)]
    [Tooltip("Thickness at low speed, as a fraction of Max Thickness.")]
    public float minThicknessFactor = 0.45f;
    [Tooltip("Pushes the pointed head slightly ahead of the limb.")]
    public float headOffset = 0.12f;
    [Range(0.2f, 1f)]
    [Tooltip("When the limb moves straight toward or away from the camera the streak gets this much shorter.")]
    public float foreshortenMin = 0.5f;

    [Header("Feel")]
    [Tooltip("How quickly the speed estimate reacts. Lower = smoother.")]
    public float speedResponse = 25f;
    [Tooltip("How quickly the streak direction follows the limb direction. Lower = smoother.")]
    public float directionResponse = 30f;

    [Header("Break-up after the strike (time based)")]
    [Range(0.3f, 0.9f)]
    [Tooltip("The streak freezes when the limb has slowed down to this fraction of its peak speed.")]
    public float freezeAtSpeedRatio = 0.6f;
    [Tooltip("Seconds for the tail to erode from nothing to completely gone.")]
    public float erodeTime = 0.30f;
    [Tooltip("Seconds before what is left starts to shrink away.")]
    public float fadeDelay = 0.22f;
    public float fadeTime = 0.12f;
    [Tooltip("Off = slows down with hit stop (freezes on the hit). On = ignores hit stop.")]
    public bool useUnscaledTime = false;

    private static readonly int ID_Reveal = Shader.PropertyToID("_Reveal");
    private static readonly int ID_Erode = Shader.PropertyToID("_Erode");
    private static readonly int ID_Fade = Shader.PropertyToID("_Fade");
    private static readonly int ID_Seed = Shader.PropertyToID("_Seed");
    private static Camera[] camBuffer = new Camera[8];

    private Transform limb;
    private MaterialPropertyBlock mpb;
    private Vector3 lastPos;
    private float smoothedSpeed;
    private Vector3 smoothedDir;
    private float peakSpeed;
    private float peakK;
    private bool hasPeak;
    private Vector3 peakPos;
    private Quaternion peakRot;
    private Vector3 peakScale;
    private bool released;
    private float releaseTime;
    private float flipY = 1f;
    private float seed;
    private float lastAngle;
    private Vector2 lastScreenDir = Vector2.right;

    public float ReleaseDuration
    {
        get { return Mathf.Max(erodeTime, fadeDelay + fadeTime); }
    }

    private void Awake()
    {
        limb = transform.parent;
        if (quad == null) quad = GetComponentInChildren<Renderer>();

        // We position the quad in world space ourselves, so we don't want to inherit the limb's scale or rotation
        transform.SetParent(null, true);

        mpb = new MaterialPropertyBlock();
        seed = Random.value;
        flipY = Random.value < 0.5f ? -1f : 1f;

        if (quad != null)
        {
            quad.enabled = false;
            SetShader(0f, 1f);
        }

        if (limb != null) lastPos = limb.position;
    }

    /// <summary>The controller calls this when the swing ends. Freezes the streak and lets it break up.</summary>
    public void Release()
    {
        if (released) return;
        released = true;
        releaseTime = 0f;

        if (quad != null && hasPeak)
        {
            quad.transform.SetPositionAndRotation(peakPos, peakRot);
            quad.transform.localScale = peakScale;
            quad.enabled = true;
            SetShader(0f, 1f);
        }
        else
        {
            Destroy(gameObject); // the limb never got fast enough to show anything
        }
    }

    /// <summary>The controller calls this when a hit lands: freeze right at the impact.</summary>
    public void Pulse()
    {
        Release();
    }

    private void LateUpdate()
    {
        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (released)
        {
            UpdateRelease(dt);
            return;
        }

        if (dt <= 0f || quad == null) return;
        if (limb == null)
        {
            Release();
            return;
        }

        Vector3 pos = limb.position;
        Vector3 vel = (pos - lastPos) / dt;
        lastPos = pos;

        float speed = vel.magnitude;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-speedResponse * dt));
        if (smoothedSpeed > peakSpeed) peakSpeed = smoothedSpeed;

        if (speed > minSpeed * 0.5f)
        {
            Vector3 d = vel / speed;
            smoothedDir = smoothedDir.sqrMagnitude < 0.0001f
                ? d
                : Vector3.Lerp(smoothedDir, d, 1f - Mathf.Exp(-directionResponse * dt));
            if (smoothedDir.sqrMagnitude > 1e-6f) smoothedDir.Normalize();
        }

        float k = Mathf.InverseLerp(minSpeed, fullSpeed, smoothedSpeed);

        Vector3 cPos;
        Quaternion cRot;
        Vector3 cScale;

        if (k > 0.02f && smoothedDir.sqrMagnitude > 0.01f && ComputePose(pos, k, out cPos, out cRot, out cScale))
        {
            quad.transform.SetPositionAndRotation(cPos, cRot);
            quad.transform.localScale = cScale;
            quad.enabled = true;

            // remember the pose at the strongest moment of the strike
            if (k >= peakK - 0.0001f)
            {
                peakK = k;
                peakPos = cPos;
                peakRot = cRot;
                peakScale = cScale;
                hasPeak = true;
            }
        }
        else
        {
            quad.enabled = false;
        }

        // the limb has peaked and is slowing down: freeze the streak here and let it break up
        if (hasPeak && peakK > 0.35f && smoothedSpeed < peakSpeed * freezeAtSpeedRatio)
        {
            Release();
        }
    }

    private bool ComputePose(Vector3 limbPos, float k, out Vector3 pos, out Quaternion rot, out Vector3 scale)
    {
        pos = limbPos;
        rot = Quaternion.identity;
        scale = Vector3.one;

        Camera cam = PickCamera(limbPos);
        if (cam == null) return false;

        Vector3 camRight = cam.transform.right;
        Vector3 camUp = cam.transform.up;

        // direction of the limb movement as seen on screen
        float sx = Vector3.Dot(smoothedDir, camRight);
        float sy = Vector3.Dot(smoothedDir, camUp);
        float proj = Mathf.Sqrt(sx * sx + sy * sy); // 1 = across the screen, 0 = straight at / away from the camera
        if (proj > 0.02f)
        {
            lastScreenDir = new Vector2(sx, sy) / proj;
            lastAngle = Mathf.Atan2(sy, sx) * Mathf.Rad2Deg;
        }

        float ease = k * k * (3f - 2f * k);
        float len = maxLength * ease * Mathf.Lerp(foreshortenMin, 1f, Mathf.Clamp01(proj));
        float thick = maxThickness * Mathf.Lerp(minThicknessFactor, 1f, ease);

        // the pointed head (at 98% of the texture) sits at the limb, the tail stretches backwards
        Vector3 dirWorld = camRight * lastScreenDir.x + camUp * lastScreenDir.y;
        Vector3 head = limbPos + dirWorld * headOffset;
        pos = head - dirWorld * (0.485f * len);

        rot = Quaternion.LookRotation(cam.transform.forward, camUp) * Quaternion.Euler(0f, 0f, lastAngle);
        scale = new Vector3(Mathf.Max(len, 0.001f), thick * flipY, 1f);
        return true;
    }

    private void UpdateRelease(float dt)
    {
        releaseTime += Mathf.Max(dt, 0f);

        float erode = Smooth01(Mathf.Clamp01(releaseTime / Mathf.Max(erodeTime, 0.01f)));
        float fade = 1f - Smooth01(Mathf.Clamp01((releaseTime - fadeDelay) / Mathf.Max(fadeTime, 0.01f)));
        SetShader(erode, fade);

        if (releaseTime >= ReleaseDuration) Destroy(gameObject);
    }

    private void SetShader(float erode, float fade)
    {
        if (quad == null) return;
        quad.GetPropertyBlock(mpb);
        mpb.SetFloat(ID_Reveal, 1f);
        mpb.SetFloat(ID_Erode, erode);
        mpb.SetFloat(ID_Fade, fade);
        mpb.SetFloat(ID_Seed, seed);
        quad.SetPropertyBlock(mpb);
    }

    private static float Smooth01(float x)
    {
        return x * x * (3f - 2f * x);
    }

    // The streak faces whichever camera is closest, so it works for the game camera and the skill-preview camera
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