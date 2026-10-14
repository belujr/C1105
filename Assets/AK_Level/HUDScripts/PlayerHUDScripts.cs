using System.Collections;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthHUD : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image healthFill;
    [SerializeField] private Image healthTrail;
    [SerializeField] private TMP_Text playerNameText;
    [SerializeField] private TMP_Text hpText;

    [Header("Display")]
    [SerializeField] private string playerDisplayName = "PLAYER";

    [Header("Delayed Damage Bar")]
    [SerializeField, Min(0f)] private float trailDelay = 0.35f;

    // Fraction of the full bar drained per second.
    // 0.6 means a full-width drain would take about 1.67 seconds.
    [SerializeField, Min(0.01f)] private float trailDrainSpeed = 0.6f;

    [Header("Damage Flash")]
    [SerializeField] private Color normalFillColor = Color.white;
    [SerializeField] private Color damageFlashColor =
        new Color(1f, 0.35f, 0.35f, 1f);

    [SerializeField, Min(0f)] private float flashDuration = 0.18f;

    // Cached lookup: we only READ this private field.
    private static readonly FieldInfo HealthField =
        typeof(PlayerHealth).GetField(
            "currentHealth",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private PlayerHealth playerHealth;
    private Coroutine findRoutine;

    private bool initialized;
    private bool warnedAboutMissingHealth;

    private int previousHealth;
    private int previousMaximum;

    private float targetFill;
    private float trailWait;
    private float flashRemaining;

    private void OnEnable()
    {
        PlayerHealth.Died += OnPlayerDied;
        initialized = false;
        findRoutine = StartCoroutine(FindPlayerRoutine());
    }

    private void OnDisable()
    {
        PlayerHealth.Died -= OnPlayerDied;

        if (findRoutine != null)
        {
            StopCoroutine(findRoutine);
            findRoutine = null;
        }
    }

    private void Start()
    {
        if (healthFill == null)
            Debug.LogWarning(
                "[PlayerHealthHUD] Missing Health Fill reference.", this);

        if (healthTrail == null)
            Debug.LogWarning(
                "[PlayerHealthHUD] Missing Health Trail reference.", this);

        if (playerNameText == null)
            Debug.LogWarning(
                "[PlayerHealthHUD] Missing Player Name Text reference.", this);

        if (hpText == null)
            Debug.LogWarning(
                "[PlayerHealthHUD] Missing HP Text reference.", this);

        if (HealthField == null)
            Debug.LogWarning(
                "[PlayerHealthHUD] Private field 'currentHealth' " +
                "was not found in PlayerHealth.", this);
    }

    private IEnumerator FindPlayerRoutine()
    {
        // Let the spawned player's Awake finish first.
        yield return null;

        var retryDelay = new WaitForSecondsRealtime(0.5f);

        while (true)
        {
            if (playerHealth == null)
            {
                PlayerController player =
                    FindFirstObjectByType<PlayerController>();

                if (player != null)
                {
                    playerHealth = player.GetComponent<PlayerHealth>();

                    if (playerHealth != null)
                    {
                        initialized = false;
                    }
                    else if (!warnedAboutMissingHealth)
                    {
                        Debug.LogWarning(
                            "[PlayerHealthHUD] Found PlayerController on '" +
                            player.name +
                            "', but PlayerHealth is missing on the same object.",
                            player);

                        warnedAboutMissingHealth = true;
                    }
                }
            }

            yield return retryDelay;
        }
    }

    private void LateUpdate()
    {
        if (playerNameText != null)
            playerNameText.text = playerDisplayName;

        if (playerHealth == null || HealthField == null)
            return;

        int maximum = Mathf.Max(0, playerHealth.maxHealth);
        int current = (int)HealthField.GetValue(playerHealth);

        current = playerHealth.IsDead
            ? 0
            : Mathf.Clamp(current, 0, maximum);

        ReceiveHealth(current, maximum);
        AnimateFeedback();
    }

    private void ReceiveHealth(int current, int maximum)
    {
        targetFill = maximum > 0 ? (float)current / maximum : 0f;

        // First connection, healing, or changed maximum:
        // snap both bars so respawns never leave a trailing damage effect.
        bool snap =
            !initialized ||
            maximum != previousMaximum ||
            current > previousHealth;

        if (snap)
        {
            if (healthTrail != null)
                healthTrail.fillAmount = targetFill;

            trailWait = 0f;
            flashRemaining = 0f;
        }
        else if (current < previousHealth)
        {
            // Every new hit restarts the delay and flash.
            trailWait = trailDelay;
            flashRemaining = flashDuration;
        }

        // Real health always changes immediately.
        if (healthFill != null)
            healthFill.fillAmount = targetFill;

        if (hpText != null)
            hpText.text = $"HP: {current}/{maximum}";

        previousHealth = current;
        previousMaximum = maximum;
        initialized = true;
    }

    private void AnimateFeedback()
    {
        // UI feedback keeps animating even when gameplay time is paused.
        float dt = Time.unscaledDeltaTime;

        if (healthTrail != null)
        {
            // Use only the portion of this frame after the delay expires.
            float drainTime = Mathf.Max(0f, dt - trailWait);
            trailWait = Mathf.Max(0f, trailWait - dt);

            healthTrail.fillAmount = Mathf.MoveTowards(
                healthTrail.fillAmount,
                targetFill,
                trailDrainSpeed * drainTime);
        }

        if (healthFill != null)
        {
            float flashStrength = flashDuration > 0f
                ? Mathf.Clamp01(flashRemaining / flashDuration)
                : 0f;

            healthFill.color = Color.Lerp(
                normalFillColor,
                damageFlashColor,
                flashStrength);
        }

        flashRemaining = Mathf.Max(0f, flashRemaining - dt);
    }

    private void OnPlayerDied(PlayerHealth deadPlayer)
    {
        if (deadPlayer != playerHealth)
            return;

        // Show zero immediately, then let the trail drain naturally.
        ReceiveHealth(0, Mathf.Max(0, deadPlayer.maxHealth));
    }
}