using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Post-processing for the chamber selection map (URP). Everything is created from code, so there is
/// nothing to set up in the Inspector: bloom, vignette, color grading, film grain, a touch of chromatic
/// aberration and depth of field.
///
/// It also plays with the map intro: the map starts dark, desaturated, blurry and heavily vignetted,
/// then fades up, colors flood in and the focus pulls sharp.
///
/// The effects live in their OWN volume on the "MapBoard" layer and the map camera only looks at that
/// layer, so the level's own post-processing never touches the map (and the map's never touches the level).
///
/// Put this on the SAME GameObject as MapController.
/// </summary>
[RequireComponent(typeof(MapController))]
public class MapPostFX : MonoBehaviour
{
    [Header("Master")]
    public bool enableFX = true;
    [Tooltip("Layer the map volume sits on. The map camera will ONLY see volumes on this layer.")]
    public string volumeLayerName = "MapBoard";

    [Header("Intro timing (match MapIntroAnimator)")]
    public float startDelay = 0.25f;
    public float introDuration = 3.2f;

    [Header("Final look")]
    public float exposure = 0.1f;
    [Range(-100f, 100f)] public float contrast = 14f;
    [Range(-100f, 100f)] public float saturation = 8f;
    [Tooltip("Overall tint. Slightly warm by default.")]
    public Color colorFilter = new Color(1f, 0.95f, 0.88f);
    [Range(0f, 1f)] public float vignette = 0.38f;
    [Range(0f, 1f)] public float vignetteSmoothness = 0.55f;

    [Header("Bloom (makes HDR / bright things glow)")]
    public float bloomIntensity = 0.9f;
    [Tooltip("Lower = more things glow. Your HDR glow color is brighter than 1, so it always glows.")]
    public float bloomThreshold = 0.85f;
    [Range(0f, 1f)] public float bloomScatter = 0.7f;
    public Color bloomTint = new Color(1f, 0.85f, 0.7f);

    [Header("Film look")]
    [Range(0f, 1f)] public float filmGrain = 0.18f;
    [Range(0f, 1f)] public float chromaticAberration = 0.07f;

    [Header("Depth of field (soft focus falloff at the map edges)")]
    public bool depthOfField = true;
    [Tooltip("Higher = sharper. Depends on the scale of your scene: if the whole map looks blurry, raise it; if there is no blur at all, lower it.")]
    [Range(1f, 32f)] public float dofAperture = 2f;
    [Range(1f, 300f)] public float dofFocalLength = 250f;

    [Header("Life")]
    [Tooltip("Tiny slow pulse of vignette and bloom once the intro is done. 0 = off.")]
    [Range(0f, 0.05f)] public float breathing = 0.02f;

    [Header("Intro start values (what it fades up from)")]
    public float introExposure = -3.5f;
    public float introSaturation = -60f;
    [Range(0f, 1f)] public float introVignette = 0.75f;
    public float introBloom = 2.2f;
    [Range(0f, 1f)] public float introAberration = 0.6f;
    [Tooltip("Where the focus starts, as a fraction of the final focus distance (the focus pulls out to 1).")]
    [Range(0.05f, 1f)] public float introFocusFraction = 0.3f;

    private Volume volume;
    private VolumeProfile profile;

    private Bloom bloom;
    private Vignette vig;
    private ColorAdjustments grade;
    private FilmGrain grain;
    private ChromaticAberration chroma;
    private DepthOfField dof;

    private bool active;
    private float clock;
    private float focus;

    // ------------------------------------------------------------------ public API

    /// <summary>Switch the map look on and start fading it up. Call after the map camera is enabled.</summary>
    public void Show()
    {
        MapController mc = GetComponent<MapController>();
        if (!enableFX || mc == null || mc.mapCamera == null) return;

        Build(mc);
        active = true;
        clock = -startDelay;
        Evaluate();                     // first frame is already the dark start look
    }

    /// <summary>Jump to the finished look (used when the intro is skipped).</summary>
    public void Skip()
    {
        if (active) clock = introDuration;
    }

    /// <summary>Switch the map look off (when the level starts).</summary>
    public void Hide()
    {
        active = false;
        if (volume != null) volume.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ build

    private void Build(MapController mc)
    {
        int layer = LayerMask.NameToLayer(volumeLayerName);
        if (layer < 0) layer = 0;       // layer not found: fall back to Default

        Camera cam = mc.mapCamera;

        // make sure the map camera renders post-processing, and only sees OUR volume
        UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        camData.volumeLayerMask = 1 << layer;

        if (volume == null)
        {
            GameObject go = new GameObject("MapPostFXVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
        }
        volume.gameObject.layer = layer;
        volume.gameObject.SetActive(true);
        volume.weight = 1f;

        // fresh profile each time, so Inspector changes are picked up the next time the map shows
        if (profile != null) Destroy(profile);
        profile = ScriptableObject.CreateInstance<VolumeProfile>();

        bloom = profile.Add<Bloom>(true);
        bloom.threshold.value = bloomThreshold;
        bloom.scatter.value = bloomScatter;
        bloom.tint.value = bloomTint;
        bloom.highQualityFiltering.value = true;

        vig = profile.Add<Vignette>(true);
        vig.color.value = Color.black;
        vig.smoothness.value = vignetteSmoothness;
        vig.rounded.value = false;

        grade = profile.Add<ColorAdjustments>(true);
        grade.contrast.value = contrast;
        grade.colorFilter.value = colorFilter;

        grain = profile.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin2;
        grain.intensity.value = filmGrain;
        grain.response.value = 0.8f;

        chroma = profile.Add<ChromaticAberration>(true);

        dof = null;
        if (depthOfField && !cam.orthographic)
        {
            // focus on the middle of the chamber icons (depth along the view direction)
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (MapNode m in mc.mapBoardRoot.GetComponentsInChildren<MapNode>(true))
            {
                sum += m.transform.position;
                count++;
            }
            Vector3 center = count > 0 ? sum / count : mc.mapBoardRoot.transform.position;
            focus = Mathf.Max(1f, Vector3.Dot(center - cam.transform.position, cam.transform.forward));

            dof = profile.Add<DepthOfField>(true);
            dof.mode.value = DepthOfFieldMode.Bokeh;
            dof.aperture.value = dofAperture;
            dof.focalLength.value = dofFocalLength;
            dof.bladeCount.value = 5;
            dof.focusDistance.value = focus;
        }

        volume.sharedProfile = profile;
    }

    // ------------------------------------------------------------------ playback

    private void Update()
    {
        if (!active) return;
        clock += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        Evaluate();
    }

    private void Evaluate()
    {
        if (profile == null) return;

        float p = Mathf.Clamp01(clock / Mathf.Max(0.01f, introDuration));
        float e = 1f - Mathf.Pow(1f - p, 3f);      // ease out
        float s = p * p * (3f - 2f * p);           // smoothstep (focus pull is slower and softer)

        float breath = Mathf.Sin(Time.unscaledTime * 0.9f) * breathing * e;

        grade.postExposure.value = Mathf.Lerp(introExposure, exposure, e);
        grade.saturation.value = Mathf.Lerp(introSaturation, saturation, e);

        vig.intensity.value = Mathf.Clamp01(Mathf.Lerp(introVignette, vignette, e) + breath);
        bloom.intensity.value = Mathf.Max(0f, Mathf.Lerp(introBloom, bloomIntensity, e) * (1f + breath * 6f));
        chroma.intensity.value = Mathf.Lerp(introAberration, chromaticAberration, e);

        if (dof != null)
            dof.focusDistance.value = Mathf.Lerp(focus * introFocusFraction, focus, s);
    }

    private void OnDestroy()
    {
        if (profile != null) Destroy(profile);
    }
}
