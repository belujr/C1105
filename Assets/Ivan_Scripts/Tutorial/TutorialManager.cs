using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Objective-style tutorial for the hub (Ved: Recure style). It has TWO phases, each played once:
///
///   Phase 1 - run 0 (<see cref="tutorialRunNumber"/>): the intro (Move, Dash, Slide... Traverse through the portal).
///   Phase 2 - run 1 (<see cref="followUpRunNumber"/>): the player died, came back to the hub, and is walked through
///             Atlas -> Trench -> Milo -> Master Ren -> the dojo -> the portal.
///
/// Flow of a phase:
///   1. waits until the player exists. Phase 1 then waits for the CameraTransitionManager intro to finish; phase 2
///      waits for the opening dialogue (Atlas) of the run to be completed instead
///   2. waits for any open dialogue to close, then a short delay
///   3. shows the objectives of the TutorialSequence one at a time, top-left
///   4. each objective is either CONDITION based (pops in -> waits for the player to do it -> glows yellow -> fades
///      to black -> next one) or TIME based (pops in -> stays for "Display Seconds" -> fades to black -> next one)
///   5. the last objective ("Traverse through the portal") is completed by RunPortal when the player enters it
///
/// Progress is saved to PlayerPrefs after every objective (separately per phase), so a reload resumes where the
/// player was and a finished phase never shows again.
/// </summary>
[RequireComponent(typeof(TutorialObjectiveHUD))]
public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance { get; private set; }

    // Phase 1 keeps its original keys, so saves made before phase 2 existed still work.
    private const string DONE_KEY = "Tutorial_Done";
    private const string STEP_KEY = "Tutorial_Step";
    private const string FOLLOWUP_DONE_KEY = "Tutorial_FollowUp_Done";
    private const string FOLLOWUP_STEP_KEY = "Tutorial_FollowUp_Step";

    [Header("Phase 1 - Run 0 (the intro)")]
    [Tooltip("The ordered objectives (Create > Tutorial > Mission Sequence).")]
    [SerializeField] private TutorialSequence sequence;
    [Tooltip("This phase only exists on this run. 0 = the very first run.")]
    [SerializeField] private int tutorialRunNumber = 0;

    [Header("Phase 2 - Run 1 (back in the hub after the first death)")]
    [Tooltip("Talk to Trench > Follow Milo > Speak with Master Ren > Enter the dojo > Traverse through the portal.\n" +
             "Tools > Tutorial > Create Run 1 Hub Objectives builds and assigns it for you.")]
    [SerializeField] private TutorialSequence followUpSequence;
    [SerializeField] private int followUpRunNumber = 1;
    [Tooltip("OPTIONAL. The dialogue (Atlas's opening) that must finish before the first objective appears. " +
             "Leave empty to use the 'Auto Trigger On Run Start' step of this run in HubNarrativeManager.")]
    [SerializeField] private DialogueSequence openingDialogue;
    [Tooltip("If the opening dialogue never opens within this many seconds, stop waiting for it and start anyway.")]
    [SerializeField, Min(0f)] private float openingDialogueGrace = 6f;

    [Header("Start")]
    [Tooltip("ON: starts by itself once the camera intro (phase 1) or the opening dialogue (phase 2) has finished.\n" +
             "OFF: call BeginTutorial() yourself, e.g. from a NarrativeStep's On Step Completed event.")]
    [SerializeField] private bool autoStart = true;
    [Tooltip("Optional. Found automatically if empty.")]
    [SerializeField] private CameraTransitionManager cameraIntro;
    [Tooltip("Pause after the camera hands over control, before the first objective appears.")]
    [SerializeField, Min(0f)] private float delayAfterIntro = 0.75f;
    [Tooltip("Hold the first objective back while a dialogue is open.")]
    [SerializeField] private bool waitForDialogueToEnd = true;

    [Tooltip("Fade the objectives out while a dialogue box is open and back in when it closes, so the two never overlap.")]
    [SerializeField] private bool hideHudDuringDialogue = true;

    [Header("Detection")]
    [Tooltip("How far the stick must be pushed to count as moving.")]
    [SerializeField, Range(0.05f, 1f)] private float moveDeadzone = 0.2f;

    [Header("Between objectives")]
    [SerializeField, Min(0f)] private float gapBetweenObjectives = 0.25f;
    [Tooltip("OFF: the next objective waits until the previous one has faded away.\n" +
             "ON: it pops in right after the glow, so they briefly stack one below the other.")]
    [SerializeField] private bool showNextWhileFading = false;

    [Header("Events")]
    [SerializeField] private UnityEvent onTutorialStarted = new UnityEvent();
    [Tooltip("Fires the moment the portal objective appears. Use it to switch on portal VFX / the portal collider.")]
    [SerializeField] private UnityEvent onPortalObjectiveReached = new UnityEvent();
    [Tooltip("Fires (once) the moment the last objective is completed.")]
    [SerializeField] private UnityEvent onTutorialCompleted = new UnityEvent();

    private TutorialObjectiveHUD hud;
    private PlayerController player;
    private Coroutine routine;

    private TutorialSequence active; // the sequence of the phase this session runs
    private bool isFollowUp;          // true = phase 2 (run 1)
    private string doneKey, stepKey;

    private bool tutorialActive;   // this session has a tutorial that is not finished yet
    private bool portalReached;    // the portal objective has been shown
    private int step;              // index of the current objective
    private bool resumedHere;      // the current objective is the first of a session that resumed from a save
    private bool shopExited;       // the player has left the shop at least once this session (while a tutorial runs)

    private TutorialMissionData current;
    private TutorialObjectiveRow currentRow;
    private bool objectiveActive;  // accepting progress
    private bool objectiveDone;
    private int count;
    private float moveTime;

    private bool wasDashing, wasSliding;

    // ------------------------------------------------------------------ Public state

    public bool IsRunning => tutorialActive;

    /// <summary>
    /// RunPortal asks this. True when there is no tutorial, or the tutorial has reached the portal objective.
    /// </summary>
    public bool IsPortalOpen => !tutorialActive || portalReached;

    // RunState reads the same PlayerPrefs key as HubNarrativeManager and RunLayer, so "run 0" here is the same
    // run 0 everywhere else (and it works even before HubNarrativeManager has loaded).
    private static int CurrentRun => RunState.Current;

    // ------------------------------------------------------------------ Lifecycle

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

    private void OnEnable()
    {
        Shopkeeper.ShopClosed += HandleShopClosed;
    }

    private void OnDisable()
    {
        Shopkeeper.ShopClosed -= HandleShopClosed;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // The first time only: this just remembers it. The Shop Exit objective reads the flag, so exits after that
    // objective is done do nothing.
    private void HandleShopClosed()
    {
        if (tutorialActive) shopExited = true;
    }

    // Start (not Awake) so HubNarrativeManager's optional save reset has already run.
    private void Start()
    {
        if (Instance != this) return;

        int run = CurrentRun;
        if (run == tutorialRunNumber)
        {
            active = sequence;
            isFollowUp = false;
            doneKey = DONE_KEY;
            stepKey = STEP_KEY;
        }
        else if (run == followUpRunNumber)
        {
            active = followUpSequence;
            isFollowUp = true;
            doneKey = FOLLOWUP_DONE_KEY;
            stepKey = FOLLOWUP_STEP_KEY;
        }
        else
        {
            Debug.Log($"[Tutorial] Run {run} has no tutorial phase (phase 1 = run {tutorialRunNumber}, phase 2 = run {followUpRunNumber}): skipped.", this);
            return;
        }

        if (active == null || active.objectives == null || active.objectives.Count == 0)
        {
            Debug.LogWarning($"[Tutorial] No TutorialSequence (or it is empty) for run {run}: tutorial disabled for this run.", this);
            return;
        }

        if (PlayerPrefs.GetInt(doneKey, 0) == 1)
        {
            Debug.Log($"[Tutorial] Phase for run {run} already completed on this save: skipped.", this);
            return;
        }

        step = Mathf.Clamp(PlayerPrefs.GetInt(stepKey, 0), 0, active.objectives.Count - 1);
        tutorialActive = true;

        if (autoStart) BeginTutorial();
    }

    /// <summary>Starts the tutorial. Safe to call from a UnityEvent; ignored if there is nothing to run.</summary>
    public void BeginTutorial()
    {
        if (!tutorialActive || routine != null) return;
        routine = StartCoroutine(RunTutorial());
    }

    // ------------------------------------------------------------------ Main flow

    private IEnumerator RunTutorial()
    {
        // 1) The hub spawns the player in its Start, so wait for it.
        while (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
            if (player == null) yield return null;
        }

        // 2) Phase 2: wait for the run's opening dialogue (Atlas) to be completed. The hub plays it ~1.5s after the
        //    player dissolves in, so "no dialogue open yet" is NOT enough to know it is over.
        //    Phase 1 (and phase 2 without an opening dialogue): wait for the title camera to finish its curved move.
        DialogueSequence opening = isFollowUp ? FindOpeningDialogue() : null;
        if (opening != null)
        {
            yield return WaitForOpeningDialogue(opening);
        }
        else
        {
            if (cameraIntro == null) cameraIntro = FindFirstObjectByType<CameraTransitionManager>();
            if (cameraIntro != null && cameraIntro.isActiveAndEnabled)
            {
                while (cameraIntro.CurrentState != CameraTransitionManager.CameraState.Gameplay)
                    yield return null;
            }
        }

        // 3) Let any dialogue finish first, then a short breather.
        if (waitForDialogueToEnd)
        {
            while (DialogueOpen) yield return null;
        }
        if (delayAfterIntro > 0f) yield return new WaitForSecondsRealtime(delayAfterIntro);

        onTutorialStarted?.Invoke();

        // 4) One objective at a time.
        bool firstOfSession = true;
        while (step < active.objectives.Count)
        {
            TutorialMissionData data = active.objectives[step];
            if (data == null)
            {
                Debug.LogWarning($"[Tutorial] Objective slot {step} is empty in the sequence: skipped.", this);
                step++;
                continue;
            }

            current = data;
            count = 0;
            moveTime = 0f;
            objectiveDone = false;
            objectiveActive = true;
            resumedHere = firstOfSession && step > 0;
            firstOfSession = false;

            if (data.Matches(TutorialCondition.DialogueCompleted) && data.dialogue == null)
                Debug.LogWarning($"[Tutorial] '{data.name}' is a Dialogue Completed objective with no Dialogue assigned: it will never complete.", data);

            TutorialObjectiveRow row = hud.Show(data);
            currentRow = row;

            if (data.Matches(TutorialCondition.Portal) && !portalReached)
            {
                portalReached = true;
                onPortalObjectiveReached?.Invoke();
            }

            // The HUD is faded out while a dialogue is open, so don't play the pop-in where nobody can see it.
            yield return WaitWhileDialogueHidesHud();
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

            // Completed (progress was already saved in CompleteCurrent). Now the visuals.
            currentRow = null;

            // The yellow glow means "you did it", so only condition objectives get it. If a dialogue opened at the
            // moment of completion (e.g. Milo greeting you right after Trench), let the glow wait until it is visible.
            yield return WaitWhileDialogueHidesHud();
            if (!data.IsTimed) yield return row.PlayGlow();

            bool more = step < active.objectives.Count;
            if (showNextWhileFading && more) StartCoroutine(row.PlayFadeOutAndRemove());
            else yield return row.PlayFadeOutAndRemove();

            if (more && gapBetweenObjectives > 0f) yield return new WaitForSecondsRealtime(gapBetweenObjectives);
        }

        hud.Hide();
        routine = null;
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

    /// <summary>Holds the flow while a dialogue is open (only when the HUD hides itself for dialogues).</summary>
    private IEnumerator WaitWhileDialogueHidesHud()
    {
        if (!hideHudDuringDialogue) yield break;
        while (DialogueOpen) yield return null;
    }

    /// <summary>The 'Auto Trigger On Run Start' dialogue of the current run, unless one is assigned by hand.</summary>
    private DialogueSequence FindOpeningDialogue()
    {
        if (openingDialogue != null) return openingDialogue;

        HubNarrativeManager hub = HubNarrativeManager.Instance;
        int index = CurrentRun - 1;
        if (hub == null || hub.runNarratives == null || index < 0 || index >= hub.runNarratives.Count) return null;

        foreach (NarrativeStep narrativeStep in hub.runNarratives[index].steps)
            if (narrativeStep.autoTriggerOnRunStart && narrativeStep.dialogueSequence != null)
                return narrativeStep.dialogueSequence;

        return null;
    }

    private IEnumerator WaitForOpeningDialogue(DialogueSequence opening)
    {
        HubNarrativeManager hub = HubNarrativeManager.Instance;
        if (hub == null) yield break;

        bool sawOpen = false;
        float waited = 0f;
        while (!hub.IsSequenceCompleted(opening))
        {
            if (DialogueOpen) sawOpen = true;
            else if (sawOpen) break; // it opened and closed without registering: never wait forever
            else
            {
                waited += Time.unscaledDeltaTime;
                if (waited >= openingDialogueGrace)
                {
                    Debug.LogWarning($"[Tutorial] Opening dialogue '{opening.name}' did not start within {openingDialogueGrace}s: starting the objectives anyway.", this);
                    break;
                }
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ Progress detection

    private static bool DialogueOpen =>
        DialogueUI.Instance != null && DialogueUI.Instance.dialoguePanel != null &&
        DialogueUI.Instance.dialoguePanel.activeSelf;

    private bool PlayerCanAct =>
        player != null && player.isActiveAndEnabled && !player.IsMovementLocked && !player.IsDead;

    private void Update()
    {
        if (tutorialActive && hideHudDuringDialogue) hud.SetVisible(!DialogueOpen);

        if (!objectiveActive || current == null || current.IsTimed) return;

        PollWorldConditions();
        if (!objectiveActive) return; // that completed it

        if (!current.Matches(TutorialCondition.Move)) return;
        if (!PlayerCanAct || player.InputHandler == null) return;
        if (player.InputHandler.MoveInput.sqrMagnitude < moveDeadzone * moveDeadzone) return;

        moveTime += Time.deltaTime;
        if (moveTime >= current.requiredSeconds) CompleteCurrent();
    }

    /// <summary>Conditions that are read from the world instead of being reported: dialogues, Milo, the shop.</summary>
    private void PollWorldConditions()
    {
        switch (current.condition)
        {
            case TutorialCondition.DialogueCompleted:
            {
                HubNarrativeManager hub = HubNarrativeManager.Instance;
                if (current.dialogue != null && hub != null && hub.IsSequenceCompleted(current.dialogue))
                    CompleteCurrent();
                break;
            }

            case TutorialCondition.FollowMilo:
            {
                MiloController milo = MiloController.Instance;
                // No Milo in the scene, or this session resumed on this objective and his walk was never restarted
                // (his journey is not saved): don't leave the player stuck on an impossible objective.
                bool lost = milo == null || (resumedHere && !milo.HasBegun);
                if (lost || milo.HasFinishedLeading) CompleteCurrent();
                break;
            }

            case TutorialCondition.ShopExit:
                if (shopExited) CompleteCurrent();
                break;
        }
    }

    // Polled after every Update (like the player's own state machine) so no player script has to change.
    // Edges are tracked all the time, so a dash that was already running when an objective appears is not counted.
    private void LateUpdate()
    {
        // If the player object is ever replaced mid-tutorial, pick the new one up instead of stalling forever.
        if (player == null)
        {
            if (routine != null) player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;
        }

        PlayerState state = player.CurrentState;
        bool dashing = state != null && state == player.DashState;
        bool sliding = state != null && state == player.SlideState;
        bool dashStarted = dashing && !wasDashing;
        bool slideStarted = sliding && !wasSliding;
        wasDashing = dashing;
        wasSliding = sliding;

        if (!PlayerCanAct) return;
        if (dashStarted) Report(TutorialCondition.Dash);
        if (slideStarted) Report(TutorialCondition.Slide);
    }

    /// <summary>
    /// Tell the tutorial something happened. RunPortal calls Report(Portal); your own code or a UnityEvent can call
    /// Report(Manual). Ignored unless the CURRENT objective is a condition objective that uses that condition
    /// (timed objectives never react to reports).
    /// </summary>
    public void Report(TutorialCondition condition)
    {
        if (!objectiveActive || current == null || !current.Matches(condition)) return;
        if (condition == TutorialCondition.Move) return; // Move is measured in Update

        count++;
        int needed = current.TargetCount;
        if (currentRow != null) currentRow.SetProgress(count, needed);
        if (count >= needed) CompleteCurrent();
    }

    /// <summary>For UnityEvents: report a Manual condition.</summary>
    public void ReportManual() => Report(TutorialCondition.Manual);

    private void CompleteCurrent()
    {
        if (!objectiveActive) return;
        objectiveActive = false;
        objectiveDone = true;
        step++;

        bool finished = step >= active.objectives.Count;
        PlayerPrefs.SetInt(stepKey, Mathf.Min(step, active.objectives.Count - 1));
        if (finished) PlayerPrefs.SetInt(doneKey, 1);
        PlayerPrefs.Save();

        if (finished)
        {
            tutorialActive = false; // the portal is open from now on
            onTutorialCompleted?.Invoke();
        }
    }

    // ------------------------------------------------------------------ Save helpers / debug

    /// <summary>Wipes tutorial progress. HubNarrativeManager.ResetSaveData calls this so one reset clears everything.</summary>
    public static void ResetSavedProgress()
    {
        PlayerPrefs.DeleteKey(DONE_KEY);
        PlayerPrefs.DeleteKey(STEP_KEY);
        PlayerPrefs.DeleteKey(FOLLOWUP_DONE_KEY);
        PlayerPrefs.DeleteKey(FOLLOWUP_STEP_KEY);
        PlayerPrefs.Save();
        LevelObjectiveManager.ResetSavedProgress(); // so the one reset line in HubNarrativeManager clears the level objectives too
    }

    [ContextMenu("Debug / Reset Tutorial Progress")]
    private void DebugReset()
    {
        ResetSavedProgress();
        Debug.Log("[Tutorial] Saved tutorial progress cleared. It will run again next Play.", this);
    }

    [ContextMenu("Debug / Complete Current Objective")]
    private void DebugCompleteCurrent()
    {
        if (!Application.isPlaying) return;
        CompleteCurrent();
    }
}