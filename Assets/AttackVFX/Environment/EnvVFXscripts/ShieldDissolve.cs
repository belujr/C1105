using System.Collections;
using UnityEngine;

/// Put this on an object that stays ACTIVE (e.g. the beacon root), NOT on the shield sphere itself,
/// because the shield sphere gets switched off when the dissolve finishes.
public class ShieldDissolve : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Shield sphere renderer + ripple sphere renderer (anything using the shield shader graph).")]
    [SerializeField] Renderer[] renderers;
    [Tooltip("Object switched off when the dissolve finishes (usually the shield sphere that BeaconWorldShieldVisual scales).")]
    [SerializeField] GameObject visualRoot;

    [Header("Timing")]
    [SerializeField] float duration = 1.2f;
    [SerializeField] AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // Must match the Reference name of the "Dissolve" property in Shader Graph (Graph Inspector).
    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

    MaterialPropertyBlock block;
    Coroutine routine;

    public bool IsDissolved { get; private set; }

    void Awake()
    {
        SetDissolve(0f);
    }

    /// Shield dissolves top to bottom, then the visual root is switched off.
    public void Break()
    {
        if (IsDissolved) return;
        IsDissolved = true;
        Play(0f, 1f, true);
    }

    /// Shield comes back (handy for testing / respawning beacons).
    public void Restore()
    {
        IsDissolved = false;
        if (visualRoot) visualRoot.SetActive(true);
        Play(1f, 0f, false);
    }

    void Play(float from, float to, bool hideWhenDone)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(from, to, hideWhenDone));
    }

    IEnumerator Run(float from, float to, bool hideWhenDone)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            SetDissolve(Mathf.Lerp(from, to, curve.Evaluate(t)));
            yield return null;
        }
        SetDissolve(to);
        if (hideWhenDone && visualRoot) visualRoot.SetActive(false);
        routine = null;
    }

    /// Public so the custom inspector slider can scrub it (works in edit mode too).
    public void SetDissolve(float value)
    {
        if (renderers == null) return;
        if (block == null) block = new MaterialPropertyBlock();

        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetFloat(DissolveId, value);
            r.SetPropertyBlock(block);
        }
    }
}