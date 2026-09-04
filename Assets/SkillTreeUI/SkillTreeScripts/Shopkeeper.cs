using UnityEngine;
using UnityEngine.InputSystem;

public class Shopkeeper : MonoBehaviour
{
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
            
        if (backActionRef != null && backActionRef.action != null)
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
        // 1. Handle Back button press when the Shop is currently open
        bool backPressed = backActionRef != null && 
                           backActionRef.action != null && 
                           backActionRef.action.WasPressedThisFrame();

        if (backPressed && ComboUIManager.Instance != null && ComboUIManager.Instance.skillTreePanel != null && ComboUIManager.Instance.skillTreePanel.activeSelf)
        {
            ComboUIManager.Instance.CloseMenu();

            // Safely transition narrative step from 5 to 6 so Bhide's final lore is unlocked
            if (HubNarrativeManager.Instance != null && HubNarrativeManager.Instance.currentRunNumber == 1)
            {
                if (HubNarrativeManager.Instance.run1ProgressStep == 5)
                {
                    HubNarrativeManager.Instance.run1ProgressStep = 6;
                    Debug.Log("Back button pressed: Shop closed, narrative advanced to Step 6.");
                }
            }
            return;
        }

        // 2. Handle Interact (Y) button press when player is in the shop range
        if (!isPlayerInRange) return;

        bool interactPressed = interactActionRef != null && 
                               interactActionRef.action != null && 
                               interactActionRef.action.WasPressedThisFrame();

        if (!interactPressed) return;

        if (DialogueUI.Instance != null && DialogueUI.Instance.dialoguePanel != null && DialogueUI.Instance.dialoguePanel.activeSelf)
        {
            return;
        }

        if (HubNarrativeManager.Instance != null && HubNarrativeManager.Instance.currentRunNumber == 1)
        {
            if (HubNarrativeManager.Instance.run1ProgressStep < 5)
            {
                return; 
            }
        }

        if (ComboUIManager.Instance != null && ComboUIManager.Instance.skillTreePanel != null)
        {
            if (!ComboUIManager.Instance.skillTreePanel.activeSelf)
            {
                ComboUIManager.Instance.OpenMenu();
            }
        }
    }
}