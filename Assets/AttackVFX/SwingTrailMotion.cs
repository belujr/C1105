using UnityEngine;

/// <summary>
/// Makes a swing trail follow the limb's speed:
///  - slow wind-up  = tiny / invisible trail
///  - fast strike   = full-length, full-width trail
///  - limb slows    = the trail shrinks back quickly, so it fades out together with the leg or arm
///
/// The controller adds this automatically to any swing prefab that contains a TrailRenderer.
/// You can also add it to the prefab yourself to give that attack its own numbers.
/// </summary>
public class SwingTrailMotion : MonoBehaviour
{
    [Header("Speed range (world units per second)")]
    [Tooltip("At or below this speed the trail is at its smallest.")]
    public float minSpeed = 2f;
    [Tooltip("At or above this speed the trail is at full length and width.")]
    public float fullSpeed = 8f;

    [Header("Trail length (seconds)")]
    [Tooltip("Trail Time when the limb is slow. Keep this small so the tail vanishes quickly.")]
    public float minTime = 0.02f;
    [Tooltip("Trail Time when the limb is fast. 0 = use the Time already set on the Trail Renderer.")]
    public float maxTime = 0f;

    [Header("Trail width")]
    [Range(0f, 1f)]
    [Tooltip("Width at slow speed, as a fraction of the width set on the Trail Renderer.")]
    public float minWidthFactor = 0.3f;

    [Header("Feel")]
    [Tooltip("How quickly the trail reacts to speed changes. Higher = snappier, lower = smoother and a bit more lag.")]
    public float response = 18f;

    private TrailRenderer[] trails;
    private float[] baseWidths;
    private float[] baseTimes;
    private Vector3 lastPos;
    private float smoothedSpeed;

    private void Awake()
    {
        trails = GetComponentsInChildren<TrailRenderer>();
        baseWidths = new float[trails.Length];
        baseTimes = new float[trails.Length];

        for (int i = 0; i < trails.Length; i++)
        {
            baseWidths[i] = trails[i].widthMultiplier;
            baseTimes[i] = maxTime > 0f ? maxTime : trails[i].time;
        }
    }

    private void OnEnable()
    {
        lastPos = transform.position;
        smoothedSpeed = 0f;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        float instantSpeed = (transform.position - lastPos).magnitude / dt;
        lastPos = transform.position;

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, instantSpeed, 1f - Mathf.Exp(-response * dt));

        float k = Mathf.InverseLerp(minSpeed, fullSpeed, smoothedSpeed);

        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i] == null) continue;
            trails[i].time = Mathf.Lerp(minTime, baseTimes[i], k);
            trails[i].widthMultiplier = baseWidths[i] * Mathf.Lerp(minWidthFactor, 1f, k);
        }
    }
}