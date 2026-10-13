using UnityEngine;

public class BeaconEnemyLink : MonoBehaviour
{
    private static BeaconSpawnerManager spawner;
    private bool hasRegisteredDeath;

    private void OnEnable() => hasRegisteredDeath = false;   // pooled enemies count again next life

    /// Called by EnemySoulDrop at the moment of a real kill.
    public void ReportKill()
    {
        if (hasRegisteredDeath) return;
        hasRegisteredDeath = true;

        Debug.Log($"[BeaconEnemyLink] ReportKill on {name}. spawnerFound={spawner != null}", this);

        if (spawner == null) spawner = FindObjectOfType<BeaconSpawnerManager>();  // looked up when needed, never stale
        if (spawner != null) spawner.RegisterOnlyKill(gameObject);
    }
}