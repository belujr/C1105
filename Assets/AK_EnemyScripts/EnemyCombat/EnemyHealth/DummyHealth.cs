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
    [Tooltip("If checked, HP automatically refills to full after dying.")]
    [SerializeField] public bool isDummy = true;

    [Tooltip("Cooldown time in seconds before resetting HP.")]
    [SerializeField] private float reviveCooldown = 2.0f;

    public float MaxHP => maxHP;
    public float MaxHp => maxHP;
    public float CurrentHP => currentHP;
    public float CurrentHp => currentHP;

    public bool IsDead { get; private set; } = false;
    public float ReviveCooldown => reviveCooldown;

    // Events for controllers or UI to listen to
    public event Action<float, float> OnHealthChanged; // passes (currentHP, maxHP)
    public event Action OnDeath;
    public event Action OnRevive;

    private void Awake()
    {
        currentHP = maxHP;
    }

    /// <summary>
    /// Applies damage to the dummy health pool.
    /// </summary>
    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHP = Mathf.Clamp(currentHP - amount, 0f, maxHP);
        OnHealthChanged?.Invoke(currentHP, maxHP);
        Debug.Log($"[DummyHealth] Took {amount} damage. Current HP: {currentHP}/{maxHP}");

        if (currentHP <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        Debug.Log("[DummyHealth] Dummy HP reached 0. Triggering Death.");
        OnDeath?.Invoke();

        if (isDummy)
        {
            StartCoroutine(ReviveRoutine());
        }
    }

    private IEnumerator ReviveRoutine()
    {
        yield return new WaitForSeconds(reviveCooldown);

        currentHP = maxHP;
        IsDead = false;
        Debug.Log("[DummyHealth] Cooldown complete. HP reset to 100.");
        
        OnHealthChanged?.Invoke(currentHP, maxHP);
        OnRevive?.Invoke();
    }
}