using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyDropCollisionController : MonoBehaviour
{
    [Header("Landing Tuning")]
    [Tooltip("Distance above the floor to detect ground contact and re-enable collisions.")]
    [SerializeField] private float groundCheckDistance = 0.3f;

    private CharacterController charController;
    private Collider[] enemyColliders;
    private bool hasLanded = false;

    private void Awake()
    {
        charController = GetComponent<CharacterController>();
        enemyColliders = GetComponentsInChildren<Collider>();
    }

    private void OnEnable()
    {
        hasLanded = false;

        // Turn off CharacterController collision detection so it ignores the ship and other enemies while falling
        if (charController != null)
        {
            charController.detectCollisions = false;
        }

        // Turn off all other colliders on the enemy
        foreach (var col in enemyColliders)
        {
            if (col != null)
            {
                col.enabled = false;
            }
        }
    }

    private void Update()
    {
        if (hasLanded) return;

        // Check if the enemy has reached the ground using a downward raycast
        if (Physics.Raycast(transform.position + (Vector3.up * 0.5f), Vector3.down, out RaycastHit hit, 1.0f))
        {
            if (hit.distance <= groundCheckDistance + 0.4f || (charController != null && charController.isGrounded))
            {
                EnableCollisions();
            }
        }
        else if (charController != null && charController.isGrounded)
        {
            EnableCollisions();
        }
    }

    private void EnableCollisions()
    {
        if (hasLanded) return;
        hasLanded = true;

        // Re-enable CharacterController collision detection
        if (charController != null)
        {
            charController.detectCollisions = true;
        }

        // Turn all colliders back on now that they are safely on land
        foreach (var col in enemyColliders)
        {
            if (col != null)
            {
                col.enabled = true;
            }
        }
    }
}