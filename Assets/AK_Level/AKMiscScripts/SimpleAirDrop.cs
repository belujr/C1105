using System.Collections;
using UnityEngine;
using CombatSystem.Animation; // Required to access your EnemyAnimationEngine

/// A lightweight drop script that replaces EnemyShipPassenger.
/// It drops the enemy in a straight line, plays a landing animation, and wakes up their brain.
public class SimpleAirDrop : MonoBehaviour
{
    [Header("Drop Settings")]
    [Tooltip("How fast the enemy falls.")]
    public float fallSpeed = 35f;
    
    [Tooltip("The layers that count as the floor. (Default layer is automatically included via code).")]
    public LayerMask groundMask = ~0;

    [Header("Landing Animation")]
    [Tooltip("The animation clip to play right when hitting the ground.")]
    public AnimationClip landingClip;
    
    [Tooltip("How long to wait for the landing animation before waking up the AI brains.")]
    public float landingDuration = 1.0f;

    private void Awake()
    {
        // Failsafe: Guarantees the Default layer (Layer 0) is always checked so they never fall through the floor.
        groundMask |= (1 << 0);
    }

    public void DropAndWakeUp()
    {
        StartCoroutine(FallRoutine());
    }

    private IEnumerator FallRoutine()
    {
        CharacterController charController = GetComponent<CharacterController>();
        EnemyAnimationEngine animEngine = GetComponent<EnemyAnimationEngine>();
        
        if (charController != null) charController.enabled = false;

        float feetOffset = 0f;
        if (charController != null)
        {
            feetOffset = (charController.height * 0.5f - charController.center.y) * Mathf.Abs(transform.lossyScale.y);
        }

        while (true)
        {
            float step = fallSpeed * Time.deltaTime;
            Vector3 origin = transform.position;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, step + 0.5f, groundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 snappedPos = hit.point;
                snappedPos.y += feetOffset;
                transform.position = snappedPos;
                break; 
            }

            transform.position += Vector3.down * step;
            yield return null;
        }

        // --- GROUND HIT! WAKE EVERYTHING UP ---

        // 1. Turn on collisions so the enemy doesn't slide around during the animation
        if (charController != null) charController.enabled = true;

        // 2. Play the landing animation using your custom animation engine logic
        if (animEngine != null && landingClip != null)
        {
            animEngine.PlayAnimation(landingClip, 0.05f, 1.0f);
        }

        // 3. Pause the coroutine so the animation can play out before the AI starts attacking or walking
        if (landingDuration > 0f)
        {
            yield return new WaitForSeconds(landingDuration);
        }

        // 4. Turn on the AI brains
        var capsuleBrain = GetComponent<UltraInstinctCapsule>();
        if (capsuleBrain != null) capsuleBrain.enabled = true;

        var impBrain = GetComponent<ImpBrain>();
        if (impBrain != null) impBrain.enabled = true;

        var healerBrain = GetComponent<HealerController>();
        if (healerBrain != null) healerBrain.enabled = true;
    }
}