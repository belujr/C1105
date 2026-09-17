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
    [SerializeField] private int damageAmount = 10;
    [SerializeField] private float hitStopDuration = 0.08f;
    [SerializeField] private float knockbackForce = 5.0f;
    [SerializeField] private float stunDuration = 0.2f; // Configurable stun time per attack (e.g., 0.2 for punch 1, 0.25 for punch 2, 0.3 for kick)

    [Header("Audio Feedback")]
    [SerializeField] private AudioClip hitSound;

    [Header("Animation Profile Hook")]
    [SerializeField] private string animationClipName;

    // Add this inside BaseAttackDataSO.cs (under the Animation Profile Hook section)
    [Header("Player Reaction Hook")]
    [Tooltip("The exact name of the Animator state the player should play when hit by this attack.")]
    [SerializeField] private string playerReactionAnimName = "Hit_Light";

    public string PlayerReactionAnimName => playerReactionAnimName;

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
    public float StunDuration => stunDuration;
    public AudioClip HitSound => hitSound;
    public string AnimationClipName => animationClipName;

    public abstract void ExecuteAttackPayload(Transform attacker, Transform target);
}