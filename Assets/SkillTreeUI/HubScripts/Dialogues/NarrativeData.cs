using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class NarrativeStep
{
    public string stepLabel; // Helper name for Inspector organization (e.g., "Step 3 - Bhide Grapple")
    public DialogueSequence dialogueSequence; // The Dialogue ScriptableObject to play
    public HubNPC.NPCType triggerNPC; // NPC required to trigger this dialogue
    public bool autoTriggerOnRunStart; // True for opening dialogues (e.g., C1 opening)
    public DialogueSequence dependency; // Optional: Prerequisite Dialogue SO that MUST be completed first

    [Header("Optional Events")]
    public UnityEvent onStepCompleted; // Triggers shop unlocks, quest updates, etc.
}

[Serializable]
public class RunNarrativeData
{
    public string runName = "Run 1";
    public List<NarrativeStep> steps = new List<NarrativeStep>();
}