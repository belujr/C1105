using UnityEngine;

/// <summary>
/// Announces an enemy's death (and its soul value) exactly once per life.
/// Safe for pooled enemies: the flag re-arms every time the object is enabled.
/// </summary>
[DisallowMultipleComponent]
public class EnemySoulDrop : MonoBehaviour
{
    [Header("Soul Energy Config")]
    [Tooltip("Souls awarded when this enemy dies. Split across the soul wisps.")]
    [Min(0)] public int soulEnergyValue = 25;

    private EnemyHitFX hitFx;       // cached once, not looked up on every kill
    private bool hasDroppedSouls;

    private void Awake() => TryGetComponent(out hitFx);

    // Pooled enemies come back to life through OnEnable, so each life can drop souls once.
    private void OnEnable() => hasDroppedSouls = false;

    public void TriggerSoulDrop()
    {
        if (hasDroppedSouls) return;
        hasDroppedSouls = true;

        // Stop EnemyHitFX from announcing a second kill when this body despawns.
        if (hitFx != null) hitFx.MarkKillHandled();

        HitFeedbackManager.NotifyKill(transform.position, transform.forward, soulEnergyValue);
    }

    public void ResetDrop() => hasDroppedSouls = false;
}