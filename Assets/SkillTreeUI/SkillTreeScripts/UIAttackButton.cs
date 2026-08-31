using UnityEngine;
using UnityEngine.UI;

public class UIAttackButton : MonoBehaviour
{
    public Image buttonIconImage; // Drag the child Image component here
    public AttackData myAttackData;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnAttackClicked);
    }

    public void Initialize(AttackData data)
    {
        myAttackData = data;
        buttonIconImage.sprite = data.attackIcon;
    }

    private void OnAttackClicked()
    {
        ComboUIManager.Instance.TryAddAttackToCombo(myAttackData);
    }
}