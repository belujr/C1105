using System;
using UnityEngine;

/// Observes combat state after Update without modifying the player's scripts.
[DefaultExecutionOrder(10000)]
public sealed class TutorialPlayerObserver : MonoBehaviour
{
    public event Action OnDashPerformed;
    public event Action OnSpecialAttackPerformed;
    public event Action OnComboChainStarted;
    public event Action<int> OnComboHitLanded;

    private PlayerController player;
    private bool wasDashing;
    private bool wasSpecial;
    private bool wasAttacking;
    private int lastComboIndex = -1;

    /// Called by the tracker when its player reference is bound.
    public void BindPlayer(PlayerController instance)
    {
        player = instance;
        ResetObservation();
        // Binding midway through an action must not count that action as newly performed.
        if (player != null)
        {
            PlayerState state = player.CurrentState;
            wasDashing = state != null && state == player.DashState;
            wasSpecial = state != null && state == player.AOEAttackState;
            wasAttacking = state != null && state == player.AttackState;
            lastComboIndex = wasAttacking ? player.CurrentComboIndex : -1;
        }
    }

    private void OnDisable() => ResetObservation();

    private void ResetObservation()
    {
        wasDashing = wasSpecial = wasAttacking = false;
        lastComboIndex = -1;
    }

    private void LateUpdate()
    {
        if (player == null || !player.isActiveAndEnabled || player.IsMovementLocked || player.IsDead)
        {
            ResetObservation();
            return;
        }

        PlayerState state = player.CurrentState;
        bool dashing = state != null && state == player.DashState;
        bool special = state != null && state == player.AOEAttackState;
        bool attacking = state != null && state == player.AttackState;
        int comboIndex = player.CurrentComboIndex;

        bool dashStarted = dashing && !wasDashing;
        bool specialStarted = special && !wasSpecial && player.specialAttackY != null;
        bool chainStarted = attacking && (!wasAttacking || (comboIndex == 0 && lastComboIndex > 0));
        bool hitStarted = attacking && (chainStarted || comboIndex != lastComboIndex);

        // Cache before callbacks, which may disable or rebind this component.
        wasDashing = dashing;
        wasSpecial = special;
        wasAttacking = attacking;
        lastComboIndex = attacking ? comboIndex : -1;

        if (dashStarted) OnDashPerformed?.Invoke();
        if (specialStarted) OnSpecialAttackPerformed?.Invoke();
        if (chainStarted) OnComboChainStarted?.Invoke();

        // Match AttackState's configured-moveset branch; the fallback is not a combo hit.
        if (hitStarted && player != null && player.equippedStyle != null)
        {
            var sequence = player.equippedStyle.lightComboSequence;
            if (sequence != null && comboIndex >= 0 && comboIndex < sequence.Length && sequence[comboIndex] != null)
                OnComboHitLanded?.Invoke(comboIndex + 1);
        }
    }
}
