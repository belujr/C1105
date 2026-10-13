using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent narrative / run service (same pattern as SoulManager).
/// Place ONE instance on a root GameObject in the FIRST scene (Scene_Level). It survives every scene load.
///
/// Run model:
///   Run 0 = game start in the Level until the first death.
///   Each death increments the run; Run N begins when the player arrives in the Hub after death N.
///   The steps used are the ones inside the runNarratives element whose "Run Number" equals the current run.
///
/// It listens to PlayerHealth.Died itself, so a death is counted in whichever scene it happens in.
/// Hub-scene objects (spawn point, VFX, player prefab, Milo events) live in HubSceneSetup, not here,
/// because a persistent object cannot hold references to objects of another scene.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class HubNarrativeManager : MonoBehaviour
{
    public static HubNarrativeManager Instance { get; private set; }

    /// <summary>Fired with the new run number whenever the run changes.</summary>
    public static event System.Action<int> RunNumberChanged;

    [Header("Debug & Testing")]
    [Tooltip("Wipes narrative save data when you hit Play. Editor only.")]
    public bool resetSaveDataOnStart = false;

    [Tooltip("LIVE run number. While playing, type a new value here to jump to that run: that run's steps (and later runs) " +
             "are reset so they play again.")]
    [SerializeField, Min(0)] private int currentRunNumber = 0;

    [Tooltip("When jumping to run N, treat every step of runs below N as already completed " +
             "(so cross-run dependencies are satisfied).")]
    public bool markEarlierRunsCompleteWhenJumping = true;

    [Tooltip("READ-ONLY info: steps completed during the current run.")]
    [SerializeField] private int stepsCompletedThisRun = 0;

    [Header("Scenes")]
    [Tooltip("Scene that is loaded after the player dies.")]
    public string hubSceneName = "Scene_Hub";

    [Header("Run Configuration")]
    [Tooltip("One element per run. Set 'Run Number' on each element; name them whatever you like.")]
    public List<RunNarrativeData> runNarratives = new List<RunNarrativeData>();

    // ------------------------------------------------------------------ internals
    private const string SAVE_RUN_KEY = "Narrative_CurrentRunNumber";
    private const string SAVE_STEP_KEY = "Narrative_RunProgressStep";
    private const string SAVE_SEQUENCES_KEY = "Narrative_CompletedSteps_v2";
    private const string KEY_SEPARATOR = "::";

    // "run::sequenceName" = completed in that specific run
    private readonly HashSet<string> completedKeys = new HashSet<string>();
    // "sequenceName" = completed in any run
    private readonly HashSet<string> completedNames = new HashSet<string>();

    private int appliedRunNumber;
    private bool jumpRequested;

    // ------------------------------------------------------------------ static helpers
    public static int CurrentRun => Instance != null ? Instance.currentRunNumber : PlayerPrefs.GetInt(SAVE_RUN_KEY, 0);
    public static int DeathCount => CurrentRun;
    public static string HubSceneNameOrDefault => Instance != null ? Instance.hubSceneName : "Scene_Hub";

    // Keeps static state clean when "Enter Play Mode > Reload Domain" is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        RunNumberChanged = null;
    }

    // ------------------------------------------------------------------ lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject); // must be a ROOT GameObject

        if (resetSaveDataOnStart && Application.isEditor) ClearSave();
        else LoadState();

        appliedRunNumber = currentRunNumber;
        PlayerHealth.Died += HandlePlayerDied;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        PlayerHealth.Died -= HandlePlayerDied;
        Instance = null;
    }

    private void Update()
    {
        // Inspector edits are applied here (not inside OnValidate) so it is safe to touch dialogue/UI.
        if (jumpRequested)
        {
            jumpRequested = false;
            JumpToRun(currentRunNumber);
        }
    }

    private void OnValidate()
    {
        AutoNumberRuns();

        if (Application.isPlaying && Instance == this && currentRunNumber != appliedRunNumber)
            jumpRequested = true;
    }

    // ------------------------------------------------------------------ death -> run increment
    private void HandlePlayerDied(PlayerHealth player)
    {
        if (HubSceneSetup.Current != null)
        {
            Debug.LogWarning("[NarrativeManager] Player died inside the Hub - not counted as a run.");
            return;
        }

        int endedRun = currentRunNumber;
        currentRunNumber++;
        appliedRunNumber = currentRunNumber;
        stepsCompletedThisRun = 0;
        SaveState();

        Debug.Log($"[NarrativeManager] Player died. Run {endedRun} ended -> Run {currentRunNumber} will start at the Hub. (Deaths: {currentRunNumber})");
        RunNumberChanged?.Invoke(currentRunNumber);
    }

    // ------------------------------------------------------------------ narrative flow
    /// <summary>Plays this run's auto-trigger step (if any). Called by HubSceneSetup once the player has spawned.</summary>
    public void PlayOpeningStep()
    {
        if (HubSceneSetup.Current == null) return; // the opening only plays in the Hub
        if (DialogueUI.Instance != null && DialogueUI.Instance.dialoguePanel.activeSelf) return;

        RunNarrativeData data = GetCurrentRunData();
        if (data == null) return;

        NarrativeStep opening = data.steps.Find(s => s.autoTriggerOnRunStart && IsStepAvailable(s));
        if (opening != null) PlayNarrativeStep(opening);
    }

    public bool HasActiveStep(HubNPC.NPCType role)
    {
        RunNarrativeData data = GetCurrentRunData();
        return data != null && data.steps.Exists(s => IsStepAvailable(s) && s.triggerNPC == role);
    }

    public void ProcessNPCInteraction(HubNPC npc)
    {
        RunNarrativeData data = GetCurrentRunData();
        if (data == null)
        {
            Debug.LogWarning($"[NarrativeManager] No element with Run Number {currentRunNumber} in runNarratives. Using fallback dialogue.");
            npc.PlayFallbackDialogue();
            return;
        }

        NarrativeStep step = data.steps.Find(s => IsStepAvailable(s) && s.triggerNPC == npc.npcRole);
        if (step != null) PlayNarrativeStep(step);
        else npc.PlayFallbackDialogue();
    }

    private void PlayNarrativeStep(NarrativeStep step)
    {
        if (step == null || step.dialogueSequence == null || DialogueUI.Instance == null) return;

        int runAtStart = currentRunNumber;
        DialogueSequence sequence = step.dialogueSequence;

        DialogueUI.Instance.StartSequence(sequence, () =>
        {
            if (MarkCompleted(runAtStart, sequence))
            {
                if (runAtStart == currentRunNumber) stepsCompletedThisRun++;
                SaveState();
            }

            step.onStepCompleted?.Invoke();
            if (HubSceneSetup.Current != null) HubSceneSetup.Current.InvokeStepEvents(sequence, runAtStart);

            Debug.Log($"[NarrativeManager] Run {runAtStart} - Completed & Saved Step: {step.stepLabel}");
        });
    }

    private bool IsStepAvailable(NarrativeStep step)
    {
        if (step == null || step.dialogueSequence == null) return false;
        if (completedKeys.Contains(MakeKey(currentRunNumber, step.dialogueSequence.name))) return false;
        return IsDependencySatisfied(step);
    }

    private bool IsDependencySatisfied(NarrativeStep step)
    {
        if (step.dependency == null) return true;

        string depName = step.dependency.name;
        RunNarrativeData data = GetCurrentRunData();

        // Dependency is itself a step of THIS run -> it must be completed in this run.
        // Otherwise it is a "story so far" dependency -> completed in any earlier run is fine.
        bool belongsToThisRun = data != null &&
            data.steps.Exists(s => s != null && s.dialogueSequence == step.dependency);

        return belongsToThisRun
            ? completedKeys.Contains(MakeKey(currentRunNumber, depName))
            : completedNames.Contains(depName);
    }

    private RunNarrativeData GetCurrentRunData()
    {
        for (int i = 0; i < runNarratives.Count; i++)
        {
            if (runNarratives[i] != null && runNarratives[i].runNumber == currentRunNumber)
                return runNarratives[i];
        }
        return null;
    }

    public bool IsSequenceCompleted(DialogueSequence sequence)
        => sequence != null && completedNames.Contains(sequence.name);

    public bool IsSequenceCompletedThisRun(DialogueSequence sequence)
        => sequence != null && completedKeys.Contains(MakeKey(currentRunNumber, sequence.name));

    // ------------------------------------------------------------------ jumping between runs (testing)
    /// <summary>Jumps to a run: resets that run and every later run so their steps play again.</summary>
    public void JumpToRun(int run)
    {
        run = Mathf.Max(0, run);
        if (!Application.isPlaying) LoadCompletedKeys();

        completedKeys.RemoveWhere(k => ParseRunFromKey(k) >= run);

        if (markEarlierRunsCompleteWhenJumping)
        {
            foreach (RunNarrativeData data in runNarratives)
            {
                if (data == null || data.runNumber >= run) continue;
                foreach (NarrativeStep step in data.steps)
                    if (step != null && step.dialogueSequence != null)
                        completedKeys.Add(MakeKey(data.runNumber, step.dialogueSequence.name));
            }
        }

        RebuildNames();
        currentRunNumber = run;
        appliedRunNumber = run;
        stepsCompletedThisRun = 0;
        SaveState();

        Debug.Log($"[NarrativeManager] Jumped to Run {run}. Steps for Run {run}+ were reset.");
        RunNumberChanged?.Invoke(run);

        if (Application.isPlaying) PlayOpeningStep(); // only does something when you are standing in the Hub
    }

    // ------------------------------------------------------------------ save system
    private static string MakeKey(int run, string sequenceName) => run + KEY_SEPARATOR + sequenceName;

    private static int ParseRunFromKey(string key)
    {
        int sep = key.IndexOf(KEY_SEPARATOR, System.StringComparison.Ordinal);
        if (sep <= 0) return -1;
        return int.TryParse(key.Substring(0, sep), out int run) ? run : -1;
    }

    private bool MarkCompleted(int run, DialogueSequence sequence)
    {
        bool added = completedKeys.Add(MakeKey(run, sequence.name));
        completedNames.Add(sequence.name);
        return added;
    }

    private void RebuildNames()
    {
        completedNames.Clear();
        foreach (string key in completedKeys)
        {
            int sep = key.IndexOf(KEY_SEPARATOR, System.StringComparison.Ordinal);
            if (sep >= 0) completedNames.Add(key.Substring(sep + KEY_SEPARATOR.Length));
        }
    }

    private void LoadState()
    {
        currentRunNumber = Mathf.Max(0, PlayerPrefs.GetInt(SAVE_RUN_KEY, 0));
        stepsCompletedThisRun = PlayerPrefs.GetInt(SAVE_STEP_KEY, 0);
        LoadCompletedKeys();
    }

    private void LoadCompletedKeys()
    {
        completedKeys.Clear();
        string saved = PlayerPrefs.GetString(SAVE_SEQUENCES_KEY, "");
        if (!string.IsNullOrEmpty(saved))
        {
            foreach (string key in saved.Split('|'))
                if (!string.IsNullOrEmpty(key)) completedKeys.Add(key);
        }
        RebuildNames();
    }

    private void SaveState()
    {
        PlayerPrefs.SetInt(SAVE_RUN_KEY, currentRunNumber);
        PlayerPrefs.SetInt(SAVE_STEP_KEY, stepsCompletedThisRun);
        PlayerPrefs.SetString(SAVE_SEQUENCES_KEY, string.Join("|", completedKeys));
        PlayerPrefs.Save();
    }

    private void ClearSave()
    {
        PlayerPrefs.DeleteKey(SAVE_RUN_KEY);
        PlayerPrefs.DeleteKey(SAVE_STEP_KEY);
        PlayerPrefs.DeleteKey(SAVE_SEQUENCES_KEY);
        PlayerPrefs.Save();

        currentRunNumber = 0;
        appliedRunNumber = 0;
        stepsCompletedThisRun = 0;
        completedKeys.Clear();
        completedNames.Clear();

        Debug.Log("[NarrativeManager] Save Data Reset Successfully (Run 0).");
    }

    // ------------------------------------------------------------------ run numbering helper
    /// <summary>
    /// Keeps Run Numbers sane without touching the names you type:
    ///  - brand-new data (every element still 0) gets numbered by list order,
    ///  - an element you just added with "+" (Unity clones the previous one) gets the next free number.
    /// </summary>
    private void AutoNumberRuns()
    {
        if (runNarratives == null || runNarratives.Count == 0) return;

        bool allZero = true;
        foreach (RunNarrativeData r in runNarratives)
            if (r != null && r.runNumber != 0) { allZero = false; break; }

        if (allZero && runNarratives.Count > 1)
        {
            for (int i = 0; i < runNarratives.Count; i++)
                if (runNarratives[i] != null) runNarratives[i].runNumber = i;
            return;
        }

        int last = runNarratives.Count - 1;
        RunNarrativeData lastData = runNarratives[last];
        if (lastData == null) return;

        int max = -1;
        bool duplicate = false;
        for (int i = 0; i < runNarratives.Count; i++)
        {
            if (runNarratives[i] == null) continue;
            if (runNarratives[i].runNumber < 0) runNarratives[i].runNumber = 0;
            max = Mathf.Max(max, runNarratives[i].runNumber);
            if (i != last && runNarratives[i].runNumber == lastData.runNumber) duplicate = true;
        }
        if (duplicate) lastData.runNumber = max + 1;
    }

    // ------------------------------------------------------------------ context menus
    [ContextMenu("Jump To Current Run Number (Debug)")]
    private void ContextJump() => JumpToRun(currentRunNumber);

    [ContextMenu("Reset Save Data (Debug)")]
    public void ResetSaveData()
    {
        ClearSave();
        RunNumberChanged?.Invoke(0);
    }

    [ContextMenu("Auto-Number Runs From List Order")]
    private void ContextAutoNumber()
    {
        for (int i = 0; i < runNarratives.Count; i++)
            if (runNarratives[i] != null) runNarratives[i].runNumber = i;
    }
}