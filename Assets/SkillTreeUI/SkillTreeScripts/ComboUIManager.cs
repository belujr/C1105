using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

public class ComboUIManager : MonoBehaviour
{
    public static ComboUIManager Instance;

    [Header("UI Panel & Inputs")]
    public GameObject skillTreePanel; 
    public InputActionReference backAction;
    public InputActionAsset inputActions; 

    [Header("Player Data References")]
    public CombatStyle playerCombatStyle; 

    [Header("UI References")]
    public UIComboSlot[] comboSlots; 
    public Transform treeContainer; 
    public SkillTreeNode startingSelectedNode; 

    private SkillTreeNode[] allTreeNodes;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        if (skillTreePanel != null)
            skillTreePanel.SetActive(false); 

        if (treeContainer != null)
            allTreeNodes = treeContainer.GetComponentsInChildren<SkillTreeNode>(true);
    }

    private void Update()
    {
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

        if (inputActions != null)
        {
            inputActions.FindActionMap("Gameplay").Disable();
            inputActions.FindActionMap("UI").Enable();
        }

        RefreshAllNodes();
        LoadComboFromPlayer();
        AnimateAllLines(); // Trigger line animations

        StartCoroutine(SetInitialUIFocus());
    }

    private IEnumerator SetInitialUIFocus()
    {
        yield return null; 

        EventSystem.current.SetSelectedGameObject(null);

        if (startingSelectedNode != null)
        {
            EventSystem.current.SetSelectedGameObject(startingSelectedNode.gameObject);
        }
        else if (allTreeNodes != null && allTreeNodes.Length > 0)
        {
            EventSystem.current.SetSelectedGameObject(allTreeNodes[0].gameObject);
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

    public void RefreshAllNodes()
    {
        if (allTreeNodes == null && treeContainer != null)
            allTreeNodes = treeContainer.GetComponentsInChildren<SkillTreeNode>(true);

        if (allTreeNodes == null) return;

        foreach (SkillTreeNode node in allTreeNodes)
        {
            node.UpdateVisuals();
        }
    }

    public void AnimateAllLines()
    {
        if (allTreeNodes == null) return;

        foreach (SkillTreeNode node in allTreeNodes)
        {
            node.AnimateLineToParent();
        }
    }

    public void LoadComboFromPlayer()
    {
        if (playerCombatStyle == null || playerCombatStyle.lightComboSequence == null) return;

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

    public void TryEquipToSpecificSlot(AttackData attack, int slotIndex)
    {
        if (slotIndex >= 0 && slotIndex < comboSlots.Length)
        {
            comboSlots[slotIndex].SetAttack(attack);
            SaveComboToPlayer();
        }
    }

    public void SaveComboToPlayer()
    {
        if (playerCombatStyle == null) return;

        AttackData[] newCombo = new AttackData[comboSlots.Length];

        for (int i = 0; i < comboSlots.Length; i++)
        {
            if (!comboSlots[i].IsEmpty())
            {
                newCombo[i] = comboSlots[i].currentAttack;
            }
            else
            {
                newCombo[i] = null; 
            }
        }

        playerCombatStyle.lightComboSequence = newCombo;
    }
}