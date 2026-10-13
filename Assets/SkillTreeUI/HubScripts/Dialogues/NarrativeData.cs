using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class NarrativeStep
{
    public string stepLabel; // Organization label (e.g., "Step 1 - Opening Cutscene")
    public DialogueSequence dialogueSequence;
    public HubNPC.NPCType triggerNPC;
    public bool autoTriggerOnRunStart;
    public DialogueSequence dependency;

    [Header("Optional Events")]
    public UnityEvent onStepCompleted;
}

[Serializable]
public class RunNarrativeData
{
    public string runName = "Run 1";
    public List<NarrativeStep> steps = new List<NarrativeStep>();
}