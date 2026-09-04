using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

public class HubNarrativeManager : MonoBehaviour
{
    public static HubNarrativeManager Instance;

    [Header("Run Data")]
    public int currentRunNumber = 1; 
    public int run1ProgressStep = 0; 

    [Header("VFX & Spawn")]
    public GameObject vesselConjureVFX;
    public Transform spawnPoint;
    public GameObject playerPrefab;

    [Header("UI Windows")]
    public GameObject upgradeShopUI;
    public DialogueSequence bhideFinalLoreSequence;

    private void Awake() 
    { 
        if (Instance == null) Instance = this; 
        else Destroy(gameObject);
    }

    private void Start()
    {
        StartCoroutine(Sequence_ConjureVessel());
    }

    private IEnumerator Sequence_ConjureVessel()
    {
        if (spawnPoint == null) yield break;

        if (vesselConjureVFX != null) 
            Instantiate(vesselConjureVFX, spawnPoint.position, Quaternion.identity);
        
        yield return new WaitForSeconds(1.5f);
        
        GameObject activePlayer = null;
        if (playerPrefab != null)
        {
            activePlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        }

        if (activePlayer != null)
        {
            // Fixed obsolete FindObjectOfType warning
            IsoCameraRig camRig = Object.FindFirstObjectByType<IsoCameraRig>();
            if (camRig != null)
            {
                FieldInfo targetField = typeof(IsoCameraRig).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                if (targetField != null) targetField.SetValue(camRig, activePlayer.transform);
            }
        }

        string[] c1Lines = { "Go. Kill them all.", "You better be better than the previous one." };
        DialogueLine c1Line = new DialogueLine { speakerName = "C1", text = c1Lines[Random.Range(0, c1Lines.Length)] };

        DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> { c1Line }, () => {
            if (currentRunNumber == 1) run1ProgressStep = 1;
            else run1ProgressStep = 7; 
        });
    }

    public void ProcessNPCInteraction(HubNPC npc)
    {
        if (currentRunNumber == 1)
        {
            if (npc.npcRole == HubNPC.NPCType.Popatlal && run1ProgressStep == 1)
            {
                DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber), () => run1ProgressStep = 2);
            }
            else if (npc.npcRole == HubNPC.NPCType.Dmitri && run1ProgressStep == 2)
            {
                DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber), () => run1ProgressStep = 4);
            }
            else if (npc.npcRole == HubNPC.NPCType.Bhide && run1ProgressStep == 4)
            {
                DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber), () => run1ProgressStep = 5);
            }
            else if (npc.npcRole == HubNPC.NPCType.Bhide && run1ProgressStep == 6)
            {
                Debug.Log("Triggering Bhide Final Lore Sequence!");
                DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> {
                    new DialogueLine { speakerName = "Player", text = "(Is this a grapple?)" }
                }, () => {
                    if (bhideFinalLoreSequence != null)
                    {
                        DialogueUI.Instance.StartSequence(bhideFinalLoreSequence, () => {
                            run1ProgressStep = 7; 
                            Debug.Log("Run 1 Tutorial sequence complete.");
                        });
                    }
                    else
                    {
                        Debug.LogError("HubNarrativeManager: Bhide Final Lore Sequence is unassigned in the Inspector!");
                    }
                });
            }
            else
            {
                Debug.Log($"Interaction blocked for {npc.npcRole}. Current Run: {currentRunNumber}, Current Step: {run1ProgressStep}");
            }
        }
        else
        {
            DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber));
        }
    }
}