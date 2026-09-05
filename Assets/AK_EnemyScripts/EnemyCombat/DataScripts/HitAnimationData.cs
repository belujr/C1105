using UnityEngine;

namespace CombatSystem.Data
{
    [CreateAssetMenu(fileName = "NewHitAnimationData", menuName = "CombatSystem/Hit Animation Data")]
    public class HitAnimationData : ScriptableObject
    {
        [Header("Animation Settings")]
        [Tooltip("The Mixamo animation clip for this reaction.")]
        public AnimationClip clip;

        [Tooltip("How fast the Playables graph crossfades into this animation (in seconds). Set low (0.05) for snappy response.")]
        public float transitionDuration = 0.05f;

        [Tooltip("Playback speed multiplier for this clip.")]
        public float playbackSpeed = 1.0f;

        [Header("Procedural Motion Curve")]
        [Tooltip("Defines the knockback movement over the normalized duration of the animation.")]
        public AnimationCurve knockbackCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Total peak distance traveled during knockback.")]
        public float knockbackDistance = 1.5f;
    }
}