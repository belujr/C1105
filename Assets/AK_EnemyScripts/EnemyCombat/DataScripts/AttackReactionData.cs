using UnityEngine;

namespace CombatSystem.Data
{
    [CreateAssetMenu(fileName = "NewAttackReaction", menuName = "CombatSystem/Attack Reaction Data")]
    public class AttackReactionData : ScriptableObject
    {
        [Header("Action Serial ID")]
        [Tooltip("Unique integer serial ID matching this attack's damage.")]
        public int attackID;

        [Header("Combat Feel & Optimization")]
        [Tooltip("If true, the dummy instantly rotates to face the attacker.")]
        public bool orientTowardsAttacker = true;

        [Header("Knockdown / Trip Mechanics")]
        [Tooltip("If true, this non-lethal attack knocks the enemy flat on the ground and forces them to play a get-up animation.")]
        public bool canFallDown = false;

        [Header("Interruptibility")]
        [Tooltip("If false, this knockdown/recovery animation cannot be interrupted by spamming hits until halfway through getting up.")]
        public bool canBeInterrupted = true;

        [Header("Special Physics Flags")]
        [Tooltip("If true, this attack launches the enemy into the air (e.g., Uppercut).")]
        public bool isAirborneLaunch = false;
        [Tooltip("Upward launch force if airborne.")]
        public float launchUpwardForce = 5f;

        [Header("Custom Get-Up / Recovery Animation (Optional)")]
        [Tooltip("Specific get-up animation when downed by this attack.")]
        public HitAnimationData customStandUpAnimation;

        [Header("Directional Animation Clips")]
        public HitAnimationData reactionFront;
        public HitAnimationData reactionBack;
        public HitAnimationData reactionLeft;
        public HitAnimationData reactionRight;
    }
}