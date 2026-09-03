using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class HubNameNPC : MonoBehaviour
{
    [Header("UI References")]
    public GameObject nameMenuUI;
    public TMP_InputField nameInputField;

    [Header("Inputs")]
    public InputActionReference interactAction; // Map to Y button
    public InputActionReference backAction;     // Map to B button

    [Header("Data")]
    public string savedPlayerName = "Vessel"; 

    private bool isPlayerInRange = false;

    private void Start()
    {
        if (nameMenuUI != null) nameMenuUI.SetActive(false);
    }

    private void OnEnable()
    {
        interactAction.action.Enable();
        interactAction.action.performed += OnInteract;
        
        backAction.action.Enable();
        backAction.action.performed += OnBack;
    }

    private void OnDisable()
    {
        interactAction.action.performed -= OnInteract;
        backAction.action.performed -= OnBack;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) isPlayerInRange = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player")) isPlayerInRange = false;
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        if (!isPlayerInRange) return;

        // Open menu if it's closed
        if (!nameMenuUI.activeSelf)
        {
            nameMenuUI.SetActive(true);
            nameInputField.Select(); // Auto-focuses the text field so the player can type immediately
            nameInputField.ActivateInputField();
        }
    }

    private void OnBack(InputAction.CallbackContext context)
    {
        if (nameMenuUI.activeSelf)
        {
            nameMenuUI.SetActive(false);
            
            if (!string.IsNullOrEmpty(nameInputField.text))
            {
                savedPlayerName = nameInputField.text; 
            }

            // Trigger the local speech generator
            if (LocalTTSOrator.Instance != null)
            {
                LocalTTSOrator.Instance.TriggerNPCScream(savedPlayerName);
            }
        }
    }
}