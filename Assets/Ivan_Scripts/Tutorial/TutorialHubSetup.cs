#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the RUN 1 hub objectives (Tools > Tutorial > Create Run 1 Hub Objectives).
/// Open the hub scene first. It
///   1. creates the five mission assets + the sequence in Assets/Tutorial/HubRun1
///   2. fills in Trench's and Ren's dialogue from the Run 1 steps of the scene's HubNarrativeManager
///   3. assigns the sequence to the TutorialManager's "Follow Up Sequence" slot
/// Safe to run again: existing assets are kept (only empty dialogue slots are filled) and the sequence order is rebuilt.
/// Wrapped in UNITY_EDITOR, so it can live in any folder.
/// </summary>
public static class TutorialHubSetup
{
    private const string Folder = "Assets/Tutorial/HubRun1";
    private const int RunNumber = 1; // the run these objectives belong to

    [MenuItem("Tools/Tutorial/Create Run 1 Hub Objectives")]
    public static void CreateRun1Objectives()
    {
        EnsureFolder("Assets", "Tutorial");
        EnsureFolder("Assets/Tutorial", "HubRun1");

        HubNarrativeManager hub = Object.FindFirstObjectByType<HubNarrativeManager>();
        if (hub == null)
            Debug.LogWarning("[TutorialSetup] No HubNarrativeManager in the open scene, so the Trench / Ren dialogues " +
                             "could not be filled in. Open the hub scene and run this again, or assign them by hand.");

        DialogueSequence trench = FindStepDialogue(hub, HubNPC.NPCType.Trench);
        DialogueSequence ren = FindStepDialogue(hub, HubNPC.NPCType.Ren);

        TutorialMissionData talk = Mission("Mission_TalkToTrench", "Talk to Trench, the Bearer of the Dead",
                                           TutorialCondition.DialogueCompleted, trench);
        TutorialMissionData follow = Mission("Mission_FollowMilo", "Follow Milo",
                                             TutorialCondition.FollowMilo, null);
        TutorialMissionData speak = Mission("Mission_SpeakWithRen", "Speak with Master Ren",
                                            TutorialCondition.DialogueCompleted, ren);
        TutorialMissionData dojo = Mission("Mission_EnterDojo", "Enter Master Ren's Dojo and select an attack chain",
                                           TutorialCondition.ShopExit, null);
        TutorialMissionData portal = Mission("Mission_TraversePortal_Run1", "Traverse through the portal",
                                             TutorialCondition.Portal, null);

        string sequencePath = Folder + "/HubRun1_Sequence.asset";
        TutorialSequence sequence = AssetDatabase.LoadAssetAtPath<TutorialSequence>(sequencePath);
        if (sequence == null)
        {
            sequence = ScriptableObject.CreateInstance<TutorialSequence>();
            AssetDatabase.CreateAsset(sequence, sequencePath);
        }
        sequence.objectives = new List<TutorialMissionData> { talk, follow, speak, dojo, portal };
        EditorUtility.SetDirty(sequence);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        TutorialManager manager = Object.FindFirstObjectByType<TutorialManager>();
        if (manager != null)
        {
            SerializedObject so = new SerializedObject(manager);
            so.FindProperty("followUpSequence").objectReferenceValue = sequence;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
        else
        {
            Debug.LogWarning("[TutorialSetup] No TutorialManager in the open scene: assign '" + sequence.name +
                             "' to its 'Follow Up Sequence' slot yourself.");
        }

        Debug.Log("[TutorialSetup] Run " + RunNumber + " hub objectives ready in " + Folder + ":\n" +
                  "  Talk to Trench   -> dialogue: " + Describe(talk.dialogue) + "\n" +
                  "  Follow Milo      -> completes when Milo's 2nd dialogue ends / he reaches Ren\n" +
                  "  Speak with Ren   -> dialogue: " + Describe(speak.dialogue) + "\n" +
                  "  Enter the dojo   -> completes on the first shop exit\n" +
                  "  Traverse portal  -> completed by RunPortal\n" +
                  (manager != null ? "Assigned to the TutorialManager in the scene." : ""), sequence);

        Selection.activeObject = sequence;
        EditorGUIUtility.PingObject(sequence);
    }

    // ------------------------------------------------------------------ helpers

    private static TutorialMissionData Mission(string assetName, string text, TutorialCondition condition, DialogueSequence dialogue)
    {
        string path = Folder + "/" + assetName + ".asset";
        TutorialMissionData data = AssetDatabase.LoadAssetAtPath<TutorialMissionData>(path);

        if (data == null)
        {
            data = ScriptableObject.CreateInstance<TutorialMissionData>();
            data.text = text;
            data.completion = TutorialCompletion.Condition;
            data.condition = condition;
            data.requiredCount = 1;
            AssetDatabase.CreateAsset(data, path);
        }

        // Never overwrite a dialogue you assigned by hand; only fill an empty slot.
        if (dialogue != null && data.dialogue == null) data.dialogue = dialogue;

        EditorUtility.SetDirty(data);
        return data;
    }

    /// <summary>The first step of this run's narrative that this NPC triggers.</summary>
    private static DialogueSequence FindStepDialogue(HubNarrativeManager hub, HubNPC.NPCType npc)
    {
        if (hub == null || hub.runNarratives == null) return null;

        int index = RunNumber - 1;
        if (index < 0 || index >= hub.runNarratives.Count) return null;

        foreach (NarrativeStep step in hub.runNarratives[index].steps)
            if (step.triggerNPC == npc && step.dialogueSequence != null)
                return step.dialogueSequence;

        return null;
    }

    private static string Describe(DialogueSequence dialogue)
    {
        return dialogue != null ? "'" + dialogue.name + "'" : "NOT FOUND - assign it on the asset";
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            AssetDatabase.CreateFolder(parent, name);
    }
}
#endif