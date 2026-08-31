using UnityEngine;

public class GroundedState : PlayerState
{
    public GroundedState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

    public override void Enter()
    {
        base.Enter();
        controller.AirborneState.ResetJumps();

        // Reset horizontal residual momentum upon touching the ground
        controller.VerticalVelocity = new Vector3(0f, controller.VerticalVelocity.y, 0f);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (!controller.CharacterController.isGrounded)
        {
            controller.TransitionToState(controller.AirborneState);
            return;
        }

        // --- ATTACK TRIGGER (LB) ---
        if (input.AttackTriggered)
        {
            controller.TransitionToState(controller.AttackState);
            return;
        }

        // Check if combo window expired
        if (Time.time > controller.LastAttackEndTime + controller.stats.comboResetWindow)
        {
            controller.CurrentComboIndex = 0;
        }

        if (input.JumpTriggered)
        {
            controller.TransitionToState(controller.AirborneState);
            controller.AirborneState.ExecuteJump();
            return;
        }

        if (input.DashTriggered && !controller.DashState.IsOnCooldown())
        {
            controller.TransitionToState(controller.DashState);
            return;
        }

        if (input.SlideTriggered && !controller.SlideState.IsOnCooldown())
        {
            controller.TransitionToState(controller.SlideState);
            return;
        }


        // Movement & Animation
        Vector3 moveDir = controller.GetIsometricInputDirection();
        float targetSpeed = input.SprintTriggered ? controller.stats.sprintSpeed : controller.stats.walkSpeed;
        float currentSpeed = moveDir.magnitude * targetSpeed;

        if (controller.Animator != null)
        {
            controller.Animator.SetFloat("Speed", currentSpeed, 0.1f, Time.deltaTime);
        }

        if (moveDir.sqrMagnitude > 0.01f)
        {
            controller.LastMoveDirection = moveDir;
            controller.RotateTowards(moveDir);
            controller.CharacterController.Move(moveDir * targetSpeed * Time.deltaTime);
        }
    }
}