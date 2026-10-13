using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopSoulDisplay : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The background container panel for the shop soul counter.")]
    public GameObject soulPanel;

    [Tooltip("The soul currency icon image.")]
    public Image soulIconImage;

    [Tooltip("The TextMeshPro text element displaying the total soul count.")]
    public TMP_Text soulText;

    private void OnEnable()
    {
        // Subscribe to the persistent SoulManager currency change event
        SoulManager.OnSoulsChanged += HandleSoulsChanged;

        // Immediate sync upon opening the shop
        if (SoulManager.Instance != null)
        {
            UpdateDisplay(SoulManager.Instance.TotalSouls);
        }
    }

    private void OnDisable()
    {
        SoulManager.OnSoulsChanged -= HandleSoulsChanged;
    }

    private void HandleSoulsChanged(int newTotal)
    {
        UpdateDisplay(newTotal);
    }

    private void UpdateDisplay(int totalSouls)
    {
        if (soulText != null)
        {
            soulText.text = totalSouls.ToString("N0");
        }

        if (soulPanel != null && !soulPanel.activeSelf)
        {
            soulPanel.SetActive(true);
        }
    }
}