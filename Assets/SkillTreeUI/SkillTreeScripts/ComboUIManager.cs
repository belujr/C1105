using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;

// Runs before the skill nodes' Awake so the editor reset below lands before they read PlayerPrefs.
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
    [Tooltip("UNITY EDITOR ONLY. Wipes every saved skill-tree unlock once each time you press Play, so you can " +
             "re-test buying skills with the souls you have. Builds ignore this: players keep their unlocks.")]
    public bool resetSkillUnlocksOnPlay = true;

    private static bool hasResetUnlocksThisSession;

    // Clears the flag on every new Play, even with Domain Reload disabled
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearUnlockResetFlag() => hasResetUnlocksThisSession = false;

    // Set by Shopkeeper so it can fade the panel out BEFORE the menu closes.
    // Defaults to false, so the menu behaves exactly as before everywhere else.
    [HideInInspector] public bool suppressBackClose;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

#if UNITY_EDITOR
        // Once per Play session (not every time the hub reloads after a death), and only for the real instance.
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

    [ContextMenu("Reset Skill Unlocks (Debug)")]
    public void ResetAllSkillUnlocks()
    {
        foreach (SkillTreeNode node in FindAllNodes())
            node.ResetUnlockSaveData();

        PlayerPrefs.Save();
        Debug.Log("[ComboUIManager] Skill tree unlocks reset.");
    }

    // Right-click the component > "Log Skill Tree Wiring" to print every node's parent and attack.
    // Use it to spot a child whose Parent Node points at the wrong node, or two nodes sharing one AttackData.
    [ContextMenu("Log Skill Tree Wiring")]
    private void LogSkillTreeWiring()
    {
        var seen = new Dictionary<AttackData, SkillTreeNode>();

        foreach (SkillTreeNode n in FindAllNodes())
        {
            string parent = n.parentNode != null ? n.parentNode.name : "NONE (root, unlocked for free)";
            string attack = n.attackData != null ? n.attackData.name : "NONE";
            Debug.Log($"[SkillTree] {n.name}  <-  parent: {parent}  |  attack: {attack}", n);

            if (n.parentNode == n)
                Debug.LogWarning($"[SkillTree] {n.name} is its own parent!", n);

            if (n.attackData == null) continue;

            if (seen.TryGetValue(n.attackData, out SkillTreeNode other))
                Debug.LogWarning($"[SkillTree] {n.name} and {other.name} share the same AttackData '{n.attackData.name}'. " +
                                 "They share one save key, so unlocking one unlocks the other on the next launch.", n);
            else
                seen.Add(n.attackData, n);
        }
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