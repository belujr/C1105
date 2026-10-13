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
    [Tooltip("Only useful for targets that persist across scenes (assets, or objects on the manager). " +
             "For Hub scene objects (e.g. Milo) use the 'Step Events' list on HubSceneSetup instead.")]
    public UnityEvent onStepCompleted;
}

[Serializable]
public class RunNarrativeData
{
    [Tooltip("Free label for you only - name it anything (Run 0, Run 1 - Awakening, ...). Never read by code.")]
    public string runName;

    [Tooltip("The run these steps belong to. 0 = game start in the Level until the first death, " +
             "1 = after the first death (first Hub visit), 2 = after the second death, ...")]
    public int runNumber;

    public List<NarrativeStep> steps = new List<NarrativeStep>();
}