using UnityEngine;
using UnityEngine.InputSystem;

public class Shopkeeper : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference interactAction; // Drag your Gameplay/Interact action here

    private bool isPlayerInRange = false;

    private void OnEnable()
    {
        if (interactAction != null)
        {
            interactAction.action.Enable();
            interactAction.action.performed += OnInteractPressed;
        }
    }

    private void OnDisable()
    {
        if (interactAction != null)
        {
            interactAction.action.performed -= OnInteractPressed;
        }
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

    private void OnInteractPressed(InputAction.CallbackContext context)
    {
        if (!isPlayerInRange) return;

        // Toggle menu state open or closed when Y is pressed near the shopkeeper
        if (ComboUIManager.Instance.skillTreePanel.activeSelf)
        {
            ComboUIManager.Instance.CloseMenu();
        }
        else
        {
            ComboUIManager.Instance.OpenMenu();
        }
    }
}