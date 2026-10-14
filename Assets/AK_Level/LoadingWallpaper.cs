using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a random wallpaper behind the loading screen. Put it on a full-screen
/// Raw Image that is the FIRST child of the loading Canvas (so it draws behind
/// the progress bar and text). A new wallpaper is picked every time the
/// loading scene loads.
///
/// The wallpaper is drawn by the Canvas (Screen Space - Overlay), not by a camera.
/// Opacity fades the wallpaper into whatever is behind the Canvas.
///
/// If you can't see the wallpaper, press Play and read the Console: this script
/// prints a warning for each common cause.
/// </summary>
[RequireComponent(typeof(RawImage), typeof(AspectRatioFitter))]
public class LoadingWallpaper : MonoBehaviour
{
    [Tooltip("Drag your wallpaper images here (Texture Type: Default is fine).")]
    [SerializeField] private Texture2D[] wallpapers;

    [Range(0f, 1f)]
    [Tooltip("0 = invisible, 1 = fully opaque wallpaper.")]
    [SerializeField] private float opacity = 0.6f;

    [Tooltip("Never show the same wallpaper twice in a row.")]
    [SerializeField] private bool avoidRepeat = true;

    [Tooltip("Sets the root Canvas to Screen Space - Overlay so the wallpaper is drawn by the Canvas and not by a camera.")]
    [SerializeField] private bool forceOverlayCanvas = true;

    [Tooltip("Print a warning to the Console when something is likely hiding the wallpaper.")]
    [SerializeField] private bool logDiagnostics = true;

    // static, so it survives the loading scene being unloaded and loaded again
    private static int lastIndex = -1;

    private RawImage image;
    private Texture2D shown;

    private void Awake()
    {
        image = GetComponent<RawImage>();
        image.raycastTarget = false;

        if (forceOverlayCanvas) ForceOverlay();

        Apply(PickIndex());

        if (logDiagnostics) StartCoroutine(Diagnose());
    }

    // Live preview while editing (not in Play mode): shows the first wallpaper
    // at the chosen opacity so you can see it without pressing Play.
    private void OnValidate()
    {
        if (Application.isPlaying) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= EditorPreview;
        UnityEditor.EditorApplication.delayCall += EditorPreview;
#endif
    }

#if UNITY_EDITOR
    private void EditorPreview()
    {
        if (this == null) return; // destroyed before the delayed call ran

        var img = GetComponent<RawImage>();
        if (img == null) return;

        Texture2D first = null;
        if (wallpapers != null)
        {
            for (int i = 0; i < wallpapers.Length; i++)
            {
                if (wallpapers[i] != null) { first = wallpapers[i]; break; }
            }
        }

        img.enabled = true;
        if (first != null) ApplyTexture(img, first);
        else SetOpacity(img);
    }
#endif

    private void ForceOverlay()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        Canvas root = canvas.rootCanvas;
        if (root.renderMode != RenderMode.ScreenSpaceOverlay)
            root.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    private int PickIndex()
    {
        if (wallpapers == null || wallpapers.Length == 0) return -1;

        // only pick from slots that actually have a texture
        var valid = new List<int>();
        for (int i = 0; i < wallpapers.Length; i++)
            if (wallpapers[i] != null) valid.Add(i);

        if (valid.Count == 0) return -1;

        int index = valid[Random.Range(0, valid.Count)];

        if (avoidRepeat && valid.Count > 1)
        {
            while (index == lastIndex) index = valid[Random.Range(0, valid.Count)];
        }

        lastIndex = index;
        return index;
    }

    private void Apply(int index)
    {
        // No wallpapers assigned -> nothing to draw
        if (index < 0)
        {
            image.enabled = false;
            return;
        }

        shown = wallpapers[index];
        image.enabled = true;
        ApplyTexture(image, shown);
    }

    private void ApplyTexture(RawImage img, Texture2D tex)
    {
        img.texture = tex;
        SetOpacity(img);

        // Cover the whole screen without stretching (crops the edges if the ratio differs)
        var fitter = img.GetComponent<AspectRatioFitter>();
        if (fitter != null)
        {
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)tex.width / tex.height;
        }
    }

    private void SetOpacity(RawImage img)
    {
        Color c = Color.white;
        c.a = opacity;
        img.color = c;
    }

    // ---------------------------------------------------------------- diagnostics

    private void Warn(string message)
    {
        Debug.LogWarning("[LoadingWallpaper] " + message, this);
    }

    private IEnumerator Diagnose()
    {
        yield return null; // let the layout system run first

        bool anyWarning = false;

        if (shown == null)
        {
            Warn("No wallpaper texture is assigned. Drag your images into the 'Wallpapers' list.");
            anyWarning = true;
        }

        if (opacity <= 0.01f)
        {
            Warn("Opacity is 0, so the wallpaper is invisible. Raise the 'Opacity' slider.");
            anyWarning = true;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Warn("This object is not under a Canvas, so nothing will be drawn. Make it a child of your loading Canvas.");
            anyWarning = true;
        }
        else
        {
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
            {
                Warn("Canvas is 'Screen Space - Camera' but has no Render Camera assigned.");
                anyWarning = true;
            }
            else if (canvas.renderMode == RenderMode.WorldSpace)
            {
                Warn("Canvas is 'World Space', so it may be off-screen. Use 'Screen Space - Overlay'.");
                anyWarning = true;
            }
        }

        CanvasGroup group = GetComponentInParent<CanvasGroup>();
        if (group != null && group.alpha <= 0.01f)
        {
            Warn("A parent CanvasGroup on '" + group.name + "' has Alpha 0, which hides everything under it.");
            anyWarning = true;
        }

        RectTransform rt = (RectTransform)transform;
        if (rt.rect.width < 1f || rt.rect.height < 1f)
        {
            Warn("The wallpaper has zero size. Its parent must be the Canvas (or a stretched, full-screen panel).");
            anyWarning = true;
        }

        // Anything drawn AFTER us (lower in the Hierarchy) in the same parent is drawn on top.
        Transform parent = transform.parent;
        RectTransform parentRect = parent as RectTransform;
        if (parent != null && parentRect != null)
        {
            for (int i = transform.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                Graphic g = parent.GetChild(i).GetComponent<Graphic>();
                if (g == null || !g.enabled || !g.gameObject.activeInHierarchy) continue;
                if (!(g is Image) && !(g is RawImage)) continue;
                if (g.color.a < 0.95f) continue;

                Rect r = g.rectTransform.rect;
                if (r.width >= parentRect.rect.width * 0.9f && r.height >= parentRect.rect.height * 0.9f)
                {
                    Warn("'" + g.name + "' is a full-screen, opaque image drawn ON TOP of the wallpaper. " +
                         "Disable it, make it transparent, or move the wallpaper below it in the Hierarchy.");
                    anyWarning = true;
                }
            }
        }

        if (!anyWarning)
        {
            Debug.Log("[LoadingWallpaper] Showing '" + shown.name + "' at opacity " + opacity.ToString("0.00") +
                      ". If you still can't see it, check for another Canvas with a higher Sort Order, " +
                      "or a Camera/Skybox covering the screen.", this);
        }
    }
}