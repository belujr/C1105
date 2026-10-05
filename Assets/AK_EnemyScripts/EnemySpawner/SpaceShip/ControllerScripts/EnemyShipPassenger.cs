using System.Collections;
using UnityEngine;
using CombatSystem.Animation;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class EnemyShipPassenger : MonoBehaviour
{
    [Header("Flight & Landing Animations")]
    public AnimationClip hangingClip;
    public AnimationClip landingClip;
    
    [Tooltip("How long the landing animation takes before movement is fully active.")]
    public float landingDuration = 1.0f;

    [Header("AI & Physics to Disable")]
    [Tooltip("Drag the enemy's brain script here (ImpBrain or UltraInstinctCapsule).")]
    public MonoBehaviour enemyBrain;

    private CharacterController charController;
    private EnemyAnimationEngine animEngine;
    private Transform playerTransform;
    private Coroutine landRoutine;

    private void Awake()
    {
        charController = GetComponent<CharacterController>();
        animEngine = GetComponent<EnemyAnimationEngine>();
    }

    public void MountToShip(Transform mountPoint)
    {
        if (landRoutine != null)
        {
            StopCoroutine(landRoutine);
            landRoutine = null;
        }

        if (enemyBrain != null) 
        {
            if (enemyBrain is UltraInstinctCapsule ui) ui.ForceDisableUntilGrounded();
            else if (enemyBrain is ImpBrain imp) imp.ForceDisableUntilGrounded();
            
            enemyBrain.enabled = false;
        }

        // SHIP HAS EXCLUSIVE CONTROL: Turn off physics while hanging
        if (charController != null) charController.enabled = false;

        transform.SetParent(mountPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (animEngine != null && hangingClip != null)
        {
            animEngine.PlayAnimation(hangingClip, 0.1f, 1.0f);
        }
    }

    public void DetachAndLand()
    {
        if (landRoutine != null) return;
        landRoutine = StartCoroutine(LandRoutine());
    }

    private IEnumerator LandRoutine()
    {
        transform.SetParent(null);

        if (playerTransform == null)
        {
            PlayerController player = FindObjectOfType<PlayerController>();
            if (player != null) playerTransform = player.transform;
        }

        if (playerTransform != null)
        {
            Vector3 dirToPlayer = playerTransform.position - transform.position;
            dirToPlayer.y = 0f;
            if (dirToPlayer.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(dirToPlayer.normalized, Vector3.up);
            }
        }

        if (animEngine != null && landingClip != null)
        {
            animEngine.PlayAnimation(landingClip, 0.05f, 1.0f);
        }

        if (charController != null)
        {
            // Toggle off/on to flush stale physics states from the object pool
            charController.enabled = false;
            charController.enabled = true;
            charController.detectCollisions = true; // MUST be true so it physically stops at the floor mesh!
        }

        float verticalVelocity = 0f;
        float safetyTimer = 0f;

        while (charController != null && safetyTimer < 6.0f)
        {
            safetyTimer += Time.deltaTime;
            verticalVelocity -= 25f * Time.deltaTime;

            Vector3 move = new Vector3(0, verticalVelocity, 0);
            
            // Explicitly capture physical collision data this frame, ignoring the sticky isGrounded bool
            CollisionFlags flags = charController.Move(move * Time.deltaTime);

            // If the bottom of the capsule physically bumped into the floor, stop dropping!
            if ((flags & CollisionFlags.Below) != 0)
            {
                break;
            }

            // Fallback raycast safety net
            if (safetyTimer > 0.1f && Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 0.75f))
            {
                break;
            }

            yield return null;
        }

        // Enable brain after landing successfully
        if (enemyBrain != null)
        {
            if (enemyBrain is UltraInstinctCapsule ui) ui.AuthorizeGroundContactAndEnable();
            else if (enemyBrain is ImpBrain imp) imp.AuthorizeGroundContactAndEnable();
            
            enemyBrain.enabled = true;
        }

        yield return new WaitForSeconds(landingDuration);
        landRoutine = null;
    }
}