using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on any object in your Loading scene. It shows the real loading
/// progress from SceneTransitionManager. Every field is optional, so assign
/// only what your loading screen has.
/// </summary>
public class LoadingScreenUI : MonoBehaviour
{
    [Header("Progress (use either one)")]
    [SerializeField] private Slider progressBar;
    [Tooltip("An Image with Image Type = Filled, Fill Method = Horizontal.")]
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text percentText;

    [Header("Tips (optional)")]
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private string[] tips;
    [SerializeField] private float tipInterval = 4f;

    private float tipTimer;
    private int tipIndex = -1;

    private void Start()
    {
        ShowNextTip();
    }

    private void Update()
    {
        float p = SceneTransitionManager.Progress;

        if (progressBar != null) progressBar.value = p;
        if (progressFill != null) progressFill.fillAmount = p;
        if (percentText != null) percentText.text = Mathf.RoundToInt(p * 100f) + "%";

        if (tips != null && tips.Length > 1)
        {
            tipTimer += Time.unscaledDeltaTime;
            if (tipTimer >= tipInterval) ShowNextTip();
        }
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