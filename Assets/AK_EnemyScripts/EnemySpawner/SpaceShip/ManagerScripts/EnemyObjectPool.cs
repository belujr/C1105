using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

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
    public List<PoolConfig> poolsToPreWarm = new List<PoolConfig>();

    private Dictionary<int, IObjectPool<GameObject>> poolDictionary = new Dictionary<int, IObjectPool<GameObject>>();
    private Dictionary<int, int> instanceToPrefabMap = new Dictionary<int, int>();

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
            poolDictionary[prefabKey] = CreateNativePool(config.enemyPrefab);

            List<GameObject> prewarmedObjects = new List<GameObject>();
            for (int i = 0; i < config.initialPoolSize; i++)
            {
                prewarmedObjects.Add(poolDictionary[prefabKey].Get());
            }
            foreach (var obj in prewarmedObjects)
            {
                poolDictionary[prefabKey].Release(obj);
            }
        }
    }

    private IObjectPool<GameObject> CreateNativePool(GameObject prefab)
    {
        
        return new ObjectPool<GameObject>(
            createFunc: () =>
            {
                GameObject instance = Instantiate(prefab, transform);
                instanceToPrefabMap[instance.GetInstanceID()] = prefab.GetInstanceID();
                return instance;
            },
            actionOnGet: (instance) => { },
            actionOnRelease: (instance) =>
            {
                instance.SetActive(false);
                instance.transform.SetParent(transform);
            },
            actionOnDestroy: (instance) => Destroy(instance),
            collectionCheck: true,
            defaultCapacity: 20,
            maxSize: 200
        );
    }

    public GameObject GetPooledEnemy(GameObject enemyPrefab, Vector3 position, Quaternion rotation)
    {
        if (enemyPrefab == null) return null;

        int prefabKey = enemyPrefab.GetInstanceID();

        if (!poolDictionary.ContainsKey(prefabKey))
        {
            poolDictionary[prefabKey] = CreateNativePool(enemyPrefab);
        }

        GameObject enemyInstance = poolDictionary[prefabKey].Get();

        // 1. Keep physics/controllers strictly disabled before mounting
        var charController = enemyInstance.GetComponent<CharacterController>();
        if (charController != null) charController.enabled = false;

        var collider = enemyInstance.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        var capsuleBrain = enemyInstance.GetComponent<UltraInstinctCapsule>();
if (capsuleBrain != null) capsuleBrain.enabled = false;

var impBrain = enemyInstance.GetComponent<ImpBrain>();
if (impBrain != null) impBrain.enabled = false;

        // 2. Set transform safely
        enemyInstance.transform.SetPositionAndRotation(position, rotation);

        // 3. Activate instance (Ship passenger script will handle setup right after)
        enemyInstance.SetActive(true);

        return enemyInstance;
    }

    public void ReturnToPool(GameObject enemyInstance)
    {
        if (enemyInstance == null) return;

        int instanceKey = enemyInstance.GetInstanceID();

        if (instanceToPrefabMap.TryGetValue(instanceKey, out int prefabKey))
        {
            if (poolDictionary.TryGetValue(prefabKey, out var pool))
            {
                pool.Release(enemyInstance);
                return;
            }
        }

        Destroy(enemyInstance);
    }
}