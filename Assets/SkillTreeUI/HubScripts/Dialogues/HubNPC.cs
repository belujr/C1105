using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class HubNPC : MonoBehaviour
{
    public enum NPCType { Trench, Milo, Ren, C1, Other }

    public NPCType npcRole;
    public GameObject interactPromptUI;

    [Header("Input Data Reference")]
    public InputActionReference interactActionRef;

    [Header("Fallback Dialogue")]
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

    public void PlayFallbackDialogue()
    {
        if (defaultAmbientDialogue != null)
        {
            DialogueUI.Instance.StartSequence(defaultAmbientDialogue);
        }
    }
}