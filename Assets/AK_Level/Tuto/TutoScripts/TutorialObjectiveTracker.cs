using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(TutorialPlayerObserver))]
public sealed class TutorialObjectiveTracker : MonoBehaviour
{
    [Serializable]
    public sealed class StepCompletedEvent : UnityEvent<int> { }

    [Header("References")]
    [Tooltip("Player whose performed actions are tracked.")]
    [SerializeField] private PlayerController player;
    [Tooltip("Checklist configuration. Keep this asset unchanged during a running tutorial.")]
    [SerializeField] private TutorialObjectiveData data;
    [Tooltip("Checklist renderer, kept separate from this tracker's GameObject.")]
    [SerializeField] private TutorialChecklistUI checklistUI;

    [Header("Lifecycle")]
    [Tooltip("Begin from Start, after scene Awake methods have run.")]
    [SerializeField] private bool startOnStart = true;
    [Tooltip("Scaled seconds to show the last tick before completion fires.")]
    [Min(0f)] [SerializeField] private float delayBeforeCompletion = 1f;

    [Header("Events")]
    [Tooltip("Connect to BeaconWallBarrier.BuildWall.")]
    [SerializeField] private UnityEvent onTutorialStarted = new UnityEvent();
    [Tooltip("Zero-based index of a newly completed step.")]
    [SerializeField] private StepCompletedEvent onStepCompleted = new StepCompletedEvent();
    [Tooltip("Connect to BeaconWallBarrier.Disappear. Fires once per BeginTutorial.")]
    [SerializeField] private UnityEvent onTutorialCompleted = new UnityEvent();

    private TutorialPlayerObserver observer;
    private float[] progress;
    private bool[] done;
    private bool[] comboEligible;
    private int activeStep;
    private int completedSteps;
    private int lastComboHit;
    private int runVersion;
    private bool comboValid;
    private bool running;
    private bool completionPending;
    private bool completionFired;
    private float completionRemaining;
    private Coroutine completionRoutine;

    private void OnEnable()
    {
        Subscribe();
        if (completionPending && !completionFired)
            completionRoutine = StartCoroutine(CompleteAfterDelay());
    }

    private void Start()
    {
        if (startOnStart && progress == null) BeginTutorial();
    }

    private void OnDisable()
    {
        Unsubscribe();
        comboValid = false;
        StopCompletionRoutine();
    }

    private void OnValidate()
    {
        delayBeforeCompletion = float.IsNaN(delayBeforeCompletion) || float.IsInfinity(delayBeforeCompletion)
            ? 1f : Mathf.Max(0f, delayBeforeCompletion);
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (player == null)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Assign the PlayerController reference.", this);
            return;
        }
        observer = GetComponent<TutorialPlayerObserver>();
        if (observer == null)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Add TutorialPlayerObserver to this GameObject.", this);
            return;
        }
        observer.BindPlayer(player);
        observer.OnDashPerformed += HandleDash;
        observer.OnSpecialAttackPerformed += HandleSpecial;
        observer.OnComboChainStarted += HandleChainStarted;
        observer.OnComboHitLanded += HandleComboHit;
    }

    private void Unsubscribe()
    {
        if (ReferenceEquals(observer, null)) return;
        observer.OnDashPerformed -= HandleDash;
        observer.OnSpecialAttackPerformed -= HandleSpecial;
        observer.OnComboChainStarted -= HandleChainStarted;
        observer.OnComboHitLanded -= HandleComboHit;
        observer = null;
    }

    /// Lets a spawner supply the actual player instance before BeginTutorial.
    public void BindPlayer(PlayerController instance)
    {
        Unsubscribe();
        player = instance;
        comboValid = false;
        if (isActiveAndEnabled) Subscribe();
    }

    /// Resets progress and starts one new tutorial run.
    public void BeginTutorial()
    {
        if (!isActiveAndEnabled)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Enable the tracker before beginning.", this);
            return;
        }
        if (player == null || player.InputHandler == null || data == null || checklistUI == null)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Assign player, data and UI; begin after the player's Awake.", this);
            return;
        }
        if (!checklistUI.IsConfigured)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Assign the checklist's row prefab and container.", this);
            return;
        }
        for (int i = 0; i < data.Steps.Count; i++)
        {
            if (data.Steps[i] != null) continue;
            Debug.LogWarning("TutorialObjectiveTracker: Remove null steps from the data asset.", this);
            return;
        }

        StopCompletionRoutine();
        Subscribe();
        int version = ++runVersion;
        progress = new float[data.Steps.Count];
        done = new bool[progress.Length];
        comboEligible = new bool[progress.Length];
        activeStep = completedSteps = lastComboHit = 0;
        comboValid = false;
        completionPending = completionFired = false;
        running = true;
        checklistUI.Build(data);
        checklistUI.Show();
        onTutorialStarted?.Invoke();
        if (version == runVersion) CheckCompletion();
    }

    private bool CanReceive => isActiveAndEnabled && running && player != null && !player.IsMovementLocked && !player.IsDead;
    private bool CanProgress(int index) => !done[index] && (!data.RequireInOrder || index == activeStep);

    private void Update()
    {
        if (!CanReceive || player.InputHandler == null || player.InputHandler.MoveInput.sqrMagnitude <= 0.01f) return;
        int version = runVersion;
        int first = data.RequireInOrder ? activeStep : 0;
        int end = data.RequireInOrder ? Mathf.Min(first + 1, progress.Length) : progress.Length;
        for (int i = first; i < end; i++)
        {
            if (CanProgress(i) && data.Steps[i].Action == TutorialAction.Move)
                AddProgress(i, Time.deltaTime);
            if (version != runVersion || !CanReceive) return;
        }
    }

    private void HandleDash() => CountAction(TutorialAction.Dash);
    private void HandleSpecial() => CountAction(TutorialAction.SpecialAttack);

    private void CountAction(TutorialAction action)
    {
        if (!CanReceive) return;
        int version = runVersion;
        int first = data.RequireInOrder ? activeStep : 0;
        int end = data.RequireInOrder ? Mathf.Min(first + 1, progress.Length) : progress.Length;
        for (int i = first; i < end; i++)
        {
            if (CanProgress(i) && data.Steps[i].Action == action) AddProgress(i, 1f);
            if (version != runVersion || !CanReceive) return;
        }
    }

    private void HandleChainStarted()
    {
        comboValid = CanReceive;
        lastComboHit = 0;
        if (!comboValid) return;
        for (int i = 0; i < comboEligible.Length; i++)
            comboEligible[i] = CanProgress(i) && data.Steps[i].Action == TutorialAction.Combo;
    }

    private void HandleComboHit(int hitNumber)
    {
        if (!CanReceive || !comboValid) return;
        // An interrupted AttackState may resume at a nonzero combat index. That is not a fresh full chain.
        if (hitNumber != lastComboHit + 1)
        {
            comboValid = false;
            return;
        }
        lastComboHit = hitNumber;
        int version = runVersion;
        int first = data.RequireInOrder ? activeStep : 0;
        int end = data.RequireInOrder ? Mathf.Min(first + 1, progress.Length) : progress.Length;
        for (int i = first; i < end; i++)
        {
            if (comboEligible[i] && CanProgress(i) && hitNumber >= data.Steps[i].ComboLength)
            {
                comboEligible[i] = false;
                AddProgress(i, 1f);
            }
            if (version != runVersion || !CanReceive) return;
        }
    }

    private void AddProgress(int index, float amount)
    {
        if (done[index]) return;
        float target = data.Steps[index].Target;
        progress[index] = Mathf.Min(progress[index] + amount, target);
        if (checklistUI != null) checklistUI.SetProgress(index, progress[index], target);
        if (progress[index] < target) return;

        done[index] = true;
        completedSteps++;
        while (activeStep < done.Length && done[activeStep]) activeStep++;
        if (checklistUI != null) checklistUI.SetDone(index);
        int version = runVersion;
        onStepCompleted?.Invoke(index);
        if (version == runVersion) CheckCompletion();
    }

    private void CheckCompletion()
    {
        if (!running || completedSteps != progress.Length) return;
        running = false;
        completionPending = true;
        completionRemaining = Mathf.Max(0f, delayBeforeCompletion);
        if (isActiveAndEnabled) completionRoutine = StartCoroutine(CompleteAfterDelay());
    }

    private IEnumerator CompleteAfterDelay()
    {
        // Even a zero delay leaves one frame for the final tick to render.
        yield return null;
        while (completionRemaining > 0f)
        {
            completionRemaining -= Time.deltaTime;
            if (completionRemaining > 0f) yield return null;
        }
        completionRoutine = null;
        if (!completionPending || completionFired) yield break;
        completionPending = false;
        completionFired = true;
        onTutorialCompleted?.Invoke();
    }

    private void StopCompletionRoutine()
    {
        if (completionRoutine == null) return;
        StopCoroutine(completionRoutine);
        completionRoutine = null;
    }

    [ContextMenu("Complete All Steps")]
    public void CompleteAllSteps()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("TutorialObjectiveTracker: Complete All Steps is a Play Mode test.", this);
            return;
        }
        if (progress == null) BeginTutorial();
        if (!running || !isActiveAndEnabled) return;
        int version = runVersion;
        for (int i = 0; i < progress.Length; i++)
        {
            if (!done[i]) AddProgress(i, data.Steps[i].Target);
            if (version != runVersion || !isActiveAndEnabled) return;
        }
    }
}
