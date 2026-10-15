using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem; // Required for New Input System
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PauseMenuManager : MonoBehaviour
{
    /// <summary>One volume slider in the pause menu, tied to one Audio Mixer parameter.</summary>
    [System.Serializable]
    public class VolumeLayer
    {
        [Tooltip("Text shown next to the slider.")]
        public string label = "Master";
        [Tooltip("Name of the exposed parameter in the Audio Mixer (e.g. MasterVolume).")]
        public string mixerParameter = "MasterVolume";
        [Range(0f, 1f)] public float defaultValue = 1f;
    }

    // Names of the Audio Mixer groups that the other sound scripts link to automatically.
    public const string MusicGroupName = "Music";
    public const string AmbienceGroupName = "Ambience";
    public const string SfxGroupName = "SFX";

    private static PauseMenuManager instance;

    /// <summary>
    /// Finds a group of the Audio Mixer assigned to the pause menu (null if there is no
    /// pause menu yet, no mixer, or no group with that name).
    /// </summary>
    public static AudioMixerGroup FindMixerGroup(string groupName)
    {
        if (instance == null || instance.audioMixer == null) return null;

        foreach (AudioMixerGroup g in instance.audioMixer.FindMatchingGroups(groupName))
            if (g.name == groupName) return g;

        return null;
    }

    [Header("Settings")]
    [Tooltip("The exact name of your loading scene where pausing should be blocked.")]
    [SerializeField] private string loadingSceneName = "Scene_Loading";

    [Header("UI Customization")]
    [Tooltip("Drag your custom 'PAUSED' sprite graphic here.")]
    [SerializeField] private Sprite pauseSprite;
    [Tooltip("Width and height of the pause sprite graphic on screen.")]
    [SerializeField] private Vector2 pauseSpriteSize = new Vector2(500f, 150f);

    [Header("Slider Look (optional sprites)")]
    [Tooltip("Sprite for the slider's background bar. Leave empty for a plain bar.")]
    [SerializeField] private Sprite sliderTrackSprite;
    [Tooltip("Sprite for the filled part of the slider. Leave empty for a plain bar.")]
    [SerializeField] private Sprite sliderFillSprite;
    [Tooltip("Sprite for the draggable knob. Leave empty for a plain square.")]
    [SerializeField] private Sprite sliderHandleSprite;
    
    [Tooltip("Height of the background track and fill bar in pixels.")]
    [SerializeField] private float trackHeight = 10f;
    [Tooltip("Width and height of the draggable handle knob in pixels.")]
    [SerializeField] private Vector2 handleSize = new Vector2(30f, 30f);

    [Header("Controller")]
    [Tooltip("How much one D-pad / stick push changes a slider (0.05 = 5%). Hold to keep going.")]
    [Range(0.01f, 0.25f)] [SerializeField] private float controllerStep = 0.05f;
    [Tooltip("Color of the slider (label and knob) that the controller is currently on.")]
    [SerializeField] private Color selectedColor = new Color(1f, 0.85f, 0.45f, 1f);

    [Header("Audio Layers")]
    [Tooltip("The Audio Mixer that has the exposed volume parameters. The music, environment and menu sounds link to this mixer automatically.")]
    [SerializeField] private AudioMixer audioMixer;
    [Tooltip("One slider per entry. Add more entries for more layers; each needs an exposed mixer parameter.")]
    [SerializeField] private VolumeLayer[] layers =
    {
        new VolumeLayer { label = "Master", mixerParameter = "MasterVolume" },
        new VolumeLayer { label = "Music",  mixerParameter = "MusicVolume"  },
        new VolumeLayer { label = "Nature", mixerParameter = "AmbienceVolume" },
        new VolumeLayer { label = "SFX",    mixerParameter = "SFXVolume"    },
    };
    [Tooltip("Vertical position of the first slider, relative to the screen center (negative = below the PAUSED graphic).")]
    [SerializeField] private float volumeStartY = -160f;
    [SerializeField] private float volumeRowSpacing = 56f;

    private const float RepeatDelay = 0.4f;     // hold left/right: pause before it repeats
    private const float RepeatInterval = 0.08f; // hold left/right: time between repeats

    private GameObject pauseCanvasGO;
    private CanvasGroup canvasGroup;
    private bool isPaused = false;

    private float[] layerValues;
    private Slider[] layerSliders;
    private TMP_Text[] layerLabels;
    private Image[] layerHandles;
    private int selectedLayer;
    private int previousVertical;
    private int heldDirection;
    private float nextRepeatTime;

    private GameObject createdEventSystem;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        LoadLayerValues();
        BuildPauseMenuUI();
    }

    private void Start()
    {
        if (instance != this) return;

        if (audioMixer == null)
        {
            Debug.LogWarning("PauseMenuManager: no Audio Mixer assigned, so the volume sliders will do nothing.");
            return;
        }

        // Apply the saved volumes right away so they hold from the first scene on.
        for (int i = 0; i < layers.Length; i++)
            ApplyLayer(i, true);
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

        // 5. Controller control of the volume sliders while paused
        if (isPaused) HandleControllerInput();
    }

    public void PauseGame()
    {
        if (isPaused) return;

        isPaused = true;
        Time.timeScale = 0f; // Freezes game time

        if (pauseCanvasGO != null)
        {
            pauseCanvasGO.SetActive(true);
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
        }

        // Free the mouse so the sliders can be dragged.
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Mouse dragging needs an EventSystem; create one only if the scene has none.
        EnsureEventSystem();

        // Controller starts on the slider it was last on.
        previousVertical = 0;
        heldDirection = 0;
        SetSelectedLayer(selectedLayer);
    }

    public void ResumeGame()
    {
        bool wasPaused = isPaused;
        isPaused = false;
        Time.timeScale = 1f; // Resumes game time

        if (pauseCanvasGO != null)
        {
            pauseCanvasGO.SetActive(false);
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }

        if (wasPaused)
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            PlayerPrefs.Save();
        }

        if (createdEventSystem != null)
        {
            Destroy(createdEventSystem);
            createdEventSystem = null;
        }
    }

    // ----------------------------------------------------------- Controller

    private void HandleControllerInput()
    {
        if (layerSliders == null || layerSliders.Length == 0) return;

        Vector2 input = Vector2.zero;
        foreach (var pad in Gamepad.all)
        {
            Vector2 dpad = pad.dpad.ReadValue();
            Vector2 stick = pad.leftStick.ReadValue();
            input += dpad.sqrMagnitude > stick.sqrMagnitude ? dpad : stick;
        }

        int vertical = input.y > 0.5f ? 1 : (input.y < -0.5f ? -1 : 0);
        int horizontal = input.x > 0.5f ? 1 : (input.x < -0.5f ? -1 : 0);

        if (vertical != 0 && vertical != previousVertical)
            SetSelectedLayer((selectedLayer - vertical + layerSliders.Length) % layerSliders.Length);
        previousVertical = vertical;

        if (horizontal == 0)
        {
            heldDirection = 0;
            return;
        }

        float now = Time.unscaledTime; 
        if (horizontal != heldDirection)
        {
            heldDirection = horizontal;
            nextRepeatTime = now + RepeatDelay;
            NudgeSelected(horizontal);
        }
        else if (now >= nextRepeatTime)
        {
            nextRepeatTime = now + RepeatInterval;
            NudgeSelected(horizontal);
        }
    }

    private void NudgeSelected(int direction)
    {
        Slider s = layerSliders[selectedLayer];
        s.value = Mathf.Clamp01(s.value + direction * controllerStep);
    }

    private void SetSelectedLayer(int index)
    {
        if (layerSliders == null || layerSliders.Length == 0) return;

        selectedLayer = Mathf.Clamp(index, 0, layerSliders.Length - 1);

        for (int i = 0; i < layerSliders.Length; i++)
        {
            Color c = i == selectedLayer ? selectedColor : Color.white;
            if (layerHandles[i] != null) layerHandles[i].color = c;
            if (layerLabels[i] != null) layerLabels[i].color = c;
        }
    }

    // ---------------------------------------------------------------- Audio

    private static string PrefKey(VolumeLayer layer)
    {
        return "Volume_" + layer.mixerParameter;
    }

    private void LoadLayerValues()
    {
        layerValues = new float[layers.Length];
        for (int i = 0; i < layers.Length; i++)
            layerValues[i] = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefKey(layers[i]), layers[i].defaultValue));
    }

    private void ApplyLayer(int index, bool warnIfMissing)
    {
        if (audioMixer == null) return;

        float v = layerValues[index];
        float dB = v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;

        bool ok = audioMixer.SetFloat(layers[index].mixerParameter, dB);
        if (!ok && warnIfMissing)
            Debug.LogWarning("PauseMenuManager: the Audio Mixer has no exposed parameter named \"" +
                             layers[index].mixerParameter + "\". Expose it in the mixer (right-click the group's Volume).");
    }

    private void OnLayerChanged(int index, float value, TMP_Text percentLabel)
    {
        layerValues[index] = value;
        percentLabel.text = Mathf.RoundToInt(value * 100f) + "%";

        PlayerPrefs.SetFloat(PrefKey(layers[index]), value);
        ApplyLayer(index, false);

        SetSelectedLayer(index);
    }

    // ------------------------------------------------------------------ UI

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        createdEventSystem = new GameObject("PauseEventSystem", typeof(EventSystem));
        createdEventSystem.transform.SetParent(transform, false);

        var module = createdEventSystem.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    private void BuildPauseMenuUI()
    {
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

        var bgGO = new GameObject("BackgroundDim");
        bgGO.transform.SetParent(pauseCanvasGO.transform, false);
        var bgImage = bgGO.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.6f); 

        RectTransform bgRT = bgImage.rectTransform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;

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
            img.color = new Color(1f, 1f, 1f, 0.3f);
        }

        RectTransform imgRT = img.rectTransform;
        imgRT.anchorMin = new Vector2(0.5f, 0.5f);
        imgRT.anchorMax = new Vector2(0.5f, 0.5f);
        imgRT.sizeDelta = pauseSpriteSize;
        imgRT.anchoredPosition = Vector2.zero;

        BuildVolumeRows(pauseCanvasGO.transform);

        pauseCanvasGO.SetActive(false);
    }

    private void BuildVolumeRows(Transform parent)
    {
        layerSliders = new Slider[layers.Length];
        layerLabels = new TMP_Text[layers.Length];
        layerHandles = new Image[layers.Length];

        for (int i = 0; i < layers.Length; i++)
        {
            int index = i;

            RectTransform row = NewRect("Row_" + layers[i].label, parent);
            row.anchorMin = new Vector2(0.5f, 0.5f);
            row.anchorMax = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(700f, Mathf.Max(40f, handleSize.y));
            row.anchoredPosition = new Vector2(0f, volumeStartY - i * volumeRowSpacing);

            TMP_Text label = NewText("Label", row, layers[i].label, TextAlignmentOptions.MidlineLeft);
            RectTransform labelRT = label.rectTransform;
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(0f, 1f);
            labelRT.pivot = new Vector2(0f, 0.5f);
            labelRT.sizeDelta = new Vector2(180f, 0f);
            labelRT.anchoredPosition = Vector2.zero;

            TMP_Text percent = NewText("Percent", row, "", TextAlignmentOptions.MidlineRight);
            RectTransform percentRT = percent.rectTransform;
            percentRT.anchorMin = new Vector2(1f, 0f);
            percentRT.anchorMax = new Vector2(1f, 1f);
            percentRT.pivot = new Vector2(1f, 0.5f);
            percentRT.sizeDelta = new Vector2(80f, 0f);
            percentRT.anchoredPosition = Vector2.zero;

            Slider slider = NewSlider(row);
            slider.SetValueWithoutNotify(layerValues[i]);
            percent.text = Mathf.RoundToInt(layerValues[i] * 100f) + "%";
            slider.onValueChanged.AddListener(v => OnLayerChanged(index, v, percent));

            layerSliders[i] = slider;
            layerLabels[i] = label;
            layerHandles[i] = slider.handleRect.GetComponent<Image>();
        }

        SetSelectedLayer(0);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TMP_Text NewText(string name, Transform parent, string text, TextAlignmentOptions alignment)
    {
        RectTransform rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = 28f;
        t.color = Color.white;
        t.alignment = alignment;
        t.raycastTarget = false;
        return t;
    }

    private static void ApplySprite(Image image, Sprite sprite, Color placeholderColor)
    {
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        }
        else
        {
            image.color = placeholderColor;
        }
    }

    // Builds a standard horizontal slider with independent track and handle sizes.
    private Slider NewSlider(Transform parent)
    {
        float maxH = Mathf.Max(trackHeight, handleSize.y);

        RectTransform root = NewRect("Slider", parent);
        root.anchorMin = new Vector2(0f, 0.5f);
        root.anchorMax = new Vector2(1f, 0.5f);
        root.offsetMin = new Vector2(200f, -maxH * 0.5f);
        root.offsetMax = new Vector2(-90f, maxH * 0.5f);

        // Track
        RectTransform track = NewRect("Background", root);
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(1f, 0.5f);
        track.sizeDelta = new Vector2(0f, trackHeight);
        var trackImage = track.gameObject.AddComponent<Image>();
        ApplySprite(trackImage, sliderTrackSprite, new Color(1f, 1f, 1f, 0.2f));

        // Fill Area
        RectTransform fillArea = NewRect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, 0.5f);
        fillArea.anchorMax = new Vector2(1f, 0.5f);
        fillArea.anchoredPosition = new Vector2(-handleSize.x * 0.25f, 0f);
        fillArea.sizeDelta = new Vector2(-handleSize.x * 0.5f, trackHeight);

        RectTransform fill = NewRect("Fill", fillArea);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.sizeDelta = new Vector2(handleSize.x * 0.5f, 0f);
        var fillImage = fill.gameObject.AddComponent<Image>();
        ApplySprite(fillImage, sliderFillSprite, new Color(1f, 1f, 1f, 0.85f));

        // Handle
        RectTransform handleArea = NewRect("Handle Slide Area", root);
        handleArea.anchorMin = new Vector2(0f, 0.5f);
        handleArea.anchorMax = new Vector2(1f, 0.5f);
        handleArea.sizeDelta = new Vector2(-handleSize.x, maxH);

        RectTransform handle = NewRect("Handle", handleArea);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.sizeDelta = handleSize;
        var handleImage = handle.gameObject.AddComponent<Image>();
        ApplySprite(handleImage, sliderHandleSprite, Color.white);
        if (sliderHandleSprite != null) handleImage.preserveAspect = true;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;

        slider.transition = Selectable.Transition.None;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };

        return slider;
    }
}