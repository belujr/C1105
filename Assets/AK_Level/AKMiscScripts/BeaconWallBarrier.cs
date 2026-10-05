using System.Collections;
using UnityEngine;

/// Controls the fire wall that locks the player into the beacon arena.
/// BuildWall()  -> hook to BeaconHealth > On Beacon Activated
/// Disappear()  -> hook to BeaconHealth > On Beacon Destroyed
/// The wall pieces (Quads with the FireWall material and a Box Collider) are children of this object.
/// While the wall object is active, its colliders block the player. When it is switched off, they stop blocking.
public class BeaconWallBarrier : MonoBehaviour
{
    public enum SweepOrigin { Middle, Left, Right }
    public enum VanishStyle
    {
        RetreatToStart,     // plays the appear animation backwards (fire pulls back to where it started)
        SpreadFromMiddle,   // a gap opens in the middle and spreads to both ends
        SpreadFromLeft,     // gap opens at the left end and sweeps right
        SpreadFromRight     // gap opens at the right end and sweeps left
    }

    [Header("Wall Configuration")]
    [Tooltip("The wall GameObject to control. If left empty, this script controls its own GameObject.")]
    [SerializeField] private GameObject wallObject;
    [Tooltip("Leave empty to auto-find every MeshRenderer under the wall.")]
    [SerializeField] private Renderer[] fireRenderers;

    [Header("Appear")]
    [Tooltip("Where the fire starts when the wall appears. Middle = spreads to both ends.")]
    [SerializeField] private SweepOrigin appearFrom = SweepOrigin.Middle;
    [SerializeField] private float appearTime = 1.0f;

    [Header("Disappear")]
    [SerializeField] private VanishStyle disappearStyle = VanishStyle.SpreadFromMiddle;
    [SerializeField] private float disappearTime = 0.8f;

    [Header("Multiple Wall Pieces")]
    [Tooltip("Seconds of delay between each wall piece, in hierarchy order. 0 = all pieces animate together.")]
    [SerializeField] private float staggerPerPiece = 0f;

    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int CenterId = Shader.PropertyToID("_RevealCenter");
    private static readonly int InvertId = Shader.PropertyToID("_RevealInvert");

    private MaterialPropertyBlock block;
    private Coroutine routine;
    private bool buildRequested;

    private void Awake()
    {
        EnsureInit();

        // Start hidden, unless BuildWall is what woke this object up
        if (!buildRequested) wallObject.SetActive(false);
    }

    private void EnsureInit()
    {
        if (wallObject == null) wallObject = gameObject;
        if (fireRenderers == null || fireRenderers.Length == 0)
            fireRenderers = wallObject.GetComponentsInChildren<MeshRenderer>(true);
        if (block == null) block = new MaterialPropertyBlock();
    }

    [ContextMenu("Test/Build Wall")]
    public void BuildWall()
    {
        EnsureInit();
        buildRequested = true;
        wallObject.SetActive(true);
        StartSweep(0f, 1f, appearTime, CenterOf(appearFrom), false, false);
    }

    [ContextMenu("Test/Disappear")]
    public void Disappear()
    {
        EnsureInit();
        if (!wallObject.activeSelf) return;

        switch (disappearStyle)
        {
            case VanishStyle.RetreatToStart:
                StartSweep(1f, 0f, disappearTime, CenterOf(appearFrom), false, true); break;
            case VanishStyle.SpreadFromMiddle:
                StartSweep(1f, 0f, disappearTime, 0.5f, true, true); break;
            case VanishStyle.SpreadFromLeft:
                StartSweep(1f, 0f, disappearTime, 0f, true, true); break;
            default:
                StartSweep(1f, 0f, disappearTime, 1f, true, true); break;
        }
    }

    private static float CenterOf(SweepOrigin origin)
    {
        switch (origin)
        {
            case SweepOrigin.Left: return 0f;
            case SweepOrigin.Right: return 1f;
            default: return 0.5f;
        }
    }

    private void StartSweep(float from, float to, float time, float center, bool invert, bool deactivateAtEnd)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Run(from, to, time, center, invert, deactivateAtEnd));
    }

    private IEnumerator Run(float from, float to, float time, float center, bool invert, bool deactivateAtEnd)
    {
        time = Mathf.Max(time, 0.01f);
        int n = fireRenderers.Length;
        float total = time + staggerPerPiece * Mathf.Max(0, n - 1);

        for (float elapsed = 0f; elapsed < total; elapsed += Time.deltaTime)
        {
            for (int i = 0; i < n; i++)
            {
                float k = Mathf.Clamp01((elapsed - i * staggerPerPiece) / time);
                Apply(fireRenderers[i], Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k)), center, invert);
            }
            yield return null;
        }

        for (int i = 0; i < n; i++) Apply(fireRenderers[i], to, center, invert);

        if (deactivateAtEnd) wallObject.SetActive(false);
        routine = null;
    }

    private void Apply(Renderer r, float reveal, float center, bool invert)
    {
        if (r == null) return;
        r.GetPropertyBlock(block);
        block.SetFloat(RevealId, reveal);
        block.SetFloat(CenterId, center);
        block.SetFloat(InvertId, invert ? 1f : 0f);
        r.SetPropertyBlock(block);
    }
}