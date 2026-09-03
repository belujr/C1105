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

    private int dmitriHitCount = 0;

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
        if (spawnPoint == null)
        {
            Debug.LogError("HubNarrativeManager: SpawnPoint is not assigned!");
            yield break;
        }

        if (vesselConjureVFX != null) 
            Instantiate(vesselConjureVFX, spawnPoint.position, Quaternion.identity);
        
        yield return new WaitForSeconds(1.5f);
        
        GameObject activePlayer = null;
        if (playerPrefab != null)
        {
            activePlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        }
        else
        {
            Debug.LogError("HubNarrativeManager: PlayerPrefab is not assigned!");
        }

        // Dynamically assign target to IsoCameraRig via Reflection without altering original script files[cite: 1]
        if (activePlayer != null)
        {
            IsoCameraRig camRig = FindObjectOfType<IsoCameraRig>();
            if (camRig != null)
            {
                FieldInfo targetField = typeof(IsoCameraRig).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
                if (targetField != null)
                {
                    targetField.SetValue(camRig, activePlayer.transform);
                }
            }
        }

        string[] c1Lines = { "Go. Kill them all.", "You better be better than the previous one." };
        DialogueLine c1Line = new DialogueLine { speakerName = "C1", text = c1Lines[Random.Range(0, c1Lines.Length)] };

        DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> { c1Line }, () => {
            if (currentRunNumber == 1) run1ProgressStep = 1;
            else run1ProgressStep = 5;
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
                DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber), () => run1ProgressStep = 3);
            }
            else if (npc.npcRole == HubNPC.NPCType.Bhide && run1ProgressStep == 4)
            {
                DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber), () => {
                    if (upgradeShopUI != null) upgradeShopUI.SetActive(true);
                });
            }
            else
            {
                Debug.Log($"Cannot talk to {npc.npcRole} right now. Complete preceding tutorial steps.");
            }
        }
        else
        {
            DialogueUI.Instance.StartSequence(npc.GetDialogueForCurrentRun(currentRunNumber));
        }
    }

    public void OnPlayerHitDmitri()
    {
        if (currentRunNumber != 1 || run1ProgressStep != 3) return;

        dmitriHitCount++;
        string response;

        if (dmitriHitCount <= 5)
        {
            string[] sparringLines = { 
                "You done yet?", 
                "That’s enough beating up I guess…", 
                "Chill bro, go do that in the forest", 
                "If you die after attacking me like this, I’m gonna tell C1 to conjure you without clothes for your next run", 
                "Man, just go already." 
            };
            response = sparringLines[(dmitriHitCount - 1) % sparringLines.Length];
        }
        else
        {
            string[] extraHits = { "Bruh", "I can do this all day", "Here we go again" };
            response = extraHits[Random.Range(0, extraHits.Length)];
        }

        DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> { new DialogueLine { speakerName = "Dmitri", text = response } });
    }

    public void OnCompleteDmitriTutorialArea()
    {
        if (currentRunNumber != 1 || run1ProgressStep != 3) return;

        string[] readyLines = { "You ready?", "All set? To die again?", "Alright" };
        List<DialogueLine> exitLines = new List<DialogueLine> {
            new DialogueLine { speakerName = "Dmitri", text = readyLines[Random.Range(0, readyLines.Length)] },
            new DialogueLine { speakerName = "Dmitri", text = "Good luck out there! Don't die!" }
        };

        DialogueUI.Instance.StartDynamicLines(exitLines, () => run1ProgressStep = 4);
    }

    public void OnCloseUpgradeShopUI()
    {
        if (upgradeShopUI != null) upgradeShopUI.SetActive(false);

        if (currentRunNumber == 1 && run1ProgressStep == 4)
        {
            DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> {
                new DialogueLine { speakerName = "Player", text = "(Is this a grapple?)" }
            }, () => {
                DialogueUI.Instance.StartSequence(bhideFinalLoreSequence, () => {
                    run1ProgressStep = 5;
                });
            });
        }
    }
}