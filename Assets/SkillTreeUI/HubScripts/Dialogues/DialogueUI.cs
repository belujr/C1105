using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance;

    [Header("Main Dialogue Frame")]
    public GameObject dialoguePanel;
    public Image dialogueBoxImage;

    [Header("Speaker Title Components")]
    public Image titleBackgroundImage;
    public TMP_Text speakerNameText;
    public TMP_Text speakerSubtitleText;
    public Image speakerSpriteImage;
    public TMP_Text dialogueText;

    [Header("Skip Prompt Visuals")]
    [Tooltip("Assign the UI Image / GameObject located at the bottom right corner indicating skip prompt.")]
    public GameObject skipPromptUI;

    [Tooltip("Delay in seconds before the skip prompt arrow appears on screen for a dialogue line.")]
    public float skipPromptDelay = 3.0f;

    // Static event for player controller subscription
    public static event Action<bool> OnPlayerMovementStateChanged;

    private Queue<DialogueLine> linesQueue = new Queue<DialogueLine>();
    private Action onDialogueComplete;

    private DialogueAdvanceMode currentAdvanceMode = DialogueAdvanceMode.PlayerInteractionBased;
    private DialogueMovementMode currentMovementMode = DialogueMovementMode.Fixed;
    private float currentAutoAdvanceTime = 3.0f;

    private Coroutine autoAdvanceCoroutine;
    private Coroutine promptDelayCoroutine;

    // Frame on which the current dialogue was opened (see Update)
    private int lastStartFrame = -1;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        dialoguePanel.SetActive(false);
        if (skipPromptUI != null) skipPromptUI.SetActive(false);
    }

    // Pulls settings directly from the DialogueSequence asset
    public void StartSequence(DialogueSequence sequence, Action callback = null)
    {
        if (sequence == null) return;

        onDialogueComplete = ChainWithInterrupted(callback);
        currentAdvanceMode = sequence.advanceMode;
        currentMovementMode = sequence.movementMode;
        currentAutoAdvanceTime = sequence.autoAdvanceTime;

        // Apply movement restrictions based on sequence asset settings
        SetPlayerMovementAllowed(currentMovementMode == DialogueMovementMode.Walkable);

        linesQueue.Clear();
        foreach (var line in sequence.lines)
        {
            linesQueue.Enqueue(line);
        }

        lastStartFrame = Time.frameCount;
        dialoguePanel.SetActive(true);
        DisplayNextLine();
    }

    public void StartDynamicLines(
        List<DialogueLine> dynamicLines, 
        DialogueAdvanceMode advanceMode = DialogueAdvanceMode.PlayerInteractionBased, 
        DialogueMovementMode movementMode = DialogueMovementMode.Fixed,
        float autoAdvanceTime = 3.0f, 
        Action callback = null)
    {
        onDialogueComplete = ChainWithInterrupted(callback);
        currentAdvanceMode = advanceMode;
        currentMovementMode = movementMode;
        currentAutoAdvanceTime = autoAdvanceTime;

        SetPlayerMovementAllowed(currentMovementMode == DialogueMovementMode.Walkable);

        linesQueue.Clear();
        foreach (var line in dynamicLines)
        {
            linesQueue.Enqueue(line);
        }

        lastStartFrame = Time.frameCount;
        dialoguePanel.SetActive(true);
        DisplayNextLine();
    }

    // If a dialogue is still playing when a new one starts, the old one used to lose its completion
    // callback (so e.g. a narrative step was never marked as completed and the next NPC stayed locked).
    // Now the interrupted dialogue's callback is kept and runs when the new dialogue ends.
    private Action ChainWithInterrupted(Action newCallback)
    {
        bool interrupting = dialoguePanel != null && dialoguePanel.activeSelf;
        if (!interrupting) return newCallback;

        if (onDialogueComplete != null)
            Debug.LogWarning("[DialogueUI] A dialogue started while another was still playing. " +
                             "The first one's completion callback is kept and will run when the new one ends.");

        return onDialogueComplete + newCallback;
    }

    public void DisplayNextLine()
    {
        StopLineCoroutines();
        if (skipPromptUI != null) skipPromptUI.SetActive(false);

        if (linesQueue.Count == 0)
        {
            EndDialogue();
            return;
        }

        DialogueLine currentLine = linesQueue.Dequeue();

        speakerNameText.text = currentLine.SpeakerName;
        if (speakerSubtitleText != null) speakerSubtitleText.text = currentLine.SpeakerSubtitle;

        if (titleBackgroundImage != null) titleBackgroundImage.color = currentLine.AccentColor;

        if (currentLine.speakerProfile != null && currentLine.speakerProfile.overrideTextColor)
        {
            speakerNameText.color = currentLine.speakerProfile.nameTextColor;
            if (speakerSubtitleText != null) speakerSubtitleText.color = currentLine.speakerProfile.subtitleTextColor;
        }

        if (speakerSpriteImage != null)
        {
            if (currentLine.SpeakerSprite != null)
            {
                speakerSpriteImage.gameObject.SetActive(true);
                speakerSpriteImage.sprite = currentLine.SpeakerSprite;
            }
            else
            {
                speakerSpriteImage.gameObject.SetActive(false);
            }
        }

        dialogueText.text = currentLine.GetRandomText();

        // 1. Show skip prompt arrow after skipPromptDelay for ALL modes
        promptDelayCoroutine = StartCoroutine(ShowPromptDelayRoutine(skipPromptDelay));

        // 2. In Continuous mode, also run the auto-advance timer in parallel
        if (currentAdvanceMode == DialogueAdvanceMode.Continuous)
        {
            autoAdvanceCoroutine = StartCoroutine(AutoAdvanceRoutine(currentAutoAdvanceTime));
        }
    }

    private IEnumerator ShowPromptDelayRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (skipPromptUI != null)
        {
            skipPromptUI.SetActive(true);
        }
    }

    private IEnumerator AutoAdvanceRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        DisplayNextLine();
    }

    private void StopLineCoroutines()
    {
        if (promptDelayCoroutine != null)
        {
            StopCoroutine(promptDelayCoroutine);
            promptDelayCoroutine = null;
        }
        if (autoAdvanceCoroutine != null)
        {
            StopCoroutine(autoAdvanceCoroutine);
            autoAdvanceCoroutine = null;
        }
    }

    private void EndDialogue()
    {
        StopLineCoroutines();
        if (skipPromptUI != null) skipPromptUI.SetActive(false);
        dialoguePanel.SetActive(false);

        // Always restore full player movement when dialogue ends
        SetPlayerMovementAllowed(true);

        Action finished = onDialogueComplete;
        onDialogueComplete = null;
        finished?.Invoke();
    }

    private void SetPlayerMovementAllowed(bool allowed)
    {
        OnPlayerMovementStateChanged?.Invoke(allowed);

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            PlayerInput pInput = playerObj.GetComponent<PlayerInput>();
            if (pInput != null)
            {
                if (allowed) pInput.ActivateInput();
                else pInput.DeactivateInput();
            }
        }
    }

    private void Update()
    {
        bool advancePressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
                              (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) ||
                              (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (dialoguePanel != null && dialoguePanel.activeSelf && advancePressed && Time.frameCount != lastStartFrame)
        {
            DisplayNextLine();
        }
    }
}