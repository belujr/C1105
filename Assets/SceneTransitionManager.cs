using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Seamless scene transitions with a loading scene in between:
///
///   fade out -> Scene_Loading (progress bar) -> target loads in background
///            -> fade out -> target activates -> fade in
///
/// No setup needed. It creates itself automatically when the game starts and
/// survives scene loads. Call it from anywhere:
///     SceneTransitionManager.LoadScene("Scene_Level");                  // with loading scene
///     SceneTransitionManager.LoadScene("Scene_Level", false);           // fade only
///     SceneTransitionManager.RunTransition(() => { ... });              // swap inside a scene
///
/// NOTE: the manager is created from code, so values changed in the Inspector
/// only last for that Play session. Change the defaults below to make them permanent.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    private static SceneTransitionManager instance;

    /// <summary>True while a transition is running. Use it to ignore repeat triggers.</summary>
    public static bool IsTransitioning => instance != null && instance.busy;

    /// <summary>0..1 loading progress of the target scene. Read by LoadingScreenUI.</summary>
    public static float Progress { get; private set; }

    /// <summary>Name of the scene currently being loaded.</summary>
    public static string TargetScene { get; private set; }

    [Header("Loading Scene")]
    [Tooltip("Must be added to Build Settings. Leave empty to disable the loading scene.")]
    [SerializeField] private string loadingSceneName = "Scene_Loading";
    [Tooltip("Shortest time the loading scene stays up, so it never just flashes. Set 0 for none.")]
    [SerializeField] private float minLoadingTime = 1.5f;

    [Header("Timing (seconds)")]
    [SerializeField] private float fadeOutTime = 0.4f;
    [SerializeField] private float fadeInTime = 0.5f;
    [Tooltip("Minimum time the screen stays black, hides the last bit of scene activation.")]
    [SerializeField] private float minBlackTime = 0.15f;

    private CanvasGroup group;
    private bool busy;

    // Creates the manager automatically before the first scene loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject("SceneTransitionManager");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SceneTransitionManager>();
    }

    private void Awake()
    {
        BuildFadeOverlay();
    }

    // ------------------------------------------------------------------ Public API

    /// <summary>
    /// Seamlessly loads a scene. With useLoadingScreen the loading scene is shown in between.
    /// Returns false if it could not start.
    /// </summary>
    public static bool LoadScene(string sceneName, bool useLoadingScreen = true)
    {
        if (instance == null || instance.busy) return false;

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"SceneTransitionManager: scene '{sceneName}' is not in Build Settings (File > Build Profiles > Scene List).");
            return false;
        }

        bool showLoading = useLoadingScreen && instance.HasLoadingScene();
        instance.StartCoroutine(instance.Transition(sceneName, showLoading));
        return true;
    }

    /// <summary>
    /// Fade to black, run <paramref name="onBlack"/> while the screen is black, then fade back in.
    /// Use this for swaps INSIDE a scene (map -> level, room -> room, respawn...).
    /// Returns false if a transition is already running.
    /// </summary>
    public static bool RunTransition(System.Action onBlack, int settleFrames = 3)
    {
        if (instance == null || instance.busy) return false;
        instance.StartCoroutine(instance.TransitionWithAction(onBlack, settleFrames));
        return true;
    }

    // ------------------------------------------------------------------ Transitions

    private bool HasLoadingScene()
    {
        if (string.IsNullOrEmpty(loadingSceneName)) return false;

        if (!Application.CanStreamedLevelBeLoaded(loadingSceneName))
        {
            Debug.LogWarning($"SceneTransitionManager: loading scene '{loadingSceneName}' is not in Build Settings, skipping the loading screen.");
            return false;
        }
        return true;
    }

    private IEnumerator Transition(string sceneName, bool showLoading)
    {
        busy = true;
        TargetScene = sceneName;
        Progress = 0f;

        // 1. Fade to black (unscaled time, so it works even if the game is paused/slowed)
        yield return Fade(1f, fadeOutTime);

        // 2. Go to the loading scene (tiny, so it loads almost instantly) and reveal it
        if (showLoading)
        {
            yield return SceneManager.LoadSceneAsync(loadingSceneName);
            yield return null;
            yield return null;
            yield return Fade(0f, fadeInTime);
        }

        // 3. Load the real target in the background, but hold activation
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        float minTime = showLoading ? minLoadingTime : 0f;
        float elapsed = 0f;

        while (true)
        {
            elapsed += Time.unscaledDeltaTime;

            float real = Mathf.Clamp01(op.progress / 0.9f);                      // Unity stops at 0.9 while held
            float timed = minTime > 0f ? Mathf.Clamp01(elapsed / minTime) : 1f;  // keeps the bar from jumping
            float target = Mathf.Min(real, timed);

            Progress = showLoading
                ? Mathf.MoveTowards(Progress, target, 2f * Time.unscaledDeltaTime) // smooth bar
                : target;

            if (real >= 1f && elapsed >= minTime && Progress >= 1f) break;
            yield return null;
        }

        // 4. Short beat on 100%, then fade out the loading scene
        yield return new WaitForSecondsRealtime(showLoading ? 0.2f : minBlackTime);
        if (showLoading) yield return Fade(1f, fadeOutTime);

        // 5. Activate the target while the screen is black
        op.allowSceneActivation = true;
        yield return op;

        // 6. Let the new scene run Awake/Start and render a couple of frames
        yield return null;
        yield return null;

        // 7. Fade in
        yield return Fade(0f, fadeInTime);

        busy = false;
    }

    private IEnumerator TransitionWithAction(System.Action onBlack, int settleFrames)
    {
        busy = true;

        yield return Fade(1f, fadeOutTime);

        // Do the swap while nobody can see it. try/catch so an error can never leave the screen black.
        try { onBlack?.Invoke(); }
        catch (System.Exception e) { Debug.LogException(e); }

        // Let new objects run Awake/Start and the camera settle behind the black screen
        for (int i = 0; i < settleFrames; i++) yield return null;
        yield return new WaitForSecondsRealtime(minBlackTime);

        yield return Fade(0f, fadeInTime);

        busy = false;
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        float startAlpha = group.alpha;
        group.blocksRaycasts = true; // block UI clicks while fading

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        group.alpha = targetAlpha;
        group.blocksRaycasts = targetAlpha > 0f;
    }

    private void BuildFadeOverlay()
    {
        var canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // always on top of everything
        canvasGO.AddComponent<GraphicRaycaster>();

        group = canvasGO.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;

        var imageGO = new GameObject("Black");
        imageGO.transform.SetParent(canvasGO.transform, false);
        var image = imageGO.AddComponent<Image>();
        image.color = Color.black;

        RectTransform rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}