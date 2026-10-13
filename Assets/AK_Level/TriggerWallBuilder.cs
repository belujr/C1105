using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TriggerWallBuilder : MonoBehaviour
{
    [Header("Barriers")]
    [Tooltip("Add all the Beacon wall objects you want to build/destroy here.")]
    [SerializeField] private BeaconWallBarrier[] targetBarriers;

    [Header("Phase 1: Pre-Placed Enemies")]
    [Tooltip("Drag pre-placed enemies here. Waves start only after these are defeated.")]
    [SerializeField] private GameObject[] prePlacedEnemies;

    [Header("Phase 2: Wave Spawners")]
    [Tooltip("Drag the Wave Spawners here. Walls drop when all spawners finish and disable themselves.")]
    [SerializeField] private WaveSpawner[] waveSpawners;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (targetBarriers != null)
            {
                foreach (BeaconWallBarrier barrier in targetBarriers)
                {
                    if (barrier != null) barrier.BuildWall();
                }
            }
            
            GetComponent<Collider>().enabled = false;
            StartCoroutine(ArenaProgressionRoutine());
        }
    }

    private IEnumerator ArenaProgressionRoutine()
    {
        // PHASE 1: Monitor pre-placed enemies
        while (HasActiveObjects(prePlacedEnemies))
        {
            yield return new WaitForSeconds(0.5f);
        }

        // PHASE 2: Start all Wave Spawners
        if (waveSpawners != null)
        {
            foreach (WaveSpawner spawner in waveSpawners)
            {
                if (spawner != null) 
                {
                    spawner.gameObject.SetActive(true);
                    spawner.StartSpawning();
                }
            }
        }

        // PHASE 3: Monitor spawners (they disable themselves when waves are cleared)
        while (HasActiveSpawners())
        {
            yield return new WaitForSeconds(0.5f);
        }

        // PHASE 4: Open the walls
        if (targetBarriers != null)
        {
            foreach (BeaconWallBarrier barrier in targetBarriers)
            {
                if (barrier != null) barrier.Disappear();
            }
        }
    }

    private bool HasActiveObjects(GameObject[] objects)
    {
        if (objects == null || objects.Length == 0) return false;
        foreach (GameObject obj in objects)
        {
            if (obj != null && obj.activeInHierarchy) return true;
        }
        return false;
    }

    private bool HasActiveSpawners()
    {
        if (waveSpawners == null || waveSpawners.Length == 0) return false;
        foreach (WaveSpawner spawner in waveSpawners)
        {
            if (spawner != null && spawner.gameObject.activeInHierarchy) return true;
        }
        return false;
    }
}