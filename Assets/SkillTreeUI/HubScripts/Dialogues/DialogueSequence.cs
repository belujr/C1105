using System.Collections.Generic;
using UnityEngine;

public enum DialogueAdvanceMode
{
    PlayerInteractionBased,
    Continuous
}

public enum DialogueMovementMode
{
    Fixed,   // Player cannot move until dialogue finishes
    Walkable // Player can move and explore freely while dialogue plays
}

[CreateAssetMenu(fileName = "NewDialogueSequence", menuName = "Dialogue/Dialogue Sequence")]
public class DialogueSequence : ScriptableObject
{
    [Header("Progression & Movement Settings")]
    [Tooltip("Choose whether lines require manual input or auto-advance on a timer.")]
    public DialogueAdvanceMode advanceMode = DialogueAdvanceMode.PlayerInteractionBased;

    [Tooltip("Choose whether the player is locked in place or free to move during dialogue.")]
    public DialogueMovementMode movementMode = DialogueMovementMode.Fixed;

    [Tooltip("Duration (in seconds) each line plays before auto-skipping. Active only when Continuous mode is selected.")]
    public float autoAdvanceTime = 3.0f;

    [Header("Dialogue Lines")]
    public List<DialogueLine> lines = new List<DialogueLine>();
}