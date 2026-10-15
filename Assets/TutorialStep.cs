using System;
using System.Collections.Generic;
using UnityEngine;

public enum TutorialAction { Move, Combo, SpecialAttack, Dash }

[Serializable]
public sealed class TutorialStep
{
    [Tooltip("The player action required by this row.")]
    [SerializeField] private TutorialAction action;
    [Tooltip("Checklist text. Empty uses an automatic label.")]
    [SerializeField] private string label;
    [Tooltip("Combo only: minimum number of consecutive X attacks in one chain.")]
    [Range(1, 4)][SerializeField] private int comboLength = 4;
    [Tooltip("Combo, Dash and SpecialAttack only: number of qualifying chains or uses. Move uses moveSeconds instead.")]
    [Min(1)][SerializeField] private int requiredCount = 1;
    [Tooltip("Move only: total seconds of left-stick input required; accumulated across movement sessions.")]
    [Min(0.01f)][SerializeField] private float moveSeconds = 1.5f;

    public TutorialAction Action => action;
    public int ComboLength => Mathf.Clamp(comboLength, 1, 4);
    public int RequiredCount => Mathf.Max(1, requiredCount);
    public float MoveSeconds => Mathf.Max(0.01f, moveSeconds);
    public float Target => action == TutorialAction.Move ? MoveSeconds : RequiredCount;

    /// Creates display text once when the checklist is built.
    public string GetLabel()
    {
        if (!string.IsNullOrWhiteSpace(label)) return label;
        switch (action)
        {
            case TutorialAction.Move: return "Move with the left stick";
            case TutorialAction.Combo: return "Perform a " + ComboLength + "-hit X combo";
            case TutorialAction.SpecialAttack: return "Use special attack (Y)";
            default: return "Dash (B)";
        }
    }

    public void Validate()
    {
        comboLength = Mathf.Clamp(comboLength, 1, 4);
        requiredCount = Mathf.Max(1, requiredCount);
        moveSeconds = float.IsNaN(moveSeconds) || float.IsInfinity(moveSeconds)
            ? 1.5f : Mathf.Max(0.01f, moveSeconds);
    }
}

[CreateAssetMenu(fileName = "TutorialObjectives", menuName = "Tutorial/Objective Data")]
public sealed class TutorialObjectiveData : ScriptableObject
{
    [Header("Checklist")]
    [Tooltip("Steps shown in the checklist. Progress is stored on the tracker, never on this asset.")]
    [SerializeField] private List<TutorialStep> steps = new List<TutorialStep>();
    [Tooltip("When enabled, only the first unfinished step can progress.")]
    [SerializeField] private bool requireInOrder = false;

    public IReadOnlyList<TutorialStep> Steps => steps;
    public bool RequireInOrder => requireInOrder;

    private void OnValidate()
    {
        if (steps == null) steps = new List<TutorialStep>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i] == null) steps[i] = new TutorialStep();
            steps[i].Validate();
        }
    }
}