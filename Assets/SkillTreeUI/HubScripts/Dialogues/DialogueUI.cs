using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class DialogueUI : MonoBehaviour
{
    public static DialogueUI Instance;

    [Header("UI Components")]
    public GameObject dialoguePanel;
    public TMP_Text speakerNameText;
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

    // Overload for dynamic on-the-fly lines (like Dmitri's sparring or C1's random conjure)
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
        speakerNameText.text = currentLine.speakerName;
        dialogueText.text = currentLine.text;
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