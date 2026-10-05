using System.Collections;
using UnityEngine;

/// Can live on the Shield object itself (the same object as BeaconHealth).
/// It never deactivates any GameObject: when the dissolve finishes it only switches the shield MESH off,
/// so BeaconHealth, the hitbox and the colliders keep working (the core still has to be hit afterwards).
public class ShieldDissolve : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Every renderer that uses the shield shader (main shield + ripple). All of them get the Dissolve value.")]
    [SerializeField] Renderer[] renderers;
    [Tooltip("Renderers switched OFF when the dissolve finishes and back ON by Restore. Put the MAIN shield mesh here, NOT the ripple (ShieldHit controls the ripple).")]
    [SerializeField] Renderer[] hideWhenDone;

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

    /// Shield dissolves top to bottom, then the shield mesh is switched off.
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
        SetHidden(false);
        Play(1f, 0f, false);
    }

    void Play(float from, float to, bool hideWhenFinished)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(from, to, hideWhenFinished));
    }

    IEnumerator Run(float from, float to, bool hideWhenFinished)
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            SetDissolve(Mathf.Lerp(from, to, curve.Evaluate(t)));
            yield return null;
        }
        SetDissolve(to);
        if (hideWhenFinished) SetHidden(true);
        routine = null;
    }

    void SetHidden(bool hidden)
    {
        if (hideWhenDone == null) return;
        foreach (var r in hideWhenDone)
            if (r != null) r.enabled = !hidden;
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