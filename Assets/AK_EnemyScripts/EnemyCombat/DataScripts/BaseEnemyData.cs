using UnityEngine;
using System.Collections.Generic;
using CombatSystem.Data;

public abstract class BaseEnemyDataSO : ScriptableObject
{
    [Header("Identity & Prefab")]
    [SerializeField] private string enemyName = "Enemy Archetype";
    [SerializeField] private GameObject prefabToSpawn;

    [Header("Core Combat Stats")]
    [SerializeField] private float maxHealth = 50f;
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float rotationSpeed = 120f;
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float attackStopDistance = 2.0f;

    [Header("Animation Profile Linkage")]
    [SerializeField] private EnemyAnimProfile animationProfile;

    [Header("Attack Arsenal")]
    [SerializeField] private List<BaseAttackDataSO> availableAttacks = new List<BaseAttackDataSO>();

    // Public Getters
    public string EnemyName => enemyName;
    public GameObject PrefabToSpawn => prefabToSpawn;
    public float MaxHealth => maxHealth;
    public float MoveSpeed => moveSpeed;
    public float RotationSpeed => rotationSpeed;
    public float DetectionRange => detectionRange;
    public float AttackStopDistance => attackStopDistance;
    public EnemyAnimProfile AnimationProfile => animationProfile;
    public List<BaseAttackDataSO> AvailableAttacks => availableAttacks;
}