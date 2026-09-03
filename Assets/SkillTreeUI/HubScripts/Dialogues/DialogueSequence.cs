using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewDialogueSequence", menuName = "Narrative/Dialogue Sequence")]
public class DialogueSequence : ScriptableObject
{
    public List<DialogueLine> lines;
}