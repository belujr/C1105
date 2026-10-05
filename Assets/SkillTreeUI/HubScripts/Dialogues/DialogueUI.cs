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

    // Static event for player controller subscription
    public static event Action<bool> OnPlayerMovementStateChanged;

    private Queue<DialogueLine> linesQueue = new Queue<DialogueLine>();
    private Action onDialogueComplete;

    private DialogueAdvanceMode currentAdvanceMode = DialogueAdvanceMode.PlayerInteractionBased;
    private DialogueMovementMode currentMovementMode = DialogueMovementMode.Fixed;
    private float currentAutoAdvanceTime = 3.0f;

    private Coroutine autoAdvanceCoroutine;
    private Coroutine promptDelayCoroutine;

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

        onDialogueComplete = callback;
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
        onDialogueComplete = callback;
        currentAdvanceMode = advanceMode;
        currentMovementMode = movementMode;
        currentAutoAdvanceTime = autoAdvanceTime;

        SetPlayerMovementAllowed(currentMovementMode == DialogueMovementMode.Walkable);

        linesQueue.Clear();
        foreach (var line in dynamicLines)
        {
            linesQueue.Enqueue(line);
        }

        dialoguePanel.SetActive(true);
        DisplayNextLine();
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

        if (currentAdvanceMode == DialogueAdvanceMode.PlayerInteractionBased)
        {
            promptDelayCoroutine = StartCoroutine(ShowPromptDelayRoutine(1.0f));
        }
        else if (currentAdvanceMode == DialogueAdvanceMode.Continuous)
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

        onDialogueComplete?.Invoke();
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

        if (dialoguePanel != null && dialoguePanel.activeSelf && advancePressed)
        {
            DisplayNextLine();
        }
    }
}