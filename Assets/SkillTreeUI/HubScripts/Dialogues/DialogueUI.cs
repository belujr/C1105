using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance;

    [Header("Main Dialogue Frame")]
    public GameObject dialoguePanel;
    public Image dialogueBoxImage; // Static background frame sprite (does not change color)

    [Header("Speaker Title Components")]
    public Image titleBackgroundImage; // White sprite background for title & subtitle
    public TMP_Text speakerNameText;
    public TMP_Text speakerSubtitleText;
    public Image speakerSpriteImage;
    public TMP_Text dialogueText;

    private Queue<DialogueLine> linesQueue = new Queue<DialogueLine>();
    private Action onDialogueComplete;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        dialoguePanel.SetActive(false);
    }

    public void StartSequence(DialogueSequence sequence, Action callback = null)
    {
        onDialogueComplete = callback;
        linesQueue.Clear();

        foreach (var line in sequence.lines)
        {
            linesQueue.Enqueue(line);
        }

        dialoguePanel.SetActive(true);
        DisplayNextLine();
    }

    public void StartDynamicLines(List<DialogueLine> dynamicLines, Action callback = null)
    {
        onDialogueComplete = callback;
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
        if (linesQueue.Count == 0)
        {
            EndDialogue();
            return;
        }

        DialogueLine currentLine = linesQueue.Dequeue();

        // 1. Update Speaker Text & Subtitle
        speakerNameText.text = currentLine.SpeakerName;
        if (speakerSubtitleText != null)
        {
            speakerSubtitleText.text = currentLine.SpeakerSubtitle;
        }

        // 2. Apply Dynamic HSB Accent Color to Title Background Sprite
        if (titleBackgroundImage != null)
        {
            titleBackgroundImage.color = currentLine.AccentColor;
        }

        // 3. Handle Text Color Overrides if specified on Speaker Profile
        if (currentLine.speakerProfile != null && currentLine.speakerProfile.overrideTextColor)
        {
            speakerNameText.color = currentLine.speakerProfile.nameTextColor;
            if (speakerSubtitleText != null)
                speakerSubtitleText.color = currentLine.speakerProfile.subtitleTextColor;
        }

        // 4. Update Speaker Portrait Sprite
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

        // 5. Display Body Dialogue Text
        dialogueText.text = currentLine.GetRandomText();
    }

    private void EndDialogue()
    {
        dialoguePanel.SetActive(false);
        onDialogueComplete?.Invoke();
    }

    private void Update()
    {
        bool advancePressed = (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame) ||
                              (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame) ||
                              (UnityEngine.InputSystem.Gamepad.current != null && UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame);

        if (dialoguePanel != null && dialoguePanel.activeSelf && advancePressed)
        {
            DisplayNextLine();
        }
    }
}