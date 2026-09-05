using UnityEngine;

public abstract class BaseAttackDataSO : ScriptableObject
{
    [Header("Identity & Categorization")]
    [SerializeField] private string attackName = "New Attack";
    [SerializeField] private TokenType requiredTokenType = TokenType.Melee;

    [Header("Timing Profile (Seconds)")]
    [SerializeField] private float startupTime = 0.3f;
    [SerializeField] private float activeTime = 0.2f;
    [SerializeField] private float recoveryTime = 0.5f;

    [Header("Combat Parameters")]
    [SerializeField] private float attackRange = 2.0f;
    [SerializeField] private int damageAmount = 10; // Changed from float to int to match IDamageable contract
    [SerializeField] private float hitStopDuration = 0.08f;
    [SerializeField] private float knockbackForce = 5.0f;

    [Header("Audio Feedback")]
    [SerializeField] private AudioClip hitSound;

    [Header("Animation Profile Hook")]
    [SerializeField] private string animationClipName;

    // Public Getters
    public string AttackName => attackName;
    public TokenType RequiredTokenType => requiredTokenType;
    public float StartupTime => startupTime;
    public float ActiveTime => activeTime;
    public float RecoveryTime => recoveryTime;
    public float AttackRange => attackRange;
    public int DamageAmount => damageAmount;
    public float HitStopDuration => hitStopDuration;
    public float KnockbackForce => knockbackForce;
    public AudioClip HitSound => hitSound;
    public string AnimationClipName => animationClipName;

    /// <summary>
    /// Polymorphic payload execution overridden by specific attack archetypes.
    /// Interacts cleanly with studio-standard IDamageable contracts.
    /// </summary>
    public abstract void ExecuteAttackPayload(Transform attacker, Transform target);
}