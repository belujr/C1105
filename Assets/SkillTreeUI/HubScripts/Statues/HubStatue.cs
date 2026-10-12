using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class HubStatue : MonoBehaviour
{
    [Header("Statue Lore Data")]
    [Tooltip("Assign the StatueInfo asset for this specific statue.")]
    public StatueInfo statueInfo; //[cite: 22]

    [Header("Unique Canvas / Panel Reference")]
    [Tooltip("Assign the unique UI Canvas or Panel for this specific statue.")]
    public GameObject statueCanvasPanel;
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    [Header("Text Reveal (Edith Finch style)")]
    [Tooltip("Also animate the statue name. It types first, then the description follows.")]
    public bool animateName = true;

    [Tooltip("Pause between the name finishing and the description starting.")]
    [Min(0f)] public float delayBeforeDescription = 0.25f;

    [Tooltip("Pressing interact while text is still revealing completes it instantly instead of closing the canvas.")]
    public bool interactSkipsTyping = true;

    [Header("Interact Prompt UI")]
    [Tooltip("The floating prompt/icon that appears above the statue when close.")]
    public GameObject interactPromptUI; //[cite: 22]

    [Header("Input Data Reference")]
    public InputActionReference interactActionRef; //[cite: 22]

    private bool isPlayerInZone;
    private Coroutine typingRoutine;

    private void OnEnable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Enable();
    }

    private void OnDisable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Disable();
    }

    private void Start()
    {
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        if (statueCanvasPanel != null) statueCanvasPanel.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInZone = true;
            if (interactPromptUI != null) interactPromptUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInZone = false;
            if (interactPromptUI != null) interactPromptUI.SetActive(false);
        }
    }

    private void Update()
    {
        bool interactPressed = interactActionRef != null &&
                               interactActionRef.action != null &&
                               interactActionRef.action.WasPressedThisFrame();

        if (isPlayerInZone && interactPressed)
        {
            ToggleStatueCanvas();
        }
    }

    private void ToggleStatueCanvas()
    {
        if (statueCanvasPanel == null || statueInfo == null) return;

        bool isActive = statueCanvasPanel.activeSelf;

        if (!isActive)
        {
            // Open this statue's canvas, then reveal the title and dialogue text
            statueCanvasPanel.SetActive(true);
            StopTyping();
            typingRoutine = StartCoroutine(TypeRoutine());
        }
        else if (interactSkipsTyping && IsTyping())
        {
            // Still revealing: first press finishes the text instantly
            CompleteTyping();
        }
        else
        {
            // Close the canvas
            StopTyping();
            statueCanvasPanel.SetActive(false);
        }
    }

    // ---------------------------------------------------------------------------------
    // Text reveal
    // ---------------------------------------------------------------------------------

    private IEnumerator TypeRoutine()
    {
        // Clear the description first so nothing shows while the name is typing.
        if (descriptionText != null) descriptionText.text = string.Empty;

        if (nameText != null)
        {
            if (animateName)
            {
                MorphTypewriterText nameFx = GetOrAddTypewriter(nameText);
                nameFx.Play(statueInfo.statueName, statueInfo.typingSpeedMultiplier);
                while (nameFx.IsPlaying) yield return null;

                if (delayBeforeDescription > 0f && descriptionText != null)
                    yield return new WaitForSecondsRealtime(delayBeforeDescription);
            }
            else
            {
                nameText.text = statueInfo.statueName;
            }
        }

        if (descriptionText != null)
        {
            MorphTypewriterText descFx = GetOrAddTypewriter(descriptionText);
            descFx.Play(statueInfo.statueDescription, statueInfo.typingSpeedMultiplier);
            while (descFx.IsPlaying) yield return null;
        }

        typingRoutine = null;
    }

    private bool IsTyping()
    {
        return typingRoutine != null;
    }

    private void CompleteTyping()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        if (nameText != null)
            GetOrAddTypewriter(nameText).ShowInstant(statueInfo.statueName);
        if (descriptionText != null)
            GetOrAddTypewriter(descriptionText).ShowInstant(statueInfo.statueDescription);
    }

    private void StopTyping()
    {
        if (typingRoutine != null)
        {
            StopCoroutine(typingRoutine);
            typingRoutine = null;
        }

        if (nameText != null && nameText.TryGetComponent(out MorphTypewriterText nameFx)) nameFx.Stop();
        if (descriptionText != null && descriptionText.TryGetComponent(out MorphTypewriterText descFx)) descFx.Stop();
    }

    // Uses a MorphTypewriterText you've added by hand (so you can tweak it in the Inspector),
    // otherwise adds one with default settings.
    private static MorphTypewriterText GetOrAddTypewriter(TMP_Text text)
    {
        if (!text.TryGetComponent(out MorphTypewriterText fx))
            fx = text.gameObject.AddComponent<MorphTypewriterText>();
        return fx;
    }
}