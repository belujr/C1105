using UnityEngine;

public class DashState : PlayerState
{
    private float dashTimer;
    private Vector3 dashDirection;

    public DashState(PlayerController controller, PlayerInputHandler input) : base(controller, input)
    {
        MaxCooldown = controller.stats.dashCooldown;
    }

    public override void Enter()
    {
        base.Enter();
        dashTimer = controller.stats.dashDuration;
        CooldownTimer = controller.stats.dashCooldown;

        controller.IsGravityEnabled = false;
        controller.VerticalVelocity = Vector3.zero;

        Vector3 inputDir = controller.GetIsometricInputDirection();

        // --- UPDATED LOGIC: Auto-backstep on neutral input ---
        if (inputDir.sqrMagnitude > 0.01f)
        {
            // Input given: Dash in that direction and turn to face it
            dashDirection = inputDir;
            controller.transform.rotation = Quaternion.LookRotation(dashDirection);
        }
        else
        {
            // No input: Dash backward, but DO NOT rotate (slide backward while facing forward)
            dashDirection = -controller.transform.forward;
        }
        // -----------------------------------------------------

        // --- ENABLE DASH TRAIL ---
        if (controller.dashTrailRenderer != null)
        {
            controller.dashTrailRenderer.Clear();
            controller.dashTrailRenderer.emitting = true;
        }

        // Freeze animator during dash
        if (controller.Animator != null)
        {
            controller.Animator.speed = 0f;
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        dashTimer -= Time.deltaTime;

        controller.CharacterController.Move(dashDirection * controller.stats.dashSpeed * Time.deltaTime);

        if (dashTimer <= 0f)
        {
            if (controller.CharacterController.isGrounded)
            {
                controller.TransitionToState(controller.GroundedState);
            }
            else
            {
                controller.TransitionToState(controller.AirborneState);
            }
        }
    }

    public override void Exit()
    {
        base.Exit();
        controller.IsGravityEnabled = true;

        // --- STOP EMITTING TRAIL (Line naturally fades away) ---
        if (controller.dashTrailRenderer != null)
        {
            controller.dashTrailRenderer.emitting = false;
        }

        // Unfreeze animator
        if (controller.Animator != null)
        {
            controller.Animator.speed = 1f;
        }
    }
}