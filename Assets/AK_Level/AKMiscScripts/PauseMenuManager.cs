using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem; // Required for New Input System

public class PauseMenuManager : MonoBehaviour
{
    private static PauseMenuManager instance;

    [Header("Settings")]
    [Tooltip("The exact name of your loading scene where pausing should be blocked.")]
    [SerializeField] private string loadingSceneName = "Scene_Loading";

    [Header("UI Customization")]
    [Tooltip("Drag your custom 'PAUSED' sprite graphic here.")]
    [SerializeField] private Sprite pauseSprite;
    [Tooltip("Width and height of the pause sprite graphic on screen.")]
    [SerializeField] private Vector2 pauseSpriteSize = new Vector2(500f, 150f);

    private GameObject pauseCanvasGO;
    private CanvasGroup canvasGroup;
    private bool isPaused = false;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPauseMenuUI();
    }

    private void Update()
    {
        // 1. Block pausing entirely during the loading scene
        string currentScene = SceneManager.GetActiveScene().name;
        if (currentScene == loadingSceneName)
        {
            if (isPaused) ResumeGame();
            return;
        }

        // 2. Check Keyboard Escape (For easy editor testing)
        bool keyboardTriggered = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        // 3. Check ALL connected gamepads for Select or Start button presses
        bool gamepadTriggered = false;
        foreach (var pad in Gamepad.all)
        {
            if (pad.selectButton.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)
            {
                gamepadTriggered = true;
                break;
            }
        }

        // 4. Toggle Pause State
        if (keyboardTriggered || gamepadTriggered)
        {
            if (isPaused)
                ResumeGame();
            else
                PauseGame();
        }
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f; // Freezes game time

        if (pauseCanvasGO != null)
        {
            pauseCanvasGO.SetActive(true);
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f; // Resumes game time

        if (pauseCanvasGO != null)
        {
            pauseCanvasGO.SetActive(false);
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private void BuildPauseMenuUI()
    {
        // Root Canvas
        pauseCanvasGO = new GameObject("PauseMenuCanvas");
        pauseCanvasGO.transform.SetParent(transform, false);

        var canvas = pauseCanvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32766;

        var scaler = pauseCanvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        pauseCanvasGO.AddComponent<GraphicRaycaster>();

        canvasGroup = pauseCanvasGO.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;

        // Background Dim Panel (Low Opacity)
        var bgGO = new GameObject("BackgroundDim");
        bgGO.transform.SetParent(pauseCanvasGO.transform, false);
        var bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.6f); // 60% black dim

        RectTransform bgRT = bgImage.rectTransform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

        // Centered Pause Sprite Image Graphic
        var imgGO = new GameObject("PausedSpriteGraphic");
        imgGO.transform.SetParent(pauseCanvasGO.transform, false);
        var img = imgGO.AddComponent<Image>();
        
        if (pauseSprite != null)
        {
            img.sprite = pauseSprite;
            img.preserveAspect = true;
        }
        else
        {
            // Fallback placeholder if no sprite is assigned yet
            img.color = new Color(1f, 1f, 1f, 0.3f);
        }

        RectTransform imgRT = img.rectTransform;
        imgRT.anchorMin = new Vector2(0.5f, 0.5f);
        imgRT.anchorMax = new Vector2(0.5f, 0.5f);
        imgRT.sizeDelta = pauseSpriteSize;
        imgRT.anchoredPosition = Vector2.zero;

        // Start disabled
        pauseCanvasGO.SetActive(false);
    }
}