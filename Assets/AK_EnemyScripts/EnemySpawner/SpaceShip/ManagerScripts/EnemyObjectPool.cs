using System.Collections.Generic;
using UnityEngine;
using CombatSystem.Controllers;

public class EnemyObjectPool : MonoBehaviour
{
    public static EnemyObjectPool Instance { get; private set; }

    [System.Serializable]
    public class PoolConfig
    {
        [Tooltip("The enemy prefab to pool.")]
        public GameObject enemyPrefab;

        [Tooltip("How many instances to pre-instantiate when the game starts.")]
        public int initialPoolSize = 10;
    }

    [Header("Inspector Pool Configuration")]
    [Tooltip("Add enemy types here to automatically pre-warm their object pools on startup.")]
    [SerializeField] private List<PoolConfig> poolsToPreWarm = new List<PoolConfig>();

    private Dictionary<int, Queue<GameObject>> poolDictionary = new Dictionary<int, Queue<GameObject>>();
    private Dictionary<int, GameObject> instanceToPrefabMap = new Dictionary<int, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeAllPools();
    }

    private void InitializeAllPools()
    {
        foreach (var config in poolsToPreWarm)
        {
            if (config.enemyPrefab == null) continue;

            int prefabKey = config.enemyPrefab.GetInstanceID();

            if (!poolDictionary.ContainsKey(prefabKey))
            {
                poolDictionary[prefabKey] = new Queue<GameObject>();
            }

            for (int i = 0; i < config.initialPoolSize; i++)
            {
                GameObject instance = Instantiate(config.enemyPrefab, transform);
                instance.SetActive(false);
                poolDictionary[prefabKey].Enqueue(instance);
                
                instanceToPrefabMap[instance.GetInstanceID()] = config.enemyPrefab;
            }
        }
    }

    public GameObject GetPooledEnemy(GameObject enemyPrefab, Vector3 position, Quaternion rotation)
    {
        if (enemyPrefab == null) return null;

        int prefabKey = enemyPrefab.GetInstanceID();

        if (!poolDictionary.ContainsKey(prefabKey))
        {
            poolDictionary[prefabKey] = new Queue<GameObject>();
        }

        GameObject enemyInstance;

        if (poolDictionary[prefabKey].Count > 0)
        {
            enemyInstance = poolDictionary[prefabKey].Dequeue();
        }
        else
        {
            enemyInstance = Instantiate(enemyPrefab, transform);
            instanceToPrefabMap[enemyInstance.GetInstanceID()] = enemyPrefab;
        }

        // 1. Temporarily disable physics to prevent snapping
        CharacterController charController = enemyInstance.GetComponent<CharacterController>();
        if (charController != null) charController.enabled = false;

        // 2. Apply transformations
        enemyInstance.transform.SetPositionAndRotation(position, rotation);
        
        // 3. Activate Object FIRST so OnEnable subscriptions fire properly
        enemyInstance.SetActive(true);

        // 4. Re-enable Behaviours disabled by BaseEnemyBrain.HandleDeath
        BaseEnemyBrain brain = enemyInstance.GetComponent<BaseEnemyBrain>();
        if (brain != null) brain.enabled = true;

        EnemyDummyController dummyController = enemyInstance.GetComponent<EnemyDummyController>();
        if (dummyController != null) dummyController.enabled = true;

        // 5. Fire logic resets now that components are active and listening
        DummyHealth health = enemyInstance.GetComponent<DummyHealth>();
        if (health != null) health.Revive();

        if (brain != null) brain.ResetBrain();

        // 6. Restore physics
        if (charController != null) charController.enabled = true;

        return enemyInstance;
    }

    public void ReturnToPool(GameObject enemyInstance)
    {
        if (enemyInstance == null) return;

        int instanceKey = enemyInstance.GetInstanceID();

        if (!instanceToPrefabMap.ContainsKey(instanceKey))
        {
            Destroy(enemyInstance);
            return;
        }

        GameObject originalPrefab = instanceToPrefabMap[instanceKey];
        int prefabKey = originalPrefab.GetInstanceID();

        CharacterController charController = enemyInstance.GetComponent<CharacterController>();
        if (charController != null) charController.enabled = false;

        enemyInstance.SetActive(false);
        enemyInstance.transform.SetParent(transform);

        if (!poolDictionary.ContainsKey(prefabKey))
        {
            poolDictionary[prefabKey] = new Queue<GameObject>();
        }

        // Prevent double-pooling corruption if already enqueued
        if (!poolDictionary[prefabKey].Contains(enemyInstance))
        {
            poolDictionary[prefabKey].Enqueue(enemyInstance);
        }
    }
}