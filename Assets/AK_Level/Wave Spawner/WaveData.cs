using UnityEngine;

[CreateAssetMenu(fileName = "NewWaveData", menuName = "Combat/Wave Data")]
public class WaveData : ScriptableObject
{
    [System.Serializable]
    public class EnemySpawnGroup
    {
        public GameObject enemyPrefab;
        [Tooltip("How many of this specific enemy to spawn.")]
        public int count = 1;
    }

    [System.Serializable]
    public class Wave
    {
        [Tooltip("The groups of enemies to spawn in this wave.")]
        public EnemySpawnGroup[] enemyGroups;
        
        public float spawnInterval = 1f;
        public float dropHeight = 15f;
        public float spawnRadius = 5f;
        public float delayBeforeNextWave = 3f;
    }

    public Wave[] waves;
}