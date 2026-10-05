using UnityEngine;

public class BeaconEnemyLink : MonoBehaviour
{
    private bool hasBeenEnabled = false;
    private bool hasRegisteredDeath = false;
    private BeaconSpawnerManager spawnerManager;

    private void Awake()
    {
        // Cache the manager once to avoid expensive lookups when dying
        spawnerManager = FindObjectOfType<BeaconSpawnerManager>();
    }

    private void OnEnable()
    {
        // Mark that this enemy has successfully spawned/activated from the pool
        hasBeenEnabled = true;
        
        // Reset the death flag so it can count again for its next life!
        hasRegisteredDeath = false; 
    }

    private void OnDisable()
    {
        // Ignore if unspawning during scene shutdown or if it was never formally enabled
        if (!hasBeenEnabled || !gameObject.scene.isLoaded) return;

        // THIS is the only circumstance where the death is counted.
        // It triggers exactly once when the enemy is returned to the Object Pool.
        if (!hasRegisteredDeath && spawnerManager != null)
        {
            hasRegisteredDeath = true;
            spawnerManager.RegisterOnlyKill(gameObject);
        }
    }
}