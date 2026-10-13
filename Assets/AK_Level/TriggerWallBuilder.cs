using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TriggerWallBuilder : MonoBehaviour
{
    [Header("Barriers")]
    [Tooltip("Add all the Beacon wall objects you want to build/destroy here.")]
    [SerializeField] private BeaconWallBarrier[] targetBarriers;

    [Header("Enemies")]
    [Tooltip("Drag the enemies for this area here. Walls drop when all are destroyed or disabled.")]
    [SerializeField] private GameObject[] arenaEnemies;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // 1. Build the walls
            if (targetBarriers != null)
            {
                foreach (BeaconWallBarrier barrier in targetBarriers)
                {
                    if (barrier != null) barrier.BuildWall();
                }
            }
            
            // 2. Disable trigger so it doesn't fire again
            GetComponent<Collider>().enabled = false;

            // 3. Start monitoring the enemies in the background
            StartCoroutine(MonitorEnemiesRoutine());
        }
    }

    // A Coroutine is highly optimized here because it avoids checking every single frame in Update()
    private IEnumerator MonitorEnemiesRoutine()
    {
        // Check every 0.5 seconds if enemies are still alive
        while (HasLivingEnemies())
        {
            yield return new WaitForSeconds(0.5f); 
        }

        // The loop breaks when zero enemies are left. Now, destroy the walls.
        if (targetBarriers != null)
        {
            foreach (BeaconWallBarrier barrier in targetBarriers)
            {
                if (barrier != null) barrier.Disappear();
            }
        }
    }

    private bool HasLivingEnemies()
    {
        if (arenaEnemies == null || arenaEnemies.Length == 0) return false;

        foreach (GameObject enemy in arenaEnemies)
        {
            // If the enemy GameObject hasn't been Destroyed (null) AND is currently active
            if (enemy != null && enemy.activeInHierarchy)
            {
                return true; // At least one enemy is still alive
            }
        }
        
        return false; // All enemies are null or disabled
    }
}