using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

[DefaultExecutionOrder(-50)]
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

    [Header("Debug & Testing")]
    [Tooltip("UNITY EDITOR ONLY. Wipes every saved skill-tree unlock once each time you press Play.")]
    public bool resetSkillUnlocksOnPlay = true;

    private static bool hasResetUnlocksThisSession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearUnlockResetFlag() => hasResetUnlocksThisSession = false;

    [HideInInspector] public bool suppressBackClose;

    private const string APP_STARTED_KEY = "Game_HasStartedThisSession";

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Check if this is a fresh launch of the executable game
        if (Instance == this && !PlayerPrefs.HasKey(APP_STARTED_KEY))
        {
            PlayerPrefs.SetInt(APP_STARTED_KEY, 1);
            ResetToStarterBuildUnlocks();
        }

#if UNITY_EDITOR
        if (Instance == this && resetSkillUnlocksOnPlay && !hasResetUnlocksThisSession)
        {
            hasResetUnlocksThisSession = true;
            ResetAllSkillUnlocks();
        }
#endif
    }

    private SkillTreeNode[] FindAllNodes()
    {
        if (treeContainer != null)
            return treeContainer.GetComponentsInChildren<SkillTreeNode>(true);

        return FindObjectsByType<SkillTreeNode>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    }

    // Resets everything on a fresh boot, but leaves exactly 2 nodes unlocked as starters
    public void ResetToStarterBuildUnlocks()
    {
        SkillTreeNode[] nodes = FindAllNodes();
        int unlockedCount = 0;

        foreach (SkillTreeNode node in nodes)
        {
            node.ResetUnlockSaveData();

            // Forcefully unlock only the first 2 nodes found so the player can attack twice
            if (unlockedCount < 2 && node.attackData != null)
            {
                PlayerPrefs.SetInt("NodeUnlocked_" + node.attackData.name, 1);
                node.isUnlocked = true;
                unlockedCount++;
            }
        }

        PlayerPrefs.Save();
        Debug.Log("[ComboUIManager] Fresh boot detected: Skill tree reset to 2 starter unlocks.");
    }

    [ContextMenu("Reset Skill Unlocks (Debug)")]
    public void ResetAllSkillUnlocks()
    {
        foreach (SkillTreeNode node in FindAllNodes())
            node.ResetUnlockSaveData();

        PlayerPrefs.Save();
        Debug.Log("[ComboUIManager] Skill tree unlocks reset.");
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
        if (suppressBackClose) return;
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
        AnimateAllLines();

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
        if (playerCombatStyle == null) return;

        for (int i = 0; i < comboSlots.Length; i++)
        {
            if (playerCombatStyle.lightComboSequence != null &&
                i < playerCombatStyle.lightComboSequence.Length &&
                playerCombatStyle.lightComboSequence[i] != null)
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

        // Find the highest index that actually contains an attack
        int highestAssignedIndex = -1;
        for (int i = 0; i < comboSlots.Length; i++)
        {
            if (!comboSlots[i].IsEmpty() && comboSlots[i].currentAttack != null)
            {
                highestAssignedIndex = i;
            }
        }

        // Create an array sized exactly to the highest assigned slot (minimum length 0 if empty)
        int finalComboLength = highestAssignedIndex + 1;
        AttackData[] newCombo = new AttackData[finalComboLength];

        for (int i = 0; i < finalComboLength; i++)
        {
            if (!comboSlots[i].IsEmpty())
            {
                newCombo[i] = comboSlots[i].currentAttack;
            }
            else
            {
                newCombo[i] = null; // Fills intermediate gaps safely if any exist
            }
        }

        playerCombatStyle.lightComboSequence = newCombo;
        playerCombatStyle.TrimEmptyTrailingSlots();
    }
}