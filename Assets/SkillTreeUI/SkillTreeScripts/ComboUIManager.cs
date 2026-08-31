using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement; // Added for Scene transitions
using System.Collections.Generic;
using System.Collections;

public class ComboUIManager : MonoBehaviour
{
    public static ComboUIManager Instance;

    [Header("UI Panel & Inputs")]
    public GameObject skillTreePanel; 
    public InputActionReference backAction; // Drag UI/Cancel here
    [Tooltip("Drag your PlayerControls Input Asset here from the Project window")]
    public InputActionAsset inputActions; 

    [Header("Player Data References")]
    public CombatStyle playerCombatStyle; 

    [Header("UI References")]
    public UIComboSlot[] comboSlots; 
    public Transform availableMovesContainer; 
    public GameObject moveButtonPrefab; 

    [Header("Unlocked Attacks")]
    public List<AttackData> unlockedAttacks; 

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        skillTreePanel.SetActive(false); 
    }

    private void Update()
    {
        // Hardcoded check for the Right Stick press (R3) on the currently active gamepad
        if (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame)
        {
            SceneManager.LoadScene("AK_LevelDesign");
        }
    }

    private void OnEnable()
    {
        if (backAction != null)
        {
            backAction.action.Enable();
            backAction.action.performed += OnBackPressed;
        }
    }

    private void OnDisable()
    {
        if (backAction != null)
        {
            backAction.action.performed -= OnBackPressed;
        }
    }

    private void OnBackPressed(InputAction.CallbackContext context)
    {
        if (skillTreePanel.activeSelf) CloseMenu();
    }

    public void OpenMenu()
    {
        skillTreePanel.SetActive(true);

        // Switch Action Map to UI to pause gameplay controls
        if (inputActions != null)
        {
            inputActions.FindActionMap("Gameplay").Disable();
            inputActions.FindActionMap("UI").Enable();
        }

        PopulateAvailableAttacks();
        LoadComboFromPlayer();

        // Wait 1 frame so Unity updates UI elements before setting EventSystem focus
        StartCoroutine(SetInitialUIFocus());
    }

    private IEnumerator SetInitialUIFocus()
    {
        yield return null; // Wait for layout rebuild

        EventSystem.current.SetSelectedGameObject(null); // Clear previous selection

        if (availableMovesContainer.childCount > 0)
        {
            EventSystem.current.SetSelectedGameObject(availableMovesContainer.GetChild(0).gameObject);
        }
        else if (comboSlots.Length > 0)
        {
            EventSystem.current.SetSelectedGameObject(comboSlots[0].gameObject);
        }
    }

    public void CloseMenu()
    {
        SaveComboToPlayer();
        skillTreePanel.SetActive(false);

        if (inputActions != null)
        {
            inputActions.FindActionMap("UI").Disable();
            inputActions.FindActionMap("Gameplay").Enable();
        }
    }

    public void PopulateAvailableAttacks()
    {
        // Destroy existing buttons immediately so we can read child count correctly
        foreach (Transform child in availableMovesContainer) 
        {
            Destroy(child.gameObject);
        }

        List<Selectable> createdButtons = new List<Selectable>();

        foreach (AttackData attack in unlockedAttacks)
        {
            GameObject btnObj = Instantiate(moveButtonPrefab, availableMovesContainer);
            UIAttackButton attackBtn = btnObj.GetComponent<UIAttackButton>();
            attackBtn.Initialize(attack);

            Selectable sel = btnObj.GetComponent<Selectable>();
            if (sel != null) createdButtons.Add(sel);
        }

        // Connect automatic navigation between generated buttons
        SetupExplicitNavigation(createdButtons);
    }

    private void SetupExplicitNavigation(List<Selectable> moveButtons)
    {
        // Wire D-pad navigation between instantiated moves and combo slots
        for (int i = 0; i < moveButtons.Count; i++)
        {
            Navigation nav = moveButtons[i].navigation;
            nav.mode = Navigation.Mode.Automatic; // Ensures D-Pad auto-detects adjacent UI
            moveButtons[i].navigation = nav;
        }
    }

    public void LoadComboFromPlayer()
    {
        if (playerCombatStyle.lightComboSequence == null) return;

        for (int i = 0; i < comboSlots.Length; i++)
        {
            if (i < playerCombatStyle.lightComboSequence.Length && playerCombatStyle.lightComboSequence[i] != null)
            {
                comboSlots[i].SetAttack(playerCombatStyle.lightComboSequence[i]);
            }
            else
            {
                comboSlots[i].ClearSlot();
            }
        }
    }

    public void TryAddAttackToCombo(AttackData attack)
    {
        foreach (UIComboSlot slot in comboSlots)
        {
            if (slot.IsEmpty())
            {
                slot.SetAttack(attack);
                SaveComboToPlayer();
                return;
            }
        }
    }

    public void SaveComboToPlayer()
    {
        List<AttackData> newCombo = new List<AttackData>();

        foreach (UIComboSlot slot in comboSlots)
        {
            if (!slot.IsEmpty())
            {
                newCombo.Add(slot.currentAttack);
            }
        }

        playerCombatStyle.lightComboSequence = newCombo.ToArray();
    }
}