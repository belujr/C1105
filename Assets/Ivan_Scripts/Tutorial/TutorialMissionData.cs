using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// The two kinds of objective.
/// </summary>
public enum TutorialCompletion
{
    Condition, // stays on screen until the player fulfils the condition (then glows yellow and fades)
    Timed      // just a message: stays for "Display Seconds", then fades out by itself (no glow, nothing to do)
}

/// <summary>What a CONDITION-based objective is waiting for. Ignored by Timed objectives.</summary>
public enum TutorialCondition
{
    Move,    // hold any movement input for "Required Seconds"
    Dash,    // player enters the Dash state "Required Count" times
    Slide,   // player enters the Slide state "Required Count" times
    Portal,  // reported by RunPortal when the player walks through it
    Manual,  // reported by your own code / UnityEvent: TutorialManager.Report(TutorialCondition.Manual)

    // ---- level scene (LevelObjectiveManager) ----
    ChainAttack,   // hand-to-hand: Required Count = hits in ONE unbroken chain (breaks if the player stops attacking)
    SpecialAttack, // the Y special / AOE attack performed Required Count times

    // ---- hub, run 1 (TutorialManager) ----
    DialogueCompleted, // a dialogue sequence (assign it in "Dialogue") has been completed through the narrative system
    FollowMilo,        // Milo has led the player: his 2nd dialogue is over, or he has reached Master Ren
    ShopExit           // the player left the shop (first time only; later exits are ignored)
}

/// <summary>
/// ONE on-screen objective. Create one asset per row:
/// Right-click in Project > Create > Tutorial > Mission Objective.
///
/// Every objective is either
///   Condition - goes away only when the player fulfils the condition, or
///   Timed     - goes away by itself after "Display Seconds".
/// </summary>
[CreateAssetMenu(fileName = "NewTutorialMission", menuName = "Tutorial/Mission Objective")]
public class TutorialMissionData : ScriptableObject
{
    [Header("What the player sees")]
    [Tooltip("OPTIONAL glyph shown before the text (controller button, stick...). Leave empty for text only. " +
             "A white / single-colour glyph gives the cleanest yellow glow on completion.")]
    public Sprite icon;

    [Tooltip("The objective text, e.g. 'to Move'. TextMeshPro rich text works: <b>, <color=#FFD54A>...")]
    [TextArea(1, 2)]
    public string text = "to Move";

    [Header("How it completes")]
    [Tooltip("Condition: stays until the player fulfils the condition below.\n" +
             "Timed: disappears by itself after 'Display Seconds'. The condition settings are ignored.")]
    public TutorialCompletion completion = TutorialCompletion.Condition;

    [Header("Condition based")]
    public TutorialCondition condition = TutorialCondition.Move;

    [Tooltip("Dash / Slide / Special / Manual: how many times. Chain Attack: how many hits in a row. 1 = just once. " +
             "Above 1 a counter (0/3) is shown on the row.")]
    [Min(1)] public int requiredCount = 1;

    [Tooltip("Move only: seconds of movement input needed. 0 = completes on the first step.")]
    [Min(0f)] public float requiredSeconds = 0.75f;

    [Tooltip("Dialogue Completed only: the objective completes when this sequence has been completed (e.g. Trench's " +
             "or Ren's NarrativeStep sequence). If it was already completed when the objective appears, it completes at once.")]
    public DialogueSequence dialogue;

    [Header("Time based")]
    [Tooltip("Timed only: how long the objective stays on screen (counted after it has slid in).")]
    [Min(0.1f)] public float displaySeconds = 5f;

    // ------------------------------------------------------------------ helpers used by the managers / row

    /// <summary>True for objectives that remove themselves after Display Seconds.</summary>
    public bool IsTimed => completion == TutorialCompletion.Timed;

    /// <summary>True only for a CONDITION objective that is waiting for exactly this condition.</summary>
    public bool Matches(TutorialCondition c) => !IsTimed && condition == c;

    /// <summary>Conditions that are counted (Dash x3, 3-hit chain...). The others are one-off events.</summary>
    public bool UsesCount
    {
        get
        {
            switch (condition)
            {
                case TutorialCondition.Dash:
                case TutorialCondition.Slide:
                case TutorialCondition.Manual:
                case TutorialCondition.ChainAttack:
                case TutorialCondition.SpecialAttack:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>How many reports complete it: Required Count for counted conditions, otherwise 1.</summary>
    public int TargetCount => UsesCount ? requiredCount : 1;

    /// <summary>The 0/3 counter only makes sense for counted condition objectives that need more than one.</summary>
    public bool ShowsCounter => !IsTimed && UsesCount && requiredCount > 1;
}

#if UNITY_EDITOR
/// <summary>Shows only the fields that matter for the chosen completion type.</summary>
[CustomEditor(typeof(TutorialMissionData))]
public class TutorialMissionDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("icon"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("text"));

        SerializedProperty completion = serializedObject.FindProperty("completion");
        EditorGUILayout.PropertyField(completion);

        if ((TutorialCompletion)completion.enumValueIndex == TutorialCompletion.Timed)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displaySeconds"));
        }
        else
        {
            SerializedProperty condition = serializedObject.FindProperty("condition");
            EditorGUILayout.PropertyField(condition);

            switch ((TutorialCondition)condition.enumValueIndex)
            {
                case TutorialCondition.Move:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredSeconds"));
                    break;

                case TutorialCondition.DialogueCompleted:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("dialogue"));
                    break;

                case TutorialCondition.FollowMilo:
                    EditorGUILayout.HelpBox("Completes when Milo's 2nd dialogue is over, or when he reaches Master Ren (MiloController).", MessageType.None);
                    break;

                case TutorialCondition.ShopExit:
                    EditorGUILayout.HelpBox("Completes the first time the player leaves the shop (Shopkeeper).", MessageType.None);
                    break;

                case TutorialCondition.Portal:
                    EditorGUILayout.HelpBox("Completed by RunPortal when the player enters the portal.", MessageType.None);
                    break;

                default:
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("requiredCount"));
                    break;
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif