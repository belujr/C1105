using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Objective list for the LEVEL scene (same look as the hub tutorial, driven by triggers instead of the camera intro).
///
/// A ChamberObjectiveTrigger hands it a TutorialSequence when the player walks into a chamber. Objectives then appear
/// one at a time. Each objective is one of two kinds (set on the TutorialMissionData asset):
///
///   CONDITION  pop in -> player does it -> glow yellow -> fade to black -> next one
///   TIMED      pop in -> stays for "Display Seconds" -> fade to black -> next one   (e.g. "Clear all the invaders")
///
/// Conditions it can detect from the player (nothing in your player scripts is modified):
///   ChainAttack   - hits in one unbroken hand-to-hand chain (X)
///   SpecialAttack - the Y special / AOE attack
///   Manual        - reported by your own code
///
/// Put it on a GameObject at the ROOT of Scene_Level, NOT under the MapController root (that one is DontDestroyOnLoad).
/// </summary>
[RequireComponent(typeof(TutorialObjectiveHUD))]
public class LevelObjectiveManager : MonoBehaviour
{
    public static LevelObjectiveManager Instance { get; private set; }

    private const string REGISTRY_KEY = "LevelObjectives_Keys";

    [Header("Timing")]
    [Tooltip("Pause after the player enters the chamber before the first objective appears.")]
    [SerializeField, Min(0f)] private float startDelay = 0.4f;
    [SerializeField, Min(0f)] private float gapBetweenObjectives = 0.25f;
    [Tooltip("OFF: the next objective waits until the previous one has faded away. ON: they briefly stack.")]
    [SerializeField] private bool showNextWhileFading = false;

    [Header("Blending")]
    [Tooltip("Fade the objectives out while a dialogue box is open (if the level has a DialogueUI).\n" +
             "Timed objectives also pause their countdown while the dialogue is open.")]
    [SerializeField] private bool hideHudDuringDialogue = true;

    private TutorialObjectiveHUD hud;

    // current sequence
    private Coroutine routine;
    private MonoBehaviour owner;
    private TutorialSequence sequence;
    private PlayerController player;
    private Action onSequenceCompleted;
    private bool persist;
    private string saveKey;
    private int step;

    // current objective
    private TutorialMissionData current;
    private TutorialObjectiveRow currentRow;
    private bool objectiveActive;
    private bool objectiveDone;
    private int count;

    // attack observation (polled after the player's own Update, like the hub tutorial)
    private bool wasAttacking, wasSpecial, chainValid;
    private int lastComboIndex = -1;
    private int chainHits;

    public bool IsRunning => routine != null;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        hud = GetComponent<TutorialObjectiveHUD>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Starts a sequence. Returns false (and does nothing) if one is already running, the sequence is empty, or it was
    /// already finished on this save (when saveProgress is on).
    /// </summary>
    /// <param name="who">The caller, so only it can cancel the sequence.</param>
    /// <param name="completed">Called once when the last objective is completed (or has run out its time). Can be null.</param>
    public bool Begin(MonoBehaviour who, TutorialSequence seq, PlayerController playerController,
                      bool saveProgress, Action completed)
    {
        if (routine != null) return false;
        if (seq == null || seq.objectives == null || seq.objectives.Count == 0 || playerController == null) return false;

        int start = 0;
        string key = null;
        if (saveProgress)
        {
            key = "LevelObjectives_" + seq.name;
            if (PlayerPrefs.GetInt(key + "_Done", 0) == 1) return false;
            start = Mathf.Clamp(PlayerPrefs.GetInt(key + "_Step", 0), 0, seq.objectives.Count - 1);
        }

        owner = who;
        sequence = seq;
        player = playerController;
        persist = saveProgress;
        saveKey = key;
        step = start;
        onSequenceCompleted = completed;
        SyncObservation();

        routine = StartCoroutine(Run());
        return true;
    }

    /// <summary>Stops the sequence if (and only if) <paramref name="who"/> started it. Saved progress is kept.</summary>
    public void Cancel(MonoBehaviour who)
    {
        if (routine == null || !ReferenceEquals(owner, who)) return;
        CancelCurrent();
    }

    /// <summary>
    /// Tell the manager something happened (SpecialAttack, Manual). Ignored unless the CURRENT objective is a
    /// condition objective that uses that condition. Timed objectives never react to reports.
    /// </summary>
    public void Report(TutorialCondition condition)
    {
        if (!objectiveActive || current == null || !current.Matches(condition)) return;
        if (condition == TutorialCondition.ChainAttack) return; // measured from the hit number

        count++;
        if (currentRow != null) currentRow.SetProgress(count, current.requiredCount);
        if (count >= current.requiredCount) CompleteCurrent();
    }

    /// <summary>For UnityEvents: report a Manual condition.</summary>
    public void ReportManual() => Report(TutorialCondition.Manual);

    // ------------------------------------------------------------------ flow

    private IEnumerator Run()
    {
        if (startDelay > 0f) yield return new WaitForSecondsRealtime(startDelay);

        while (step < sequence.objectives.Count)
        {
            TutorialMissionData data = sequence.objectives[step];
            if (data == null)
            {
                Debug.LogWarning($"[LevelObjectives] Objective slot {step} is empty in '{sequence.name}': skipped.", this);
                step++;
                continue;
            }

            current = data;
            count = 0;
            chainHits = 0;
            objectiveDone = false;
            objectiveActive = true;

            TutorialObjectiveRow row = hud.Show(data);
            currentRow = row;

            yield return row.PlayIn();

            if (data.IsTimed)
            {
                // Time based: it only has to be read. Stays for Display Seconds, then completes by itself.
                yield return WaitVisibleSeconds(data.displaySeconds);
                CompleteCurrent();
            }
            else
            {
                // Condition based: stays until the player fulfils it.
                yield return new WaitUntil(() => objectiveDone);
            }

            currentRow = null;

            // The yellow glow means "you did it", so only condition objectives get it.
            if (!data.IsTimed) yield return row.PlayGlow();

            bool more = step < sequence.objectives.Count;
            if (showNextWhileFading && more) StartCoroutine(row.PlayFadeOutAndRemove());
            else yield return row.PlayFadeOutAndRemove();

            if (more && gapBetweenObjectives > 0f) yield return new WaitForSecondsRealtime(gapBetweenObjectives);
        }

        hud.Hide();
        ClearState();
    }

    /// <summary>
    /// Waits <paramref name="seconds"/> of unscaled time. The clock stands still while the HUD is hidden behind a
    /// dialogue, so the player always gets the full time to read it. Ends early if the objective was completed
    /// by other means (debug menu).
    /// </summary>
    private IEnumerator WaitVisibleSeconds(float seconds)
    {
        float t = 0f;
        while (t < seconds && !objectiveDone)
        {
            if (!(hideHudDuringDialogue && DialogueOpen)) t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void CompleteCurrent()
    {
        if (!objectiveActive) return;
        objectiveActive = false;
        objectiveDone = true;
        step++;

        bool finished = step >= sequence.objectives.Count;

        if (persist)
        {
            PlayerPrefs.SetInt(saveKey + "_Step", Mathf.Min(step, sequence.objectives.Count - 1));
            if (finished) PlayerPrefs.SetInt(saveKey + "_Done", 1);
            RegisterKey(saveKey);
            PlayerPrefs.Save();
        }

        if (finished)
        {
            Action callback = onSequenceCompleted;
            onSequenceCompleted = null;
            if (callback != null) callback();
        }
    }

    private void CancelCurrent()
    {
        StopAllCoroutines();
        if (hud != null)
        {
            hud.Clear();
            hud.Hide();
        }
        ClearState();
    }

    private void ClearState()
    {
        routine = null;
        owner = null;
        sequence = null;
        player = null;
        current = null;
        currentRow = null;
        onSequenceCompleted = null;
        objectiveActive = false;
        objectiveDone = false;
    }

    // ------------------------------------------------------------------ detection

    private static bool DialogueOpen =>
        DialogueUI.Instance != null && DialogueUI.Instance.dialoguePanel != null &&
        DialogueUI.Instance.dialoguePanel.activeSelf;

    private bool PlayerCanAct =>
        player != null && player.isActiveAndEnabled && !player.IsMovementLocked && !player.IsDead;

    private void Update()
    {
        if (routine == null) return;

        if (hideHudDuringDialogue) hud.SetVisible(!DialogueOpen);
    }

    private void SyncObservation()
    {
        // Start from the player's current state so an attack that is already running is not counted.
        PlayerState state = player.CurrentState;
        wasAttacking = state != null && state == player.AttackState;
        wasSpecial = state != null && state == player.AOEAttackState;
        lastComboIndex = wasAttacking ? player.CurrentComboIndex : -1;
        chainValid = false;
        chainHits = 0;
    }

    private void LateUpdate()
    {
        if (routine == null || player == null) return;

        PlayerState state = player.CurrentState;
        bool attacking = state != null && state == player.AttackState;
        bool special = state != null && state == player.AOEAttackState;
        int index = player.CurrentComboIndex;

        // Same reading of AttackState as the combo chain itself: it stays in AttackState and moves CurrentComboIndex.
        bool chainStarted = attacking && (!wasAttacking || (index == 0 && lastComboIndex > 0));
        bool hitStarted = attacking && (chainStarted || index != lastComboIndex);
        bool chainEnded = !attacking && wasAttacking;
        bool specialStarted = special && !wasSpecial && player.specialAttackY != null;

        wasAttacking = attacking;
        wasSpecial = special;
        lastComboIndex = attacking ? index : -1;

        if (!PlayerCanAct) return;

        if (chainEnded) ChainBroken();

        if (chainStarted)
        {
            chainValid = true;
            chainHits = 0;
        }

        if (hitStarted)
        {
            int hitNumber = index + 1;
            if (chainValid && hitNumber == chainHits + 1)
            {
                chainHits = hitNumber;
                OnChainHit(chainHits);
            }
            else
            {
                // joined a chain halfway (e.g. resumed at index > 0): not a fresh full chain
                chainValid = false;
                ChainBroken();
            }
        }

        if (specialStarted) Report(TutorialCondition.SpecialAttack);
    }

    private void OnChainHit(int hits)
    {
        if (!objectiveActive || current == null || !current.Matches(TutorialCondition.ChainAttack)) return;

        if (currentRow != null) currentRow.SetProgress(hits, current.requiredCount);
        if (hits >= current.requiredCount) CompleteCurrent();
    }

    private void ChainBroken()
    {
        chainHits = 0;
        if (objectiveActive && current != null && current.Matches(TutorialCondition.ChainAttack) && currentRow != null)
            currentRow.SetProgress(0, current.requiredCount);
    }

    // ------------------------------------------------------------------ save helpers / debug

    private static void RegisterKey(string key)
    {
        string registry = PlayerPrefs.GetString(REGISTRY_KEY, "");
        foreach (string k in registry.Split('|'))
            if (k == key) return;

        PlayerPrefs.SetString(REGISTRY_KEY, string.IsNullOrEmpty(registry) ? key : registry + "|" + key);
    }

    /// <summary>Wipes every saved level-objective sequence. TutorialManager.ResetSavedProgress calls this.</summary>
    public static void ResetSavedProgress()
    {
        string registry = PlayerPrefs.GetString(REGISTRY_KEY, "");
        if (!string.IsNullOrEmpty(registry))
        {
            foreach (string k in registry.Split('|'))
            {
                PlayerPrefs.DeleteKey(k + "_Step");
                PlayerPrefs.DeleteKey(k + "_Done");
            }
        }
        PlayerPrefs.DeleteKey(REGISTRY_KEY);
        PlayerPrefs.Save();
    }

    [ContextMenu("Debug / Complete Current Objective")]
    private void DebugCompleteCurrent()
    {
        if (Application.isPlaying) CompleteCurrent();
    }

    [ContextMenu("Debug / Reset Level Objective Progress")]
    private void DebugReset()
    {
        ResetSavedProgress();
        Debug.Log("[LevelObjectives] Saved level objective progress cleared.", this);
    }
}