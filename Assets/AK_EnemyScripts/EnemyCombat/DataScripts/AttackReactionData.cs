using UnityEngine;

namespace CombatSystem.Data
{
    [CreateAssetMenu(fileName = "NewAttackReaction", menuName = "CombatSystem/Attack Reaction Data")]
    public class AttackReactionData : ScriptableObject
    {
        [Header("Attack Identification")]
        [Tooltip("Name or identifier matching this attack type.")]
        public string attackId;

        [Header("Precision Matching")]
        [Tooltip("The knockback force value from the Player's AttackData that triggers this specific reaction.")]
        public float targetKnockbackForce = 1.0f;

        [Header("Combat Feel & Optimization")]
        [Tooltip("If true, the dummy instantly rotates to face the attacker so you only need a Front reaction animation!")]
        public bool orientTowardsAttacker = true;

        [Header("Special Physics Flags")]
        [Tooltip("If true, this attack launches the enemy into the air (e.g., Uppercut).")]
        public bool isAirborneLaunch = false;
        [Tooltip("Upward launch force if airborne.")]
        public float launchUpwardForce = 5f;

        [Header("Custom Get-Up / Recovery Animation (Optional)")]
        [Tooltip("If assigned, the dummy will use this specific get-up animation when downed by this attack instead of the default.")]
        public HitAnimationData customStandUpAnimation;

        [Header("Directional Animation Clips")]
        public HitAnimationData reactionFront;
        public HitAnimationData reactionBack;
        public HitAnimationData reactionLeft;
        public HitAnimationData reactionRight;
    }
}