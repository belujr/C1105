using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One slanted objective row: [accent bar] [optional icon] [text] [optional counter].
/// Built entirely in code by TutorialObjectiveHUD, so there is no prefab to wire up.
/// All animation uses unscaled time.
/// </summary>
[RequireComponent(typeof(LayoutElement))]
public class TutorialObjectiveRow : MonoBehaviour
{
    private TutorialHUDStyle style;
    private LayoutElement layout;
    private RectTransform visual;
    private CanvasGroup group;

    private Image background, accent, icon, halo;
    private TMP_Text label, counter;

    private Graphic[] allGraphics;
    private Graphic[] glowGraphics;
    private Color[] glowFrom;

    private static Sprite haloSprite;

    // ------------------------------------------------------------------ Creation

    public static TutorialObjectiveRow Create(Transform parent, TutorialMissionData data, TutorialHUDStyle style)
    {
        var go = new GameObject("Objective_" + data.name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var row = go.AddComponent<TutorialObjectiveRow>(); // RequireComponent adds the LayoutElement
        row.Build(data, style);
        return row;
    }

    private void Build(TutorialMissionData data, TutorialHUDStyle s)
    {
        style = s;
        layout = GetComponent<LayoutElement>();
        layout.preferredWidth = s.rowSize.x;
        layout.preferredHeight = s.rowSize.y;

        // "Visual" is what animates. The root only holds the layout size, so sliding never fights the layout group.
        visual = NewRect("Visual", transform);
        Stretch(visual, 0f, 0f);
        group = visual.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        bool hasIcon = data.icon != null;
        float left = s.accentWidth + s.padding;

        // Background panel (sheared into a parallelogram)
        background = NewImage("Background", visual, s.backgroundSprite, s.backgroundColor);
        Stretch(background.rectTransform, 0f, 0f);
        background.gameObject.AddComponent<SlantedGraphic>().Slant = s.slant;

        // Accent bar on the left edge
        accent = NewImage("Accent", visual, null, s.accentColor);
        var ar = accent.rectTransform;
        ar.anchorMin = new Vector2(0f, 0f);
        ar.anchorMax = new Vector2(0f, 1f);
        ar.pivot = new Vector2(0f, 0.5f);
        ar.sizeDelta = new Vector2(s.accentWidth, 0f);
        ar.anchoredPosition = Vector2.zero;
        accent.gameObject.AddComponent<SlantedGraphic>().Slant = s.slant;

        var glowList = new List<Graphic> { accent };
        var fromList = new List<Color> { s.accentColor };

        if (hasIcon)
        {
            // Halo sits BEHIND the icon, so it is created first
            halo = NewImage("Halo", visual, GetHaloSprite(), new Color(s.glowColor.r, s.glowColor.g, s.glowColor.b, 0f));
            var hr = halo.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 0.5f);
            hr.pivot = new Vector2(0.5f, 0.5f);
            hr.sizeDelta = Vector2.one * s.iconSize * 2.6f;
            hr.anchoredPosition = new Vector2(left + s.iconSize * 0.5f, 0f);

            icon = NewImage("Icon", visual, data.icon, Color.white);
            icon.preserveAspect = true;
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
            ir.pivot = new Vector2(0f, 0.5f);
            ir.sizeDelta = Vector2.one * s.iconSize;
            ir.anchoredPosition = new Vector2(left, 0f);

            glowList.Add(icon);
            fromList.Add(Color.white);
            left += s.iconSize + s.padding;
        }

        // Counter (right side, only for objectives needing more than one)
        counter = NewText("Counter", visual, s, false);
        counter.alignment = TextAlignmentOptions.MidlineRight;
        var cr = counter.rectTransform;
        cr.anchorMin = new Vector2(1f, 0f);
        cr.anchorMax = new Vector2(1f, 1f);
        cr.pivot = new Vector2(1f, 0.5f);
        cr.sizeDelta = new Vector2(90f, 0f);
        cr.anchoredPosition = new Vector2(-s.padding - s.slant * 0.5f, 0f);
        counter.color = s.textColor;
        counter.text = string.Empty;
        counter.gameObject.SetActive(data.requiredCount > 1);

        // Label (long text wraps; the row grows to fit it, but is never smaller than the style's row height)
        label = NewText("Label", visual, s, true);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        float rightPad = data.requiredCount > 1 ? 90f + s.padding : s.padding;
        Stretch(label.rectTransform, left, rightPad);
        label.color = s.textColor;
        label.text = data.text;

        float labelWidth = Mathf.Max(50f, s.rowSize.x - left - rightPad);
        float textHeight = label.GetPreferredValues(data.text, labelWidth, 0f).y;
        layout.preferredHeight = Mathf.Max(s.rowSize.y, textHeight + s.padding);

        // No icon? Then the text itself takes the yellow glow.
        if (!hasIcon)
        {
            glowList.Add(label);
            fromList.Add(s.textColor);
        }

        glowGraphics = glowList.ToArray();
        glowFrom = fromList.ToArray();

        var all = new List<Graphic> { background, accent, label, counter };
        if (halo != null) all.Add(halo);
        if (icon != null) all.Add(icon);
        allGraphics = all.ToArray();

        SetProgress(0, data.requiredCount);
    }

    // ------------------------------------------------------------------ Public API

    public void SetProgress(int current, int required)
    {
        if (counter != null && required > 1) counter.text = current + "/" + required;
    }

    /// <summary>Slides in from the left with a little overshoot.</summary>
    public IEnumerator PlayIn()
    {
        float d = Mathf.Max(0.01f, style.popInTime);
        for (float t = 0f; t < d; t += Time.unscaledDeltaTime)
        {
            float k = t / d;
            visual.anchoredPosition = new Vector2(Mathf.LerpUnclamped(-style.slideDistance, 0f, EaseOutBack(k)), 0f);
            group.alpha = Mathf.Clamp01(k * 2.2f);
            yield return null;
        }
        visual.anchoredPosition = Vector2.zero;
        group.alpha = 1f;
    }

    /// <summary>Icon (or text, if there is no icon) and accent bar glow bright yellow, the halo pulses.</summary>
    public IEnumerator PlayGlow()
    {
        float d = Mathf.Max(0.01f, style.glowInTime);
        for (float t = 0f; t < d; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / d);
            ApplyGlow(k, 1f);
            visual.localScale = Vector3.one * (1f + style.glowPunch * Mathf.Sin(k * Mathf.PI));
            yield return null;
        }
        ApplyGlow(1f, 1f);
        visual.localScale = Vector3.one;

        for (float t = 0f; t < style.glowHoldTime; t += Time.unscaledDeltaTime)
        {
            ApplyGlow(1f, 0.82f + 0.18f * Mathf.Sin(t * 22f)); // soft pulse while it holds
            yield return null;
        }
        ApplyGlow(1f, 1f);
    }

    /// <summary>Everything darkens to black while the row fades out, then the row collapses and removes itself.</summary>
    public IEnumerator PlayFadeOutAndRemove()
    {
        var start = new Color[allGraphics.Length];
        for (int i = 0; i < allGraphics.Length; i++) start[i] = allGraphics[i].color;

        float d = Mathf.Max(0.01f, style.fadeToBlackTime);
        for (float t = 0f; t < d; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / d);
            for (int i = 0; i < allGraphics.Length; i++)
            {
                Color c = start[i];
                allGraphics[i].color = new Color(Mathf.Lerp(c.r, 0f, k), Mathf.Lerp(c.g, 0f, k), Mathf.Lerp(c.b, 0f, k), c.a);
            }
            group.alpha = 1f - k;
            visual.anchoredPosition = new Vector2(Mathf.Lerp(0f, -style.slideDistance * 0.2f, k), 0f);
            yield return null;
        }
        group.alpha = 0f;

        // Collapse so any row below slides up (matters when rows are shown while another is fading).
        float h = layout.preferredHeight;
        float cd = Mathf.Max(0.01f, style.collapseTime);
        for (float t = 0f; t < cd; t += Time.unscaledDeltaTime)
        {
            layout.preferredHeight = Mathf.Lerp(h, 0f, Mathf.SmoothStep(0f, 1f, t / cd));
            yield return null;
        }

        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ Helpers

    private void ApplyGlow(float k, float haloPulse)
    {
        Color glow = style.glowColor;
        for (int i = 0; i < glowGraphics.Length; i++)
        {
            Color c = Color.Lerp(glowFrom[i], glow, k);
            c.a = glowFrom[i].a;
            glowGraphics[i].color = c;
        }
        if (halo != null)
        {
            halo.color = new Color(glow.r, glow.g, glow.b, k * style.haloAlpha * haloPulse);
            halo.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.15f, k);
        }
    }

    private static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var rt = NewRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TMP_Text NewText(string name, Transform parent, TutorialHUDStyle s, bool wrap)
    {
        var rt = NewRect(name, parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (s.font != null) tmp.font = s.font;
        tmp.fontSize = s.fontSize;
        tmp.fontStyle = s.italic ? FontStyles.Italic : FontStyles.Normal;
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Stretch(RectTransform rt, float left, float right)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, 0f);
        rt.offsetMax = new Vector2(-right, 0f);
    }

    /// <summary>Soft white radial gradient used for the glow halo, generated once.</summary>
    private static Sprite GetHaloSprite()
    {
        if (haloSprite != null) return haloSprite;

        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r;
                float dy = (y + 0.5f - r) / r;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                a *= a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        haloSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return haloSprite;
    }
}