using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [SerializeField] private WaveData waveData;
    
    private List<GameObject> activeSpawnedEnemies = new List<GameObject>();
    private bool isSpawning = false;

    public void StartSpawning()
    {
        if (!isSpawning) StartCoroutine(SpawnRoutine()); //[cite: 3]
    }

    private IEnumerator SpawnRoutine()
    {
        isSpawning = true; //[cite: 3]

        if (waveData != null) //[cite: 3]
        {
            foreach (WaveData.Wave wave in waveData.waves) //[cite: 3]
            {
                foreach (WaveData.EnemySpawnGroup group in wave.enemyGroups) //[cite: 3]
                {
                    for (int i = 0; i < group.count; i++) //[cite: 3]
                    {
                        Vector2 randomCircle = Random.insideUnitCircle * wave.spawnRadius; //[cite: 3]
                        Vector3 dropPosition = transform.position + new Vector3(randomCircle.x, wave.dropHeight, randomCircle.y); //[cite: 3]

                        GameObject enemy = EnemyObjectPool.Instance.GetPooledEnemy(group.enemyPrefab, dropPosition, Quaternion.identity); //[cite: 3]
                        
                        if (enemy != null) //[cite: 3]
                        {
                            // 1. Check for the old complex passenger script
                            EnemyShipPassenger passenger = enemy.GetComponent<EnemyShipPassenger>();
                            
                            // 2. Check for your brand new simple drop script
                            SimpleAirDrop simpleDrop = enemy.GetComponent<SimpleAirDrop>();

                            if (passenger != null)
                            {
                                passenger.DetachAndLand();
                            }
                            else if (simpleDrop != null)
                            {
                                simpleDrop.DropAndWakeUp();
                            }
                            else
                            {
                                // Fallback if neither script exists
                                EnableEnemyBrains(enemy);
                            }

                            activeSpawnedEnemies.Add(enemy); //[cite: 3]
                        }

                        yield return new WaitForSeconds(wave.spawnInterval); //[cite: 3]
                    }
                }

                while (HasLivingEnemies()) //[cite: 3]
                {
                    yield return new WaitForSeconds(0.5f); //[cite: 3]
                }

                yield return new WaitForSeconds(wave.delayBeforeNextWave); //[cite: 3]
            }
        }

        gameObject.SetActive(false); //[cite: 3]
    }

    private void EnableEnemyBrains(GameObject enemy)
    {
        var capsuleBrain = enemy.GetComponent<UltraInstinctCapsule>();
        if (capsuleBrain != null) capsuleBrain.enabled = true;

        var impBrain = enemy.GetComponent<ImpBrain>();
        if (impBrain != null) impBrain.enabled = true;

        var healerBrain = enemy.GetComponent<HealerController>();
        if (healerBrain != null) healerBrain.enabled = true;
    }

    private bool HasLivingEnemies()
    {
        activeSpawnedEnemies.RemoveAll(e => e == null || !e.activeInHierarchy); //[cite: 3]
        return activeSpawnedEnemies.Count > 0; //[cite: 3]
    }
}