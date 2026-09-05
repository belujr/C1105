using UnityEngine;
using System.Collections.Generic;

namespace CombatSystem.Data
{
    [CreateAssetMenu(fileName = "NewEnemyAnimProfile", menuName = "CombatSystem/Enemy Anim Profile")]
    public class EnemyAnimProfile : ScriptableObject
    {
        [System.Serializable]
        public struct NamedAttackClip
        {
            [Tooltip("Match this Key exactly to AnimationClipName in your Attack Data SO (e.g., 'MinionSlash')")]
            public string attackNameKey;
            public AnimationClip attackClip;
        }

        [Header("Locomotion Animations")]
        public AnimationClip idleClip;
        public float idleTransitionDuration = 0.15f;
        public AnimationClip walkClip;
        public float walkTransitionDuration = 0.15f;
        public AnimationClip runClip;
        public float runTransitionDuration = 0.15f;
        public AnimationClip strafeLeftClip;
        public AnimationClip strafeRightClip;

        [Header("Landing Animations")]
        public AnimationClip landingClip;
        public float landingTransitionDuration = 0.1f;
        public float landingPlaybackSpeed = 1.0f;

        [Header("Death & Revival Animations")]
        public AnimationClip deathClip;
        public float deathTransitionDuration = 0.1f;
        public float deathPlaybackSpeed = 1.0f;
        public AnimationClip standUpClip;
        public float standUpTransitionDuration = 0.15f;
        public float standUpPlaybackSpeed = 1.0f;
        public float standUpToIdleTransitionDuration = 0.25f;

        [Header("Explicit Attack Animations (By Name)")]
        public List<NamedAttackClip> namedAttacks = new List<NamedAttackClip>();

        [Header("Attack Reaction Database (Studio ID Matching)")]
        public List<AttackReactionData> attackReactions = new List<AttackReactionData>();

        [Header("Fallback / Default Directional Reactions")]
        public HitAnimationData defaultHitFront;
        public HitAnimationData defaultHitBack;
        public HitAnimationData defaultHitLeft;
        public HitAnimationData defaultHitRight;

        private Dictionary<int, AttackReactionData> reactionDictionary;

        public void InitializeDictionary()
        {
            reactionDictionary = new Dictionary<int, AttackReactionData>();
            if (attackReactions == null) return;

            foreach (var reaction in attackReactions)
            {
                if (reaction != null && !reactionDictionary.ContainsKey(reaction.attackID))
                {
                    reactionDictionary.Add(reaction.attackID, reaction);
                }
            }
        }

        public AttackReactionData GetReaction(int attackID)
        {
            if (reactionDictionary == null) InitializeDictionary();
            return reactionDictionary.TryGetValue(attackID, out var reaction) ? reaction : null;
        }

        public AnimationClip GetAnimationClip(string animName)
        {
            if (string.IsNullOrEmpty(animName)) return null;

            string lower = animName.ToLower();

            // 1. Check explicit Named Attacks list first
            if (namedAttacks != null)
            {
                foreach (var named in namedAttacks)
                {
                    if (!string.IsNullOrEmpty(named.attackNameKey) && named.attackClip != null)
                    {
                        if (named.attackNameKey.Equals(animName, System.StringComparison.OrdinalIgnoreCase) || 
                            lower.Contains(named.attackNameKey.ToLower()))
                        {
                            return named.attackClip;
                        }
                    }
                }
            }

            // 2. Check locomotion and state clips
            if (lower.Contains("idle")) return idleClip;
            if (lower.Contains("walk") || lower.Contains("chase")) return walkClip != null ? walkClip : idleClip;
            if (lower.Contains("run")) return runClip != null ? runClip : walkClip;
            if (lower.Contains("strafeleft") || lower.Contains("left")) return strafeLeftClip != null ? strafeLeftClip : (walkClip != null ? walkClip : idleClip);
            if (lower.Contains("straferight") || lower.Contains("right")) return strafeRightClip != null ? strafeRightClip : (walkClip != null ? walkClip : idleClip);
            if (lower.Contains("landing") || lower.Contains("land")) return landingClip;
            if (lower.Contains("death")) return deathClip;
            if (lower.Contains("standup")) return standUpClip;

            return null;
        }
    }
}