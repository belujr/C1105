using UnityEngine;
using System.Collections;

public class EnemySoulDrop : MonoBehaviour
{
    [Header("Soul Energy Config")]
    [Tooltip("Exact soul energy rewarded when this specific enemy is defeated.")]
    public int soulEnergyValue = 25;

    [Header("Timing")]
    [Tooltip("Duration in seconds for the soul wisp VFX to travel and trigger the player absorb glow before souls are credited.")]
    public float wispTravelDelay = 0.6f;

    private bool hasDroppedSouls = false;

    private void OnEnable()
    {
        hasDroppedSouls = false;
    }

    public void TriggerSoulDrop()
    {
        if (hasDroppedSouls) return;
        hasDroppedSouls = true;

        // 1. Trigger death VFX and soul wisps via HitFeedbackManager
        if (HitFeedbackManager.Instance != null)
        {
            HitFeedbackManager.NotifyKill(transform.position, transform.forward);
        }

        // 2. Wait for wisp flight & player glow feedback, THEN credit currency
        StartCoroutine(DelayedSoulCreditRoutine());
    }

    private IEnumerator DelayedSoulCreditRoutine()
    {
        yield return new WaitForSeconds(wispTravelDelay);

        // 3. Add to centralized SoulManager economy
        if (SoulManager.Instance != null)
        {
            SoulManager.Instance.AddSouls(soulEnergyValue);
        }
    }
}