using UnityEngine;
using System.Collections.Generic;

namespace CombatSystem.Data
{
    [CreateAssetMenu(fileName = "NewEnemyAnimProfile", menuName = "CombatSystem/Enemy Anim Profile")]
    public class EnemyAnimProfile : ScriptableObject
    {
        [Header("Idle Animation")]
        [Tooltip("The default idle animation clip.")]
        public AnimationClip idleClip;
        [Tooltip("Crossfade duration into the idle state.")]
        public float idleTransitionDuration = 0.15f;

        [Header("Death Animation Settings")]
        [Tooltip("Mixamo death animation clip.")]
        public AnimationClip deathClip;
        public float deathTransitionDuration = 0.1f;
        public float deathPlaybackSpeed = 1.0f;

        [Header("Get-Up / Revival Animation Settings")]
        [Tooltip("Mixamo get-up / stand-up animation clip.")]
        public AnimationClip standUpClip;
        public float standUpTransitionDuration = 0.15f;
        public float standUpPlaybackSpeed = 1.0f;
        [Tooltip("How smoothly and early the get-up animation blends into idle before finishing.")]
        public float standUpToIdleTransitionDuration = 0.25f;

        [Header("Attack Reaction Database")]
        [Tooltip("Add your attack-specific reactions here (e.g., Uppercut, MMA Kick, Heavy Punch).")]
        public List<AttackReactionData> attackReactions = new List<AttackReactionData>();

        [Header("Fallback / Default Directional Reactions")]
        public HitAnimationData defaultHitFront;
        public HitAnimationData defaultHitBack;
        public HitAnimationData defaultHitLeft;
        public HitAnimationData defaultHitRight;
    }
}