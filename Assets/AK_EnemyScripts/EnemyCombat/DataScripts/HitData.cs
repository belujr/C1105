using UnityEngine;

namespace CombatSystem.Data
{
    /// <summary>
    /// Encapsulates all data related to a combat hit. 
    /// Passed from the player's hitbox to any IDamageable entity.
    /// </summary>
    [System.Serializable]
    public struct HitData
    {
        [Header("Damage & Impact")]
        [Tooltip("Amount of damage dealt by this attack.")]
        public float damage;

        [Tooltip("The exact world-space position where the hitbox intersected the hurtbox.")]
        public Vector3 hitPoint;

        [Tooltip("The surface normal at the point of impact (useful for particle spawns).")]
        public Vector3 hitNormal;

        [Header("Physics & Feedback")]
        [Tooltip("The magnitude of the knockback impulse applied to the receiver.")]
        public float knockbackForce;

        [Tooltip("Duration in seconds that the receiver pauses (hit-stop) on impact for heavy hit feel.")]
        public float hitStopDuration;

        [Tooltip("The GameObject that initiated the attack (e.g., the Player).")]
        public GameObject instigator;

        /// <summary>
        /// Constructor to easily initialize a hit payload.
        /// </summary>
        public HitData(float damage, Vector3 hitPoint, Vector3 hitNormal, float knockbackForce, float hitStopDuration, GameObject instigator)
        {
            this.damage = damage;
            this.hitPoint = hitPoint;
            this.hitNormal = hitNormal;
            this.knockbackForce = knockbackForce;
            this.hitStopDuration = hitStopDuration;
            this.instigator = instigator;
        }
    }
}