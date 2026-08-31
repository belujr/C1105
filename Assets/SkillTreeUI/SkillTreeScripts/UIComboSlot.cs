using UnityEngine;
using UnityEngine.UI;

public class UIComboSlot : MonoBehaviour
{
    public Image slotImage; // Drag the Image component here in Inspector
    public Sprite emptySlotSprite; // Optional: picture to show when slot is empty
    public AttackData currentAttack;
    
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(ClearSlot);
    }

    public void SetAttack(AttackData attack)
    {
        currentAttack = attack;
        slotImage.sprite = attack.attackIcon;
        slotImage.enabled = true;
    }

    public void ClearSlot()
    {
        currentAttack = null;
        if (emptySlotSprite != null)
        {
            slotImage.sprite = emptySlotSprite;
        }
        else
        {
            slotImage.enabled = false; // Hides image if empty
        }
        
        ComboUIManager.Instance.SaveComboToPlayer();
    }

    public bool IsEmpty() => currentAttack == null;
}