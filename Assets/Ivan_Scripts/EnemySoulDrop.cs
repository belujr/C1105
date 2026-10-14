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
    private static BeaconSpawnerManager spawner;

    // The summary promises pooled enemies re-arm on enable, but nothing did it before.
    // Without this, a pooled enemy only ever paid out souls on its first death.
    private void OnEnable() => hasDroppedSouls = false;

    public void TriggerSoulDrop()
    {
        if (hasDroppedSouls) return;
        hasDroppedSouls = true;

        EnemyHitFX hitFx = GetComponent<EnemyHitFX>();
        if (hitFx == null) hitFx = gameObject.AddComponent<EnemyHitFX>();
        hitFx.MarkKillHandled();

        if (spawner == null) spawner = FindObjectOfType<BeaconSpawnerManager>();
        if (spawner != null) spawner.ReportEnemyDeath(gameObject);

        HitFeedbackManager.NotifyKill(transform.position, transform.forward, soulEnergyValue);
    }

    public void ResetDrop() => hasDroppedSouls = false;
}