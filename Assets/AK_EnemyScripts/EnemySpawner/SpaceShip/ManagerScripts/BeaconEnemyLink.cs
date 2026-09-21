using UnityEngine;

public class BeaconEnemyLink : MonoBehaviour
{
    private bool hasBeenEnabled = false;
    private bool hasRegisteredDeath = false;

    private void OnEnable()
    {
        // Mark that this enemy has successfully spawned/activated from the pool
        hasBeenEnabled = true;
        hasRegisteredDeath = false;
    }

    private void OnDisable()
    {
        // Ignore if unspawning during scene shutdown or if it was never formally enabled (e.g. pool pre-warming)
        if (!hasBeenEnabled || !gameObject.scene.isLoaded) return;

        // When the enemy dies and deactivates (returns to pool), register its death to the beacon exactly once per life cycle
        if (!hasRegisteredDeath)
        {
            hasRegisteredDeath = true;

            BeaconSpawnerManager manager = FindObjectOfType<BeaconSpawnerManager>();
            if (manager != null)
            {
                manager.RegisterOnlyKill(gameObject);
            }
        }
    }
}