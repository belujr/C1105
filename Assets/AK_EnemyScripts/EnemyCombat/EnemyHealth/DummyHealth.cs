using UnityEngine;
using System;
using System.Collections;
using CombatSystem.Data; 
using CombatSystem.Animation; 

public class DummyHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private float maxHP = 100f;
    private float currentHP;

    [Header("Dummy Settings")]
    [SerializeField] public bool isDummy = true;
    [SerializeField] private float reviveCooldown = 2.0f;

    [Header("Spaceship Beacon Integration")]
    [SerializeField] private bool countAsDeathBodyInSpaceshipBeacon = true;
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

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce = 1.5f, AudioClip hitSound = null, int attackID = -1, bool isAOE = false)
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
                BeaconSpawnerManager spawnerManager = FindObjectOfType<BeaconSpawnerManager>();
                if (spawnerManager != null)
                {
                    spawnerManager.RegisterOnlyKill(gameObject);
                }
            }
            else
            {
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