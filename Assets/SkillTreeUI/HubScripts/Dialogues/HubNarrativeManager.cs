using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

public class HubNarrativeManager : MonoBehaviour
{
    public static HubNarrativeManager Instance;

    [Header("Save Keys")]
    private const string SAVE_RUN_KEY = "Narrative_CurrentRunNumber";
    private const string SAVE_STEP_KEY = "Narrative_Run1ProgressStep";
    private const string SAVE_SEQUENCES_KEY = "Narrative_CompletedSequences";

    [Header("Legacy Progress Tracking")]
    public int run1ProgressStep = 0; // Maintained for backward compatibility with Shopkeeper.cs

    [Header("Run Configuration")]
    public int currentRunNumber = 1;
    public List<RunNarrativeData> runNarratives = new List<RunNarrativeData>();

    [Header("VFX & Spawn")]
    public GameObject vesselConjureVFX;
    public Transform spawnPoint;
    public GameObject playerPrefab;

    private HashSet<DialogueSequence> completedSequences = new HashSet<DialogueSequence>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Load progress before Start runs
        LoadProgress();
    }

    private void Start()
    {
        StartCoroutine(Sequence_ConjureVessel());
    }

    private IEnumerator Sequence_ConjureVessel()
    {
        if (spawnPoint == null) yield break;

        // 1. Play VFX
        if (vesselConjureVFX != null)
            Instantiate(vesselConjureVFX, spawnPoint.position, Quaternion.identity);

        // 2. Instantiate Player & Trigger Dissolve
        GameObject activePlayer = null;
        if (playerPrefab != null)
        {
            activePlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
            PlayerDissolveController dissolveController = activePlayer.GetComponent<PlayerDissolveController>();
            if (dissolveController != null)
            {
                dissolveController.TriggerDissolveIn();
            }
        }

        // 3. Attach Camera Rig
        if (activePlayer != null)
        {
            IsoCameraRig camRig = Object.FindFirstObjectByType<IsoCameraRig>();
            if (camRig != null)
            {
                FieldInfo targetField = typeof(IsoCameraRig).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                if (targetField != null) targetField.SetValue(camRig, activePlayer.transform);
            }
        }

        // 4. Wait for dissolve completion
        yield return new WaitForSeconds(1.5f);

        // 5. Trigger Opening Step if configured for the current saved run
        NarrativeStep openingStep = GetAutoTriggerStepForCurrentRun();
        if (openingStep != null)
        {
            PlayNarrativeStep(openingStep);
        }
    }

    public void ProcessNPCInteraction(HubNPC npc)
    {
        RunNarrativeData currentRunData = GetCurrentRunData();
        if (currentRunData == null)
        {
            npc.PlayFallbackDialogue();
            return;
        }

        // Search for an eligible step for this NPC
        NarrativeStep validStep = currentRunData.steps.Find(step =>
            step.triggerNPC == npc.npcRole &&
            !completedSequences.Contains(step.dialogueSequence) &&
            IsDependencySatisfied(step)
        );

        if (validStep != null)
        {
            PlayNarrativeStep(validStep);
        }
        else
        {
            npc.PlayFallbackDialogue();
        }
    }

    private void PlayNarrativeStep(NarrativeStep step)
    {
        if (step.dialogueSequence == null) return;

        DialogueUI.Instance.StartSequence(step.dialogueSequence, () =>
        {
            if (!completedSequences.Contains(step.dialogueSequence))
            {
                completedSequences.Add(step.dialogueSequence);
                run1ProgressStep++; 
                SaveProgress(); // Auto-save when step completes
            }

            step.onStepCompleted?.Invoke();
            Debug.Log($"Completed & Saved Narrative Step: {step.stepLabel}");
        });
    }

    private bool IsDependencySatisfied(NarrativeStep step)
    {
        if (step.dependency == null) return true;
        return completedSequences.Contains(step.dependency);
    }

    private NarrativeStep GetAutoTriggerStepForCurrentRun()
    {
        RunNarrativeData currentRunData = GetCurrentRunData();
        if (currentRunData == null) return null;

        return currentRunData.steps.Find(step =>
            step.autoTriggerOnRunStart &&
            !completedSequences.Contains(step.dialogueSequence) &&
            IsDependencySatisfied(step)
        );
    }

    private RunNarrativeData GetCurrentRunData()
    {
        int runIndex = currentRunNumber - 1;
        if (runIndex >= 0 && runIndex < runNarratives.Count)
        {
            return runNarratives[runIndex];
        }
        return null;
    }

    public bool IsSequenceCompleted(DialogueSequence sequence)
    {
        return completedSequences.Contains(sequence);
    }

    #region Save & Load System

    public void AdvanceToNextRun()
    {
        currentRunNumber++;
        SaveProgress();
        Debug.Log($"Advanced to Run {currentRunNumber} and saved state.");
    }

    public void SaveProgress()
    {
        PlayerPrefs.SetInt(SAVE_RUN_KEY, currentRunNumber);
        PlayerPrefs.SetInt(SAVE_STEP_KEY, run1ProgressStep);

        // Serialize completed dialogue sequence asset names into a single string
        List<string> savedNames = new List<string>();
        foreach (DialogueSequence seq in completedSequences)
        {
            if (seq != null) savedNames.Add(seq.name);
        }
        
        string joinedData = string.Join("|", savedNames);
        PlayerPrefs.SetString(SAVE_SEQUENCES_KEY, joinedData);
        PlayerPrefs.Save();
    }

    public void LoadProgress()
    {
        currentRunNumber = PlayerPrefs.GetInt(SAVE_RUN_KEY, 1);
        run1ProgressStep = PlayerPrefs.GetInt(SAVE_STEP_KEY, 0);

        completedSequences.Clear();

        string savedData = PlayerPrefs.GetString(SAVE_SEQUENCES_KEY, "");
        if (string.IsNullOrEmpty(savedData)) return;

        // Build a lookup map of all assigned DialogueSequences across all run steps
        Dictionary<string, DialogueSequence> sequenceMap = new Dictionary<string, DialogueSequence>();
        foreach (var run in runNarratives)
        {
            foreach (var step in run.steps)
            {
                if (step.dialogueSequence != null && !sequenceMap.ContainsKey(step.dialogueSequence.name))
                    sequenceMap.Add(step.dialogueSequence.name, step.dialogueSequence);
                if (step.dependency != null && !sequenceMap.ContainsKey(step.dependency.name))
                    sequenceMap.Add(step.dependency.name, step.dependency);
            }
        }

        // Reconstruct completedHashSet from saved string names
        string[] names = savedData.Split('|');
        foreach (string seqName in names)
        {
            if (sequenceMap.TryGetValue(seqName, out DialogueSequence sequence))
            {
                completedSequences.Add(sequence);
            }
        }
    }

    [ContextMenu("Reset Save Data (Debug)")]
    public void ResetSaveData()
    {
        PlayerPrefs.DeleteKey(SAVE_RUN_KEY);
        PlayerPrefs.DeleteKey(SAVE_STEP_KEY);
        PlayerPrefs.DeleteKey(SAVE_SEQUENCES_KEY);
        PlayerPrefs.Save();
        
        currentRunNumber = 1;
        run1ProgressStep = 0;
        completedSequences.Clear();
        
        Debug.Log("Narrative Save Data Reset.");
    }

    #endregion
}