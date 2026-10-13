using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Put this on the soul counter TMP_Text in the hub AND in every level.
/// It subscribes while enabled and only touches the text when the value actually changes,
/// so there is no Update loop, no Find calls, no tags and no per-frame canvas rebuilds.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class SoulCounterUI : MonoBehaviour
{
    private TMP_Text label;
    private int lastShown = -1;

    private void Awake() => label = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        SoulManager.OnSoulsChanged += Refresh;
        Refresh(SoulManager.Instance != null ? SoulManager.Instance.TotalSouls : 0);
    }

    private void OnDisable() => SoulManager.OnSoulsChanged -= Refresh;

    private void Refresh(int total)
    {
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