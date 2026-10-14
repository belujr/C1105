using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on any object in your Loading scene. It shows the real loading
/// progress from SceneTransitionManager. Every field is optional, so assign
/// only what your loading screen has.
/// (The random wallpaper is handled separately by LoadingWallpaper.)
/// </summary>
public class LoadingScreenUI : MonoBehaviour
{
    [Header("Progress (use either one)")]
    [SerializeField] private Slider progressBar;
    [Tooltip("An Image that is a child of your bar background. With Fill From Center on, it grows outward from the middle. With it off, it must be Image Type = Filled, Fill Method = Horizontal.")]
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text percentText;
    [Tooltip("The bar grows outward from the middle to both edges instead of sweeping from one side.")]
    [SerializeField] private bool fillFromCenter = true;

    [Header("Tips (optional)")]
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private string[] tips;
    [SerializeField] private float tipInterval = 4f;

    private float tipTimer;
    private int tipIndex = -1;

    private float currentProgress;

    private void Awake()
    {
        // A Filled image would clip the bar, so show it whole and resize it instead.
        if (fillFromCenter && progressFill != null && progressFill.type == Image.Type.Filled)
            progressFill.fillAmount = 1f;
    }

    private void Start()
    {
        ShowNextTip();
    }

    private void Update()
    {
        currentProgress = Mathf.Clamp01(SceneTransitionManager.Progress);
        float p = currentProgress;

        if (progressBar != null) progressBar.value = p;
        if (progressFill != null && !fillFromCenter) progressFill.fillAmount = p;
        if (percentText != null) percentText.text = Mathf.RoundToInt(p * 100f) + "%";

        if (tips != null && tips.Length > 1)
        {
            tipTimer += Time.unscaledDeltaTime;
            if (tipTimer >= tipInterval) ShowNextTip();
        }
    }

    // Runs after the Slider has updated itself, so our center-out sizing wins.
    private void LateUpdate()
    {
        if (!fillFromCenter) return;

        if (progressFill != null)
            ApplyCenterFill(progressFill.rectTransform, currentProgress);

        if (progressBar != null && progressBar.fillRect != null)
            ApplyCenterFill(progressBar.fillRect, currentProgress);
    }

    // Anchors the bar around the horizontal middle of its parent: 0% = a point
    // in the center, 100% = the full width.
    private static void ApplyCenterFill(RectTransform rt, float p)
    {
        Vector2 min = rt.anchorMin;
        Vector2 max = rt.anchorMax;
        min.x = 0.5f - p * 0.5f;
        max.x = 0.5f + p * 0.5f;
        rt.anchorMin = min;
        rt.anchorMax = max;

        rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
        rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
    }

    private void ShowNextTip()
    {
        tipTimer = 0f;
        if (tipText == null || tips == null || tips.Length == 0) return;

        // pick a different tip each time
        int next;
        do { next = Random.Range(0, tips.Length); }
        while (tips.Length > 1 && next == tipIndex);

        tipIndex = next;
        tipText.text = tips[tipIndex];
    }
}