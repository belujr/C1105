using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class DummyHealthBar : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the DummyHealth component.")]
    [SerializeField] private DummyHealth dummyHealth;
    
    [Tooltip("UI Slider representing the health bar.")]
    [SerializeField] private Slider healthSlider;
    
    [Tooltip("The Image component of the slider's fill area.")]
    [SerializeField] private Image fillImage;

    [Header("Visual Settings")]
    [SerializeField] private Color fullHealthColor = Color.green;
    [SerializeField] private Color emptyHealthColor = Color.red;

    private Camera mainCamera;
    private Coroutine fillRoutine;

    private void Awake()
    {
        if (dummyHealth == null)
        {
            dummyHealth = GetComponentInParent<DummyHealth>();
        }

        mainCamera = Camera.main;
    }

    private void OnEnable()
    {
        if (dummyHealth != null)
        {
            dummyHealth.OnHealthChanged += UpdateHealthBar;
            dummyHealth.OnDeath += HandleDeath;
            dummyHealth.OnRevive += HandleRevive;
        }
    }

    private void OnDisable()
    {
        if (dummyHealth != null)
        {
            dummyHealth.OnHealthChanged -= UpdateHealthBar;
            dummyHealth.OnDeath -= HandleDeath;
            dummyHealth.OnRevive -= HandleRevive;
        }
    }

    private void Start()
    {
        if (dummyHealth != null && healthSlider != null)
        {
            healthSlider.maxValue = dummyHealth.MaxHP;
            healthSlider.value = dummyHealth.CurrentHP;
            UpdateFillColor(dummyHealth.CurrentHP / dummyHealth.MaxHP);
        }
    }

    private void LateUpdate()
    {
        // Billboarding: Always face the camera perfectly at all times
        if (mainCamera != null)
        {
            transform.rotation = mainCamera.transform.rotation;
        }
    }

    private void UpdateHealthBar(float currentHp, float maxHp)
    {
        if (fillRoutine != null) StopCoroutine(fillRoutine);

        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHp;
            healthSlider.value = currentHp;
        }

        float percentage = maxHp > 0f ? (currentHp / maxHp) : 0f;
        UpdateFillColor(percentage);
    }

    private void UpdateFillColor(float percentage)
    {
        if (fillImage != null)
        {
            fillImage.color = Color.Lerp(emptyHealthColor, fullHealthColor, percentage);
        }
    }

    private void HandleDeath()
    {
        if (healthSlider != null)
        {
            healthSlider.value = 0f;
            UpdateFillColor(0f);
        }

        // If isDummy is checked, slowly refill the health bar back to green over the revive cooldown duration!
        if (dummyHealth != null && dummyHealth.isDummy)
        {
            if (fillRoutine != null) StopCoroutine(fillRoutine);
            fillRoutine = StartCoroutine(SlowRefillRoutine(dummyHealth.ReviveCooldown));
        }
    }

    private IEnumerator SlowRefillRoutine(float duration)
    {
        float elapsed = 0f;
        float startVal = 0f;
        float targetVal = dummyHealth != null ? dummyHealth.MaxHP : 100f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            if (healthSlider != null)
            {
                float currentVal = Mathf.Lerp(startVal, targetVal, t);
                healthSlider.value = currentVal;
                UpdateFillColor(currentVal / targetVal);
            }

            yield return null;
        }
    }

    private void HandleRevive()
    {
        if (fillRoutine != null) StopCoroutine(fillRoutine);
        if (dummyHealth != null && healthSlider != null)
        {
            healthSlider.value = dummyHealth.CurrentHP;
            UpdateFillColor(1f);
        }
    }
}