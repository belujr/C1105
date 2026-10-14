using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Soul counter label. Put one in the hub and one INSIDE the level prefab.
/// It subscribes while enabled and only touches the text when the value actually changes,
/// so there is no Update loop, no Find calls, no tags and no per-frame canvas rebuilds.
///
/// Where it shows is decided by <see cref="placement"/>, not by scene names:
///   Hub   -> hidden until the player has finished run 0 (RunState.Current >= hubMinRun).
///   Level -> always shown. Because it lives inside the level prefab, it only exists once the
///            chamber is picked and the level spawns, so the map screen never has a counter.
/// Souls are collected and saved either way; this only controls the display.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class SoulCounterUI : MonoBehaviour
{
    public enum Placement { Hub, Level }

    [Header("Visibility")]
    [Tooltip("Hub: hidden during run 0. Level: always visible (put this inside the level prefab).")]
    [SerializeField] private Placement placement = Placement.Hub;

    [Tooltip("Hub only: first run on which the counter appears (1 = hidden during run 0).")]
    [SerializeField, Min(0)] private int hubMinRun = 1;

    [Tooltip("Optional: sibling/child objects (soul icon, background) to show/hide together with the number. " +
             "Do NOT put this object or its parents here.")]
    [SerializeField] private GameObject[] alsoToggle;

    private TMP_Text label;
    private int lastShown = -1;
    private bool visible;

    private void Awake() => label = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        SoulManager.OnSoulsChanged += Refresh;
        ApplyVisibility();
    }

    // Checked again in Start: HubNarrativeManager's optional save reset runs in its Awake,
    // which can come after our OnEnable. By Start the run number is final.
    private void Start() => ApplyVisibility();

    private void OnDisable() => SoulManager.OnSoulsChanged -= Refresh;

    private void ApplyVisibility()
    {
        visible = placement == Placement.Level || RunState.Current >= hubMinRun;

        label.enabled = visible;
        if (alsoToggle != null)
        {
            foreach (GameObject go in alsoToggle)
                if (go != null && go != gameObject) go.SetActive(visible);
        }

        // Sync the number the moment it becomes visible.
        if (visible)
        {
            lastShown = -1;
            Refresh(SoulManager.Instance != null ? SoulManager.Instance.TotalSouls : 0);
        }
    }

    private void Refresh(int total)
    {
        if (!visible) return;           // hidden: nothing to draw, resync happens on show
        if (total == lastShown) return; // skip redundant text/mesh rebuilds
        lastShown = total;
        label.text = total.ToString("N0", CultureInfo.InvariantCulture);
    }

#if UNITY_EDITOR
    // A pure display label doesn't need to be a raycast target (cheaper UI raycasting).
    private void Reset()
    {
        if (TryGetComponent(out TMP_Text t)) t.raycastTarget = false;
    }
#endif
}