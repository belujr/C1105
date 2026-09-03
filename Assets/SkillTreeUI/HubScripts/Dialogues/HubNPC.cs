using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.Events;

public class HubNPC : MonoBehaviour
{
    public enum NPCType { Popatlal, Dmitri, Bhide, Other }
    
    public NPCType npcRole;
    public GameObject interactPromptUI;
    
    [Header("Input Data Reference")]
    [Tooltip("Drag the 'Interact' action from your PlayerControls asset here.")]
    public InputActionReference interactActionRef;
    
    [Header("Dialogue Data")]
    [Tooltip("Index 0 = Run 1. Index 1 = Run 2...")]
    public List<DialogueSequence> dialoguesByRun;
    public DialogueSequence defaultAmbientDialogue;

    public UnityEvent onInteracted;
    private bool isPlayerInZone;

    private void OnEnable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Enable();
    }

    private void OnDisable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Disable();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInZone = true;
            if (interactPromptUI != null) interactPromptUI.SetActive(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInZone = false;
            if (interactPromptUI != null) interactPromptUI.SetActive(false);
        }
    }

    private void Update()
    {
        // Check if the assigned Interact action asset was pressed this frame
        bool interactPressed = interactActionRef != null && 
                               interactActionRef.action != null && 
                               interactActionRef.action.WasPressedThisFrame();

        if (isPlayerInZone && interactPressed && DialogueUI.Instance != null && !DialogueUI.Instance.dialoguePanel.activeSelf)
        {
            Interact();
        }
    }

    private void Interact()
    {
        HubNarrativeManager.Instance.ProcessNPCInteraction(this);
        onInteracted?.Invoke();
    }

    public DialogueSequence GetDialogueForCurrentRun(int runIndex)
    {
        int arrayIndex = runIndex - 1;
        if (arrayIndex >= 0 && arrayIndex < dialoguesByRun.Count && dialoguesByRun[arrayIndex] != null)
        {
            return dialoguesByRun[arrayIndex];
        }
        return defaultAmbientDialogue;
    }
}