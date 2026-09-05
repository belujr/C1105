using UnityEngine;
using System;
using System.Collections;

public class DummyHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Maximum Health Points of the dummy.")]
    [SerializeField] private float maxHP = 100f;
    private float currentHP;

    [Header("Dummy Settings")]
    [Tooltip("If checked, HP automatically refills to full after dying and the enemy revives (Dummies stay in scene, non-dummies despawn/pool).")]
    [SerializeField] public bool isDummy = true;

    [Tooltip("Cooldown time in seconds before resetting HP.")]
    [SerializeField] private float reviveCooldown = 2.0f;

    [Header("Spaceship Beacon Integration")]
    [Tooltip("If checked, this enemy's death counts toward the Spaceship Beacon kill quota.")]
    [SerializeField] private bool countAsDeathBodyInSpaceshipBeacon = true;

    [Tooltip("Time in seconds to wait after dying before returning the enemy to the object pool (Only applies if isDummy is false).")]
    [SerializeField] private float poolReturnDelay = 3.0f;

    public float MaxHP => maxHP;
    public float MaxHp => maxHP;
    public float CurrentHP => currentHP;
    public float CurrentHp => currentHP;

    public bool IsDead { get; private set; } = false;
    public float ReviveCooldown => reviveCooldown;

    public event Action<float, float> OnHealthChanged; 
    public event Action OnDeath;
    public event Action OnRevive;

    private void Awake()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHP = Mathf.Clamp(currentHP - amount, 0f, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);

        StartCoroutine(HitStopRoutine(0.05f));
        
        if (currentHP <= 0f)
        {
            Die();
        }
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        float originalTimeScale = Time.timeScale;
        Time.timeScale = 0.0f; 
        
        yield return new WaitForSecondsRealtime(duration);
        
        Time.timeScale = originalTimeScale;
    }

    public void Revive()
    {
        currentHP = maxHP;
        IsDead = false;
      
        OnHealthChanged?.Invoke(currentHP, maxHP);
        OnRevive?.Invoke();
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        if (countAsDeathBodyInSpaceshipBeacon)
        {
            if (isDummy)
            {
                // Scene dummies route through the spawner manager registry
                BeaconSpawnerManager spawnerManager = FindObjectOfType<BeaconSpawnerManager>();
                if (spawnerManager != null)
                {
                    spawnerManager.RegisterOnlyKill(gameObject);
                }
            }
            else
            {
                // Pooled minions bypass the manager registry and report directly to BeaconHealth
                BeaconHealth beacon = FindObjectOfType<BeaconHealth>();
                if (beacon != null)
                {
                    beacon.RegisterEnemyKilled();
                }
            }
        }

        OnDeath?.Invoke();

        if (!isDummy && GetComponent<BaseEnemyBrain>() == null)
        {
            StartCoroutine(ReturnToPoolAfterDelayRoutine(poolReturnDelay));
        }
    }

    private IEnumerator ReturnToPoolAfterDelayRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        }
    }
}