using UnityEngine;

/// <summary>
/// Announces an enemy's death (and its soul value) exactly once per life.
/// Safe for pooled enemies: the flag re-arms every time the object is enabled.
/// </summary>
[DisallowMultipleComponent]
public class EnemySoulDrop : MonoBehaviour
{
    [Header("Soul Energy Config")]
    [Min(0)] public int soulEnergyValue = 25;

    private bool hasDroppedSouls;

    private void OnEnable() => hasDroppedSouls = false;

    public void TriggerSoulDrop()
    {
        if (hasDroppedSouls) return;
        hasDroppedSouls = true;

        // EnemyHitFX is added at runtime on the first hit, so it can't be cached in Awake.
        // Look it up now, and create it if the enemy was never hit, so the flag is always set.
        EnemyHitFX hitFx = GetComponent<EnemyHitFX>();
        if (hitFx == null) hitFx = gameObject.AddComponent<EnemyHitFX>();
        hitFx.MarkKillHandled();

        HitFeedbackManager.NotifyKill(transform.position, transform.forward, soulEnergyValue);
    }

    public void ResetDrop() => hasDroppedSouls = false;
}