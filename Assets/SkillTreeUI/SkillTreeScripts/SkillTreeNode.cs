using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class SkillTreeNode : MonoBehaviour, ISelectHandler
{
    [Header("Combo Binding")]
    [Tooltip("Enter 1, 2, 3, or 4 based on which branch this is.")]
    public int targetComboSlot = 1; 

    [Header("Attack Settings")]
    public AttackData attackData;
    public SkillTreeNode parentNode; 

    [Header("Explicit D-Pad Navigation Links")]
    public Selectable navUp;
    public Selectable navDown;
    public Selectable navLeft;
    public Selectable navRight;

    [Header("State")]
    public bool isUnlocked = true;

    private Button button;
    private Image buttonImage;

    private void Awake()
    {
        button = GetComponent<Button>();
        buttonImage = GetComponent<Image>();
        button.onClick.AddListener(OnNodeClicked);
    }

    private void Start()
    {
        SetupExplicitNavigation();
        UpdateVisuals();
    }

    public void SetupExplicitNavigation()
    {
        Navigation nav = new Navigation();
        nav.mode = Navigation.Mode.Explicit;

        nav.selectOnUp = navUp;
        nav.selectOnDown = navDown;
        nav.selectOnLeft = navLeft;
        nav.selectOnRight = navRight;

        button.navigation = nav;
    }

    public bool CanBeUnlocked()
    {
        return parentNode == null || parentNode.isUnlocked;
    }

    public void OnNodeClicked()
    {
        if (!isUnlocked && CanBeUnlocked())
        {
            isUnlocked = true;
            UpdateVisuals();
        }

        if (isUnlocked && attackData != null)
        {
            // Subtract 1 to convert your 1-4 Inspector value to the 0-3 array index
            ComboUIManager.Instance.TryEquipToSpecificSlot(attackData, targetComboSlot - 1);
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
    }

    public void UpdateVisuals()
    {
        if (buttonImage == null) return;

        if (isUnlocked)
        {
            buttonImage.color = Color.white;
            button.interactable = true;
        }
        else if (CanBeUnlocked())
        {
            buttonImage.color = Color.yellow;
            button.interactable = true;
        }
        else
        {
            buttonImage.color = Color.gray;
            button.interactable = false;
        }
    }
}