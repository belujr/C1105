using UnityEngine;
using System.Collections;
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

        [Header("Combat Feel & Impact")]
        [SerializeField] private float hitStopDuration = 0.04f;

        [Header("Hit Filtering")]
        [SerializeField] private float hitCooldown = 0.02f;
        [SerializeField] private float comboWindow = 0.45f;

        private EnemyAnimationEngine animEngine;
        private CharacterController characterController;
        private DummyHealth dummyHealth;

        private bool isHitStunned = false;
        private bool isDead = false;
        private bool isGettingUp = false;
        private bool isAirborne = false;
        private float verticalVelocity = 0f;

        public bool IsGettingUp => isGettingUp;

        private int currentComboIndex = 0;
        private float lastHitTime = -999f;
        private float lastRealHitTime = -999f;
        private Coroutine activeRoutine = null;

        private void Awake()
        {
            animEngine = GetComponent<EnemyAnimationEngine>();
            characterController = GetComponent<CharacterController>();
            dummyHealth = GetComponent<DummyHealth>();

            if (animProfile != null) animProfile.InitializeDictionary();
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

        private void HandleRevive()
        {
            isDead = false;
            isGettingUp = false;
            isHitStunned = false;
            isAirborne = false;
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            PlayIdle();
        }

        public void PlayIdle()
        {
            if (isDead) return;
            currentComboIndex = 0;
            if (animProfile != null && animProfile.idleClip != null)
            {
                animEngine.PlayAnimation(animProfile.idleClip, animProfile.idleTransitionDuration, 1.0f);
            }
        }

        public void ProcessHit(HitData hitData, HitDirection direction, int damage, float knockbackForce)
        {
            if (isDead || isGettingUp) return;

            if (Time.time - lastRealHitTime < hitCooldown) return;
            lastRealHitTime = Time.time;

            if (isHitStunned && activeRoutine != null) StopCoroutine(activeRoutine);

            activeRoutine = StartCoroutine(HitReactionRoutine(hitData, direction, damage, knockbackForce));
        }

        private IEnumerator HitReactionRoutine(HitData hitData, HitDirection direction, int damage, float knockbackForce)
        {
            float elapsedHitStop = 0f;
            while (elapsedHitStop < hitStopDuration)
            {
                elapsedHitStop += Time.unscaledDeltaTime;
                yield return null;
            }

            isHitStunned = true;

            AttackReactionData matchedReaction = animProfile != null ? animProfile.GetReaction(damage) : null;
            if (matchedReaction == null) matchedReaction = GetNextLightComboReaction();

            HitAnimationData hitAnim = null;
            bool launchAirborne = false;
            bool canTrip = false;
            float upwardForce = 0f;
            bool orientToAttacker = false;

            if (matchedReaction != null)
            {
                orientToAttacker = matchedReaction.orientTowardsAttacker;
                if (orientToAttacker) direction = HitDirection.Front;

                hitAnim = GetReactionAnimation(matchedReaction, direction);
                launchAirborne = matchedReaction.isAirborneLaunch;
                canTrip = matchedReaction.canFallDown;
                upwardForce = matchedReaction.launchUpwardForce;
            }

            if (hitAnim == null || hitAnim.clip == null) hitAnim = GetDefaultHitAnimation(direction);

            if (hitAnim == null || hitAnim.clip == null)
            {
                isHitStunned = false;
                PlayIdle();
                yield break;
            }

            if (orientToAttacker)
            {
                Vector3 lookDir = (hitData.hitPoint - transform.position);
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(lookDir.normalized);
            }

            animEngine.PlayAnimation(hitAnim.clip, 0.0f, hitAnim.playbackSpeed);

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

            if (dummyHealth != null && dummyHealth.IsDead)
            {
                activeRoutine = StartCoroutine(GroundedDeathAndReviveRoutine());
                yield break;
            }

            if (canTrip && matchedReaction != null && matchedReaction.customStandUpAnimation != null && matchedReaction.customStandUpAnimation.clip != null)
            {
                activeRoutine = StartCoroutine(NonLethalTripGetUpRoutine(matchedReaction));
                yield break;
            }

            isHitStunned = false;
            PlayIdle();
        }

        private IEnumerator NonLethalTripGetUpRoutine(AttackReactionData tripReaction)
        {
            isGettingUp = true;
            yield return new WaitForSeconds(0.2f);

            AnimationClip getUpClip = tripReaction.customStandUpAnimation.clip;
            float transition = tripReaction.customStandUpAnimation.transitionDuration;
            float speed = tripReaction.customStandUpAnimation.playbackSpeed;

            if (getUpClip != null)
            {
                animEngine.PlayAnimation(getUpClip, transition, speed);
                float getUpDuration = getUpClip.length / Mathf.Max(0.01f, speed);
                yield return new WaitForSeconds(getUpDuration - animProfile.standUpToIdleTransitionDuration);
                animEngine.PlayAnimation(animProfile.idleClip, animProfile.standUpToIdleTransitionDuration, 1.0f);
                yield return new WaitForSeconds(animProfile.standUpToIdleTransitionDuration);
            }

            isGettingUp = false;
            PlayIdle();
        }

        private AttackReactionData GetNextLightComboReaction()
        {
            if (animProfile == null || animProfile.attackReactions == null || animProfile.attackReactions.Count == 0) return null;

            if (Time.time - lastHitTime > comboWindow) currentComboIndex = 0;

            int lightCount = Mathf.Min(2, animProfile.attackReactions.Count);
            AttackReactionData reactionToPlay = animProfile.attackReactions[currentComboIndex % lightCount];

            currentComboIndex++;
            if (currentComboIndex >= lightCount) currentComboIndex = 0;

            lastHitTime = Time.time;
            return reactionToPlay;
        }

        private void HandleDeath()
        {
            if (isDead) return;
            isDead = true;
            isGettingUp = false;
            
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            activeRoutine = StartCoroutine(GroundedDeathAndReviveRoutine());
        }

        private IEnumerator GroundedDeathAndReviveRoutine()
        {
            isDead = true;

            if (animProfile != null && animProfile.deathClip != null)
            {
                animEngine.PlayAnimation(animProfile.deathClip, animProfile.deathTransitionDuration, animProfile.deathPlaybackSpeed);
            }

            float cooldownTime = dummyHealth != null ? dummyHealth.ReviveCooldown : 2.0f;
            yield return new WaitForSeconds(cooldownTime);

            if (dummyHealth != null && dummyHealth.isDummy)
            {
                isGettingUp = true;

                AnimationClip standUpClipToUse = animProfile.standUpClip;
                float standUpTransitionToUse = animProfile.standUpTransitionDuration;
                float standUpSpeedToUse = animProfile.standUpPlaybackSpeed;

                dummyHealth.Revive(); 

                if (standUpClipToUse != null)
                {
                    animEngine.PlayAnimation(standUpClipToUse, standUpTransitionToUse, standUpSpeedToUse);
                    float getUpDuration = standUpClipToUse.length / Mathf.Max(0.01f, standUpSpeedToUse);
                    yield return new WaitForSeconds(getUpDuration - animProfile.standUpToIdleTransitionDuration);
                    animEngine.PlayAnimation(animProfile.idleClip, animProfile.standUpToIdleTransitionDuration, 1.0f);
                    yield return new WaitForSeconds(animProfile.standUpToIdleTransitionDuration);
                }
                else
                {
                    yield return new WaitForSeconds(0.5f);
                }

                isGettingUp = false;
                PlayIdle();
            }
        }

        private IEnumerator AnticipationAndExecuteRoutine(AnimationClip anticipationClip, AnimationClip mainClip, float windUpTime)
        {
            if (anticipationClip != null) animEngine.PlayAnimation(anticipationClip, 0.0f, 1.0f);
            yield return new WaitForSeconds(windUpTime);
            if (mainClip != null) animEngine.PlayAnimation(mainClip, 0.0f, 1.0f);
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