using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HubStatueUI : MonoBehaviour
{
    public static HubStatueUI Instance;

    [Header("Sidebar Panel")]
    public GameObject statueSidebarPanel;

    [Header("UI Elements")]
    public TMP_Text nameText;
    public Image statueImage;
    public TMP_Text descriptionText;

    private StatueInfo currentStatue;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (statueSidebarPanel != null)
            statueSidebarPanel.SetActive(false);
    }

    public void ToggleStatueMenu(StatueInfo info)
    {
        if (statueSidebarPanel == null || info == null) return;

        // Toggle closed if already viewing this exact statue, otherwise open/switch
        if (statueSidebarPanel.activeSelf && currentStatue == info)
        {
            CloseStatueMenu();
        }
        else
        {
            OpenStatueMenu(info);
        }
    }

    private void OpenStatueMenu(StatueInfo info)
    {
        currentStatue = info;

        if (nameText != null) nameText.text = info.statueName;
        if (descriptionText != null) descriptionText.text = info.statueDescription;

        if (statueImage != null)
        {
            if (info.statueSprite != null)
            {
                statueImage.gameObject.SetActive(true);
                statueImage.sprite = info.statueSprite;
            }
            else
            {
                statueImage.gameObject.SetActive(false);
            }
        }

        statueSidebarPanel.SetActive(true);
    }

    public void CloseStatueMenu()
    {
        currentStatue = null;
        if (statueSidebarPanel != null)
        {
            statueSidebarPanel.SetActive(false);
        }
    }
}