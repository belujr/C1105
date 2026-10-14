using UnityEngine;

public class AirborneState : PlayerState
{
    private const float airControlSpeed = 4f;   // gentle steering while falling, set to 0 to disable
    private const float minAirTime = 0.1f;      // stops instant "landed" right after a grapple launch
    private float airTimer;

    public AirborneState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

    public override void Enter()
    {
        base.Enter();
        airTimer = 0f;

        if (controller.Animator != null)
        {
            controller.Animator.speed = 1f;
            controller.Animator.SetBool("IsGrounded", false);
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        airTimer += Time.deltaTime;

        // Landed
        if (airTimer >= minAirTime && controller.CharacterController.isGrounded && controller.VerticalVelocity.y <= 0f)
        {
            controller.TransitionToState(controller.GroundedState);
            return;
        }

        // Light air steering
        Vector3 moveDir = controller.GetIsometricInputDirection();
        if (airControlSpeed > 0f && moveDir.sqrMagnitude > 0.01f)
        {
            controller.RotateTowards(moveDir);
            controller.CharacterController.Move(moveDir * airControlSpeed * Time.deltaTime);
        }
    }
}