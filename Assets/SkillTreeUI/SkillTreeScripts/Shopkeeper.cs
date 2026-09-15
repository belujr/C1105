using UnityEngine;
using UnityEngine.InputSystem;

public class Shopkeeper : MonoBehaviour
{
    [Header("Narrative Unlock Requirement")]
    [Tooltip("Assign Bhide's dialogue sequence asset here. The shop will stay locked until this sequence is played.")]
    public DialogueSequence requiredDialogueToUnlock;

    [Header("Input Data References")]
    [Tooltip("Drag the 'Interact' action from your PlayerControls asset here.")]
    public InputActionReference interactActionRef;
    [Tooltip("Drag your 'Back/Cancel' action from your PlayerControls asset here.")]
    public InputActionReference backActionRef;

    private bool isPlayerInRange = false;

    private void OnEnable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Enable();
            
        if (backActionRef != null && backActionRef.action != null)
            backActionRef.action.Enable();
    }

    private void OnDisable()
    {
        if (interactActionRef != null && interactActionRef.action != null)
            interactActionRef.action.Disable();
            
        if (backActionRef != null && backActionRef.action.WasPressedThisFrame()) { }
            backActionRef.action.Disable();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInRange = false;
        }
    }

    private void Update()
    {
        // 1. Handle Back/Cancel button press when the Shop UI is active
        bool backPressed = backActionRef != null && 
                           backActionRef.action != null && 
                           backActionRef.action.WasPressedThisFrame();

        if (backPressed && ComboUIManager.Instance != null && ComboUIManager.Instance.skillTreePanel != null && ComboUIManager.Instance.skillTreePanel.activeSelf)
        {
            ComboUIManager.Instance.CloseMenu();
            return;
        }

        // 2. Handle Interact button press when player is in physical range
        if (!isPlayerInRange) return;

        bool interactPressed = interactActionRef != null && 
                               interactActionRef.action != null && 
                               interactActionRef.action.WasPressedThisFrame();

        if (!interactPressed) return;

        // Block interaction if dialogue UI is active
        if (DialogueUI.Instance != null && DialogueUI.Instance.dialoguePanel != null && DialogueUI.Instance.dialoguePanel.activeSelf)
        {
            return;
        }

        TryOpenShop();
    }

    /// <summary>
    /// Opens the shop menu if unlocked. Can be called directly via UnityEvents or NPC interactions.
    /// </summary>
    public void TryOpenShop()
    {
        if (IsUnlocked())
        {
            if (ComboUIManager.Instance != null)
            {
                ComboUIManager.Instance.OpenMenu();
            }
        }
        else
        {
            Debug.Log($"Shopkeeper is locked. Complete dialogue sequence '{requiredDialogueToUnlock.name}' first.");
        }
    }

    public bool IsUnlocked()
    {
        if (requiredDialogueToUnlock == null) return true;
        return HubNarrativeManager.Instance != null && HubNarrativeManager.Instance.IsSequenceCompleted(requiredDialogueToUnlock);
    }
}