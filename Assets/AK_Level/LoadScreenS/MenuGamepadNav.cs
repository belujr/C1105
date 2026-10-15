using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Put on TitleMenu (the object with the CanvasGroup). D-pad up/down moves between the buttons,
// the submit action presses the highlighted one by running its existing OnClick.
public class MenuGamepadNav : MonoBehaviour
{
    [Header("Buttons (top to bottom)")]
    [SerializeField] private Button[] buttons;            // Play, Settings, Quit

    [Header("Input")]
    [SerializeField] private InputActionReference navigateAction; // Vector2, e.g. <Gamepad>/dpad
    [SerializeField] private InputActionReference submitAction;   // Button, e.g. <Gamepad>/buttonSouth

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.3f);
    [SerializeField] private Color pressedColor = new Color(1f, 0.5f, 0.2f);

    [Header("Sound")]
    [Tooltip("Short sound played each time the highlight moves to another button. Leave empty for no sound.")]
    [SerializeField] private AudioClip selectClip;
    [Tooltip("Volume of the selection sound. Can be changed live in Play mode.")]
    [Range(0f, 1f)] [SerializeField] private float selectVolume = 0.5f;
    [Tooltip("Sound played when a button is pressed with the submit action. Leave empty for no sound.")]
    [SerializeField] private AudioClip submitClip;
    [Tooltip("Volume of the press sound. Can be changed live in Play mode.")]
    [Range(0f, 1f)] [SerializeField] private float submitVolume = 0.7f;
    [Tooltip("Optional. Leave empty to link automatically to the 'SFX' group of the Pause Menu Manager's mixer.")]
    [SerializeField] private AudioMixerGroup sfxOutput;

    private CanvasGroup group;
    private AudioSource sfxSource;
    private int index;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();

        // Own AudioSource for the selection sound, so nothing else needs setting up.
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f; // 2D
        if (sfxOutput != null) sfxSource.outputAudioMixerGroup = sfxOutput;
    }

    private void Start()
    {
        // By now every Awake has run, so the Pause Menu Manager is ready.
        // Link to its mixer unless an Output was set by hand.
        if (sfxOutput == null)
        {
            sfxOutput = PauseMenuManager.FindMixerGroup(PauseMenuManager.SfxGroupName);
            if (sfxOutput != null) sfxSource.outputAudioMixerGroup = sfxOutput;
        }

        if ((selectClip != null || submitClip != null) && sfxOutput == null)
            Debug.LogWarning("MenuGamepadNav: couldn't link the menu sounds to the pause menu's mixer. Check that a Pause Menu Manager with an Audio Mixer exists and has a group named \"" +
                             PauseMenuManager.SfxGroupName + "\".", this);
    }

    private void OnEnable()
    {
        navigateAction?.action?.Enable();
        submitAction?.action?.Enable();
        index = 0;
        Refresh();
    }

    private void Update()
    {
        if (group != null && !group.interactable) return; // ignore input while fading out

        // stop the EventSystem from also submitting a button the mouse selected (double press)
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        var nav = navigateAction?.action;
        if (nav != null && nav.WasPressedThisFrame())
        {
            float y = nav.ReadValue<Vector2>().y;
            if (y > 0.5f) Move(-1);
            else if (y < -0.5f) Move(1);
        }

        var submit = submitAction?.action;
        if (submit != null && submit.WasPressedThisFrame() && buttons.Length > 0)
        {
            Button b = buttons[index];
            if (b == null || !b.interactable) return;
            b.image.color = pressedColor;
            PlaySubmitSound();
            b.onClick.Invoke();
        }
    }

    private void Move(int dir)
    {
        if (buttons.Length == 0) return;

        int next = (index + dir + buttons.Length) % buttons.Length; // wraps around
        bool changed = next != index;

        index = next;
        Refresh();

        if (changed) PlaySelectSound();
    }

    private void PlaySelectSound()
    {
        if (selectClip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(selectClip, selectVolume);
    }

    // The press usually closes the menu or starts a scene change, so this sound plays from its
    // own temporary object. That way it isn't cut off when this menu is disabled or unloaded.
    private void PlaySubmitSound()
    {
        if (submitClip == null) return;

        var go = new GameObject("MenuSubmitSound");
        DontDestroyOnLoad(go);

        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D
        if (sfxOutput != null) src.outputAudioMixerGroup = sfxOutput;
        src.clip = submitClip;
        src.volume = submitVolume;
        src.Play();

        Destroy(go, submitClip.length + 0.2f);
    }

    private void Refresh()
    {
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null && buttons[i].image != null)
                buttons[i].image.color = i == index ? highlightColor : normalColor;
    }
}