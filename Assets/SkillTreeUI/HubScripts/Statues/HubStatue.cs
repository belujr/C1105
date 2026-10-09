using UnityEngine;
using UnityEngine.InputSystem;

public class HubStatue : MonoBehaviour
{
    [Header("Statue Lore Data")]
    [Tooltip("Create and assign a StatueInfo asset for this specific statue.")]
    public StatueInfo statueInfo;

    [Header("Interact Prompt UI")]
    [Tooltip("The floating prompt/icon that appears above the statue when close.")]
    public GameObject interactPromptUI;

    [Header("Input Data Reference")]
    public InputActionReference interactActionRef;

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

    private void Start()
    {
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
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

        if (isPlayerInZone && interactPressed)
        {
            if (HubStatueUI.Instance != null && statueInfo != null)
            {
                HubStatueUI.Instance.ToggleStatueMenu(statueInfo);
            }
        }
    }
}