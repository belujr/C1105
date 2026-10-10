using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class HubStatue : MonoBehaviour
{
    [Header("Statue Lore Data")]
    [Tooltip("Assign the StatueInfo asset for this specific statue.")]
    public StatueInfo statueInfo; //[cite: 22]

    [Header("Unique Canvas / Panel Reference")]
    [Tooltip("Assign the unique UI Canvas or Panel for this specific statue.")]
    public GameObject statueCanvasPanel;
    public TMP_Text nameText;
    public TMP_Text descriptionText;

    [Header("Interact Prompt UI")]
    [Tooltip("The floating prompt/icon that appears above the statue when close.")]
    public GameObject interactPromptUI; //[cite: 22]

    [Header("Input Data Reference")]
    public InputActionReference interactActionRef; //[cite: 22]

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
        if (statueCanvasPanel != null) statueCanvasPanel.SetActive(false);
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
            ToggleStatueCanvas();
        }
    }

    private void ToggleStatueCanvas()
    {
        if (statueCanvasPanel == null || statueInfo == null) return;

        bool isActive = statueCanvasPanel.activeSelf;

        if (!isActive)
        {
            // Populate title and dialogue text, then open this statue's canvas
            if (nameText != null) nameText.text = statueInfo.statueName;
            if (descriptionText != null) descriptionText.text = statueInfo.statueDescription;
            
            statueCanvasPanel.SetActive(true);
        }
        else
        {
            // Close the canvas on second press
            statueCanvasPanel.SetActive(false);
        }
    }
}