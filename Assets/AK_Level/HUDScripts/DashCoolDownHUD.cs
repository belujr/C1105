using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DashCooldownHUD : MonoBehaviour
{
    [Header("Colored Image")]
    [SerializeField] private Image dashFill;

    private PlayerController player;
    private Coroutine findRoutine;

    private void OnEnable()
    {
        findRoutine = StartCoroutine(FindPlayerRoutine());
    }

    private void Start()
    {
        if (dashFill == null)
        {
            Debug.LogWarning(
                "[DashCooldownHUD] Missing Dash Fill reference. " +
                "Assign the colored DashFill Image.", this);
        }
    }

    private void OnDisable()
    {
        if (findRoutine != null)
        {
            StopCoroutine(findRoutine);
            findRoutine = null;
        }
    }

    private IEnumerator FindPlayerRoutine()
    {
        // Let the player's Awake create its states first.
        yield return null;

        var retryDelay = new WaitForSecondsRealtime(0.5f);

        while (true)
        {
            // Only search when the player is missing.
            // Automatically reconnect after the player is replaced.
            if (player == null)
                player = FindFirstObjectByType<PlayerController>();

            yield return retryDelay;
        }
    }

    private void LateUpdate()
    {
        if (dashFill == null)
            return;

        if (player == null)
        {
            dashFill.fillAmount = 0f;
            return;
        }

        // Same values the player's dash is actually gated by, so the bar can never disagree with it.
        float maximum = player.DashCooldownDuration;
        float remaining = player.DashCooldownRemaining;

        // Ready = fully colored.
        // Dash begins = black-and-white background exposed.
        // Cooldown counts down = colored image fills back in.
        dashFill.fillAmount = maximum <= 0f
            ? 1f
            : 1f - Mathf.Clamp01(remaining / maximum);
    }
}