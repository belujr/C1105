using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyGravityHandler : MonoBehaviour
{
    [Header("Gravity Tuning")]
    [Tooltip("Downward acceleration speed for gravity.")]
    [SerializeField] private float gravityForce = 20f;

    [Header("Layer Exemption")]
    [Tooltip("If the enemy is set to any layer checked here, gravity will be completely ignored.")]
    [SerializeField] private LayerMask ignoreGravityLayers;

    private CharacterController charController;
    private float verticalVelocity = 0f;
    private bool hasLanded = false;

    private void Awake()
    {
        charController = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        // Reset landing state whenever the enemy spawns or is pulled from the pool
        hasLanded = false;
        verticalVelocity = 0f;
    }

    private void Update()
    {
        // --- THE FIX ---
        // Abort the update if the controller is missing, disabled, or the enemy is deactivated.
        if (charController == null || !charController.enabled || !gameObject.activeInHierarchy) return;

        // Check if the enemy's current layer matches the ignored layers mask
        bool isIgnoredLayer = (ignoreGravityLayers.value & (1 << gameObject.layer)) != 0;

        // Once they have landed on the ground or are on an ignored layer, turn off gravity completely
        if (isIgnoredLayer || hasLanded)
        {
            verticalVelocity = 0f;
            return;
        }

        // Check if CharacterController has touched the floor
        if (charController.isGrounded)
        {
            hasLanded = true;
            verticalVelocity = 0f;
            return;
        }

        // Apply gravity while falling through the air from the spaceship drop
        verticalVelocity -= gravityForce * Time.deltaTime;
        Vector3 gravityMove = new Vector3(0f, verticalVelocity, 0f);
        charController.Move(gravityMove * Time.deltaTime);
    }
}