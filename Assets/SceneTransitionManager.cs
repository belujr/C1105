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
    [Tooltip("Shortest time the loading scene stays up, so it never just flashes. The progress bar takes this long to fill. Set 0 for none.")]
    [SerializeField] private float minLoadingTime = 3f;
    [Tooltip("Extra time the loading scene waits at 100% before fading out to the target scene. Set 0 for none.")]
    [SerializeField] private float holdAtFullTime = 1f;

    [Header("Timing (seconds)")]
    [SerializeField] private float fadeOutTime = 0.4f;
    [SerializeField] private float fadeInTime = 0.5f;
    [Tooltip("Minimum time the screen stays black, hides the last bit of scene activation.")]
    [SerializeField] private float minBlackTime = 0.15f;

    private CanvasGroup group;
    private Canvas fadeCanvas;
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

        int loadingIndex = useLoadingScreen ? instance.FindLoadingSceneIndex() : -1;
        instance.StartCoroutine(instance.Transition(sceneName, loadingIndex));
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

    /// <summary>
    /// Finds the loading scene in Build Settings (upper/lower case ignored).
    /// Returns its build index, or -1 if it is missing (and logs what IS in the build).
    /// </summary>
    private int FindLoadingSceneIndex()
    {
        if (string.IsNullOrEmpty(loadingSceneName)) return -1;

        int count = SceneManager.sceneCountInBuildSettings;
        var found = new System.Text.StringBuilder();

        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

            if (string.Equals(sceneName, loadingSceneName, System.StringComparison.OrdinalIgnoreCase))
                return i;

            found.Append("\n   ").Append(i).Append(": ").Append(sceneName);
        }

        Debug.LogError(
            $"SceneTransitionManager: loading scene '{loadingSceneName}' was NOT found in the build, so the loading screen is skipped.\n" +
            $"Scenes currently in the build ({count}):{found}\n" +
            "Fix: File > Build Profiles > Scene List > Add Open Scenes (with Scene_Loading open) and make sure it is ticked.");
        return -1;
    }

    private IEnumerator Transition(string sceneName, int loadingBuildIndex)
    {
        bool showLoading = loadingBuildIndex >= 0;

        busy = true;
        TargetScene = sceneName;
        Progress = 0f;

        if (Debug.isDebugBuild)
            Debug.Log($"SceneTransitionManager: -> '{sceneName}' " + (showLoading ? "via loading scene" : "(fade only, no loading scene)"));

        // 1. Fade to black (unscaled time, so it works even if the game is paused/slowed)
        yield return Fade(1f, fadeOutTime);

        // 2. Go to the loading scene (tiny, so it loads almost instantly) and reveal it
        if (showLoading)
        {
            yield return SceneManager.LoadSceneAsync(loadingBuildIndex);
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

        // 4. Wait on 100% (holdAtFullTime), then fade out the loading scene
        yield return new WaitForSecondsRealtime(showLoading ? holdAtFullTime : minBlackTime);
        if (showLoading) yield return Fade(1f, fadeOutTime);

        // 5. Activate the target while the screen is black
        op.allowSceneActivation = true;
        yield return op;

        // 6. Let the new scene run Awake/Start (and anything spawned right after load) and render a few frames
        for (int i = 0; i < 4; i++) yield return null;

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
        fadeCanvas.enabled = true;   // only exists on screen while fading / black
        group.blocksRaycasts = true; // block UI clicks while fading

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        group.alpha = targetAlpha;
        group.blocksRaycasts = targetAlpha > 0f;
        fadeCanvas.enabled = targetAlpha > 0f; // fully clear -> switch the canvas off completely (zero cost)
    }

    private void BuildFadeOverlay()
    {
        var canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // always on top of everything
        canvasGO.AddComponent<GraphicRaycaster>();
        fadeCanvas = canvas;

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

        fadeCanvas.enabled = false; // idle until a transition starts
    }
}