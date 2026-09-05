using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BeaconBossHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the BeaconHealth component in the scene.")]
    [SerializeField] private BeaconHealth beaconHealth;

    [Tooltip("The parent UI panel container for the boss health bar (stays hidden until shield drops).")]
    [SerializeField] private GameObject bossHealthBarContainer;

    [Tooltip("The green UI Image set to 'Filled' type representing current core health.")]
    [SerializeField] private Image healthFillImage;

    [Tooltip("Optional text field to display the boss title (e.g. 'BEACON CORE').")]
    [SerializeField] private TextMeshProUGUI bossTitleText;

    [Header("Visual Styling")]
    [Tooltip("Color of the health fill (Default: Green).")]
    [SerializeField] private Color barFillColor = Color.green;

    [Header("Background Styling")]
    [Tooltip("Background Image of the health bar (Default: Red).")]
    [SerializeField] private Image backgroundBarImage;
    [SerializeField] private Color barBackgroundColor = Color.red;

    private void OnEnable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnShieldDropped.AddListener(ShowBossHealthBar);
            beaconHealth.OnCoreDamaged.AddListener(UpdateBossHealthBar);
            beaconHealth.OnBeaconDestroyed.AddListener(HideBossHealthBar);
        }
        else
        {
            Debug.LogWarning("[BeaconBossHealthBarUI] Beacon Health reference is missing in the Inspector!", this);
        }
    }

    private void OnDisable()
    {
        if (beaconHealth != null)
        {
            beaconHealth.OnShieldDropped.RemoveListener(ShowBossHealthBar);
            beaconHealth.OnCoreDamaged.RemoveListener(UpdateBossHealthBar);
            beaconHealth.OnBeaconDestroyed.RemoveListener(HideBossHealthBar);
        }
    }

    private void Start()
    {
        if (bossHealthBarContainer == null)
        {
            Debug.LogError("[BeaconBossHealthBarUI] Boss Health Bar Container is not assigned in the Inspector!", this);
            return;
        }

        // Hide the boss health bar initially while the shield is up
        bossHealthBarContainer.SetActive(false);

        // Apply visual styling
        if (healthFillImage != null)
        {
            healthFillImage.color = barFillColor;
            healthFillImage.fillAmount = 1f;
        }

        if (backgroundBarImage != null)
        {
            backgroundBarImage.color = barBackgroundColor;
        }

        if (bossTitleText != null)
        {
            bossTitleText.text = "BEACON CORE";
        }
    }

    private void ShowBossHealthBar()
    {
        if (bossHealthBarContainer != null)
        {
            bossHealthBarContainer.SetActive(true);
            Debug.Log("[BeaconBossHealthBarUI] SUCCESS: Shield dropped! Boss Health Bar enabled.");
        }
    }

    private void UpdateBossHealthBar(float currentHealth, float maxHealth)
    {
        if (healthFillImage == null || maxHealth <= 0f) return;

        float healthPercentage = Mathf.Clamp01(currentHealth / maxHealth);
        healthFillImage.fillAmount = healthPercentage;
    }

    private void HideBossHealthBar()
    {
        if (bossHealthBarContainer != null)
        {
            bossHealthBarContainer.SetActive(false);
        }
    }
}