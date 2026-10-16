using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Every look-and-feel value for the objective rows. Tweak these on the TutorialManager's HUD component.</summary>
[System.Serializable]
public class TutorialHUDStyle
{
    [Header("Layout")]
    public Vector2 rowSize = new Vector2(560f, 72f);
    public float iconSize = 44f;
    [Tooltip("Gap between accent bar, icon and text.")]
    public float padding = 18f;
    public float accentWidth = 8f;
    [Tooltip("Parallelogram shear in pixels. Positive: / lean. Negative: \\ lean. 0 = plain rectangle.")]
    public float slant = 28f;

    [Header("Look")]
    [Tooltip("OPTIONAL panel sprite (leave empty for a flat panel). It is sheared by the Slant value.")]
    public Sprite backgroundSprite;
    public Color backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.82f);
    public Color accentColor = new Color(0.9f, 0.9f, 0.95f, 1f);
    public Color textColor = Color.white;
    [Tooltip("OPTIONAL. Leave empty for the TextMeshPro default font.")]
    public TMP_FontAsset font;
    public float fontSize = 30f;
    public bool italic = true;

    [Header("Completion glow")]
    public Color glowColor = new Color(1f, 0.9f, 0.15f, 1f);
    [Range(0f, 1f)] public float haloAlpha = 0.85f;
    [Tooltip("Tiny scale punch while the glow flares up.")]
    public float glowPunch = 0.05f;

    [Header("Timing (seconds, unscaled)")]
    public float popInTime = 0.45f;
    public float slideDistance = 120f;
    public float glowInTime = 0.25f;
    public float glowHoldTime = 0.55f;
    public float fadeToBlackTime = 0.6f;
    public float collapseTime = 0.2f;
}

/// <summary>
/// Builds its own screen-space canvas (top-left, rows stacked downward) and spawns objective rows into it.
/// No prefab, no manual canvas: it lives on the same GameObject as TutorialManager.
/// </summary>
public class TutorialObjectiveHUD : MonoBehaviour
{
    [SerializeField] private TutorialHUDStyle style = new TutorialHUDStyle();

    [Header("Placement (reference 1920x1080)")]
    [Tooltip("Distance from the top-left corner of the screen.")]
    [SerializeField] private Vector2 margin = new Vector2(80f, 80f);
    [SerializeField] private float rowSpacing = 12f;
    [Tooltip("Below the scene-transition fade, above normal HUD.")]
    [SerializeField] private int sortingOrder = 50;

    private Canvas canvas;
    private CanvasGroup canvasGroup;
    private RectTransform container;
    private bool visible = true;

    private const float VisibilityFadeTime = 0.2f;

    private void Awake() => Build();

    private void Update()
    {
        // Smooth show/hide (used to step aside while a dialogue is open). Unscaled, like the rows.
        if (canvasGroup == null) return;
        float target = visible ? 1f : 0f;
        if (!Mathf.Approximately(canvasGroup.alpha, target))
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, target, Time.unscaledDeltaTime / VisibilityFadeTime);
    }

    /// <summary>Fades the whole objective list in or out without touching the rows' own animations.</summary>
    public void SetVisible(bool value) => visible = value;

    public TutorialObjectiveRow Show(TutorialMissionData data)
    {
        canvas.enabled = true;
        return TutorialObjectiveRow.Create(container, data, style);
    }

    /// <summary>Removes every row immediately (used when a level sequence is cancelled).</summary>
    public void Clear()
    {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--)
            Destroy(container.GetChild(i).gameObject);
    }

    public void Hide()
    {
        if (canvas != null) canvas.enabled = false;
    }

    private void Build()
    {
        var canvasGO = new GameObject("TutorialObjectiveCanvas");
        canvasGO.transform.SetParent(transform, false);

        canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        canvasGroup = canvasGO.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        var listGO = new GameObject("Objectives", typeof(RectTransform));
        listGO.transform.SetParent(canvasGO.transform, false);
        container = (RectTransform)listGO.transform;
        container.anchorMin = container.anchorMax = new Vector2(0f, 1f);
        container.pivot = new Vector2(0f, 1f);
        container.anchoredPosition = new Vector2(margin.x, -margin.y);
        container.sizeDelta = new Vector2(style.rowSize.x, 0f);

        var layout = listGO.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = rowSpacing;

        var fitter = listGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        canvas.enabled = false; // nothing on screen until the first objective
    }
}