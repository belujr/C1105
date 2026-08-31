using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using CombatSystem.Data;
using CombatSystem.Animation;

namespace CombatSystem.Controllers
{
    [RequireComponent(typeof(EnemyAnimationEngine))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(DummyHealth))]
    public class EnemyDummyController : MonoBehaviour
    {
        [Header("Animation Profile")]
        [SerializeField] private EnemyAnimProfile animProfile;

        [Header("Combo Tracking & Hit Filtering")]
        [SerializeField] private float hitCooldown = 0.18f;
        [SerializeField] private float comboWindow = 0.45f;
        [SerializeField] private float heavyAttackKnockbackThreshold = 3.5f;

        private EnemyAnimationEngine animEngine;
        private CharacterController characterController;
        private DummyHealth dummyHealth;

        private bool isHitStunned = false;
        private bool isDead = false;
        private bool isGettingUp = false;
        private bool isAirborne = false;
        private bool diedFromAirborneHit = false; // Tracks if killed by an uppercut/launcher
        private float verticalVelocity = 0f;

        private int currentComboIndex = 0;
        private float lastHitTime = -999f;
        private float lastRealHitTime = -999f;

        private void Awake()
        {
            animEngine = GetComponent<EnemyAnimationEngine>();
            characterController = GetComponent<CharacterController>();
            dummyHealth = GetComponent<DummyHealth>();
        }

        private void OnEnable()
        {
            if (dummyHealth != null)
            {
                dummyHealth.OnDeath += HandleDeath;
                dummyHealth.OnRevive += HandleRevive;
            }
        }

        private void OnDisable()
        {
            if (dummyHealth != null)
            {
                dummyHealth.OnDeath -= HandleDeath;
                dummyHealth.OnRevive -= HandleRevive;
            }
        }

        private void Start()
        {
            PlayIdle();
        }

        public void PlayIdle()
        {
            if (isDead) return;
            currentComboIndex = 0;
            diedFromAirborneHit = false;
            if (animProfile != null && animProfile.idleClip != null)
            {
                animEngine.PlayAnimation(animProfile.idleClip, animProfile.idleTransitionDuration, 1.0f);
            }
        }

        public void ProcessHit(HitData hitData, HitDirection direction, AudioClip hitSfx, float knockbackForce)
        {
            if (isDead) return;

            if (Time.time - lastRealHitTime < hitCooldown) return;
            lastRealHitTime = Time.time;

            if (isGettingUp)
            {
                StopAllCoroutines();
                isGettingUp = false;
                isDead = false;
            }

            if (isHitStunned)
            {
                StopAllCoroutines();
            }

            StartCoroutine(HitReactionRoutine(hitData, direction, hitSfx, knockbackForce));
        }

        private IEnumerator HitReactionRoutine(HitData hitData, HitDirection direction, AudioClip hitSfx, float knockbackForce)
        {
            isHitStunned = true;

            AttackReactionData matchedReaction = null;
            if (knockbackForce >= heavyAttackKnockbackThreshold)
            {
                matchedReaction = FindReactionByKnockback(knockbackForce);
            }
            else
            {
                matchedReaction = GetNextLightComboReaction();
            }

            HitAnimationData hitAnim = null;
            bool launchAirborne = false;
            float upwardForce = 0f;
            bool orientToAttacker = false;

            if (matchedReaction != null)
            {
                orientToAttacker = matchedReaction.orientTowardsAttacker;
                if (orientToAttacker) direction = HitDirection.Front;

                hitAnim = GetReactionAnimation(matchedReaction, direction);
                launchAirborne = matchedReaction.isAirborneLaunch;
                upwardForce = matchedReaction.launchUpwardForce;
            }

            if (hitAnim == null || hitAnim.clip == null)
            {
                hitAnim = GetDefaultHitAnimation(direction);
            }

            if (hitAnim == null || hitAnim.clip == null)
            {
                isHitStunned = false;
                PlayIdle();
                yield break;
            }

            // Mark if this specific attack is an airborne launcher (Uppercut)
            if (launchAirborne)
            {
                diedFromAirborneHit = true;
            }

            if (orientToAttacker)
            {
                Vector3 lookDir = (hitData.hitPoint - transform.position);
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir.normalized);
                }
            }

            animEngine.PlayAnimation(hitAnim.clip, 0.01f, hitAnim.playbackSpeed);

            Vector3 knockbackDir = (transform.position - hitData.hitPoint).normalized;
            knockbackDir.y = 0f;
            if (knockbackDir == Vector3.zero) knockbackDir = -transform.forward;

            if (launchAirborne)
            {
                isAirborne = true;
                verticalVelocity = upwardForce;
            }

            float elapsed = 0f;
            float animDuration = hitAnim.clip.length / Mathf.Max(0.01f, hitAnim.playbackSpeed);

            while (elapsed < animDuration)
            {
                if (isDead) yield break;

                elapsed += Time.deltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / animDuration);

                float curveValue = hitAnim.knockbackCurve.Evaluate(normalizedTime);
                float currentMoveStep = curveValue * hitAnim.knockbackDistance * knockbackForce * Time.deltaTime;

                if (isAirborne)
                {
                    verticalVelocity -= 19.62f * Time.deltaTime;
                    if (characterController.isGrounded && verticalVelocity < 0f)
                    {
                        isAirborne = false;
                        verticalVelocity = 0f;
                    }
                }
                else
                {
                    verticalVelocity = -9.81f * Time.deltaTime;
                }

                Vector3 moveDelta = (knockbackDir * currentMoveStep) + (Vector3.up * verticalVelocity * Time.deltaTime);
                if (characterController != null) characterController.Move(moveDelta);

                yield return null;
            }

            isHitStunned = false;
            PlayIdle();
        }

        private void HandleDeath()
        {
            isDead = true;
            isGettingUp = false;
            StopAllCoroutines();

            // SUPPRESS DEATH ANIMATION IF KILLED BY AN UPPERCUT / LAUNCHER
            // This allows the dummy to stay in its awesome flying/uppercut reaction pose instead of cutting to a stiff death pose.
            if (!diedFromAirborneHit && animProfile != null && animProfile.deathClip != null)
            {
                animEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
            }
        }

        private void HandleRevive()
        {
            StartCoroutine(ReviveStandUpRoutine());
        }

        private IEnumerator ReviveStandUpRoutine()
        {
            isGettingUp = true;
            diedFromAirborneHit = false;

            if (animProfile != null && animProfile.standUpClip != null)
            {
                animEngine.PlayAnimation(animProfile.standUpClip, animProfile.standUpTransitionDuration, animProfile.standUpPlaybackSpeed);
                
                float getUpDuration = animProfile.standUpClip.length / Mathf.Max(0.01f, animProfile.standUpPlaybackSpeed);
                float vulnerabilityThreshold = getUpDuration * 0.4f; 
                yield return new WaitForSeconds(vulnerabilityThreshold);

                isDead = false; 

                float remainingTime = getUpDuration - vulnerabilityThreshold;
                float blendDuration = Mathf.Min(remainingTime, animProfile.standUpToIdleTransitionDuration);
                float waitBeforeBlend = Mathf.Max(0f, remainingTime - blendDuration);

                yield return new WaitForSeconds(waitBeforeBlend);

                animEngine.PlayAnimation(animProfile.idleClip, blendDuration, 1.0f);

                yield return new WaitForSeconds(blendDuration);
            }

            isGettingUp = false;
            isDead = false;
            PlayIdle();
        }

        private AttackReactionData GetNextLightComboReaction()
        {
            if (animProfile == null || animProfile.attackReactions == null || animProfile.attackReactions.Count == 0) return null;

            if (Time.time - lastHitTime <= comboWindow)
            {
                currentComboIndex++;
                if (currentComboIndex >= animProfile.attackReactions.Count) currentComboIndex = 0;
            }
            else
            {
                currentComboIndex = 0;
            }

            lastHitTime = Time.time;
            return animProfile.attackReactions[currentComboIndex];
        }

        private AttackReactionData FindReactionByKnockback(float incomingKnockback)
        {
            if (animProfile == null || animProfile.attackReactions == null || animProfile.attackReactions.Count == 0) return null;

            AttackReactionData closestReaction = null;
            float smallestDifference = Mathf.Infinity;

            foreach (var reaction in animProfile.attackReactions)
            {
                if (reaction != null)
                {
                    float difference = Mathf.Abs(reaction.targetKnockbackForce - incomingKnockback);
                    if (difference < smallestDifference)
                    {
                        smallestDifference = difference;
                        closestReaction = reaction;
                    }
                }
            }
            return closestReaction;
        }

        private HitAnimationData GetReactionAnimation(AttackReactionData reaction, HitDirection direction)
        {
            switch (direction)
            {
                case HitDirection.Front: return reaction.reactionFront;
                case HitDirection.Back: return reaction.reactionBack;
                case HitDirection.Left: return reaction.reactionLeft;
                case HitDirection.Right: return reaction.reactionRight;
                default: return reaction.reactionFront;
            }
        }

        private HitAnimationData GetDefaultHitAnimation(HitDirection direction)
        {
            if (animProfile == null) return null;

            switch (direction)
            {
                case HitDirection.Front: return animProfile.defaultHitFront;
                case HitDirection.Back: return animProfile.defaultHitBack;
                case HitDirection.Left: return animProfile.defaultHitLeft;
                case HitDirection.Right: return animProfile.defaultHitRight;
                default: return animProfile.defaultHitFront;
            }
        }
    }
}