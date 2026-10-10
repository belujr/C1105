using UnityEngine;

public class EnemySoulDrop : MonoBehaviour
{
    [Header("Soul Energy Config")]
    [Tooltip("Souls awarded when this enemy dies. Split across the soul wisps.")]
    public int soulEnergyValue = 25;

    private bool hasDroppedSouls = false;

    private void OnEnable()
    {
        // pooled enemies come back to life through OnEnable, so each life can drop souls once
        hasDroppedSouls = false;
    }

    public void TriggerSoulDrop()
    {
        if (hasDroppedSouls) return;
        hasDroppedSouls = true;

        // stop EnemyHitFX from announcing a second kill when this body despawns
        if (TryGetComponent<EnemyHitFX>(out var hitFx)) hitFx.MarkKillHandled();

        HitFeedbackManager.NotifyKill(transform.position, transform.forward, soulEnergyValue);
    }

    public void ResetDrop() => hasDroppedSouls = false;
}