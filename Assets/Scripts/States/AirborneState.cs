using UnityEngine;

public class AirborneState : PlayerState
{
    private int remainingJumps;
    public float airDrag = 5f;


	public AirborneState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

    public void ResetJumps()
    {
		remainingJumps = controller.stats.maxJumps;
    }

    public void ExecuteJump()
    {
	
        if (remainingJumps <= 0) return;

        bool isDoubleJump = remainingJumps < controller.stats.maxJumps;

        controller.VerticalVelocity = new Vector3(
            controller.VerticalVelocity.x,
            Mathf.Sqrt(controller.stats.jumpHeight * -2f * controller.stats.gravity),
            controller.VerticalVelocity.z
        );
        remainingJumps--;

        if (controller.Animator != null)
        {
            if (isDoubleJump)
            {
                controller.Animator.SetTrigger("DoubleJump");
            }
            else
            {
                controller.Animator.SetTrigger("Jump");
            }
        }
        
	}

    public override void LogicUpdate()
    {
        base.LogicUpdate();


        // --- DAMP HORIZONTAL SWING / WALL JUMP MOMENTUM ---
        Vector3 currentVel = controller.VerticalVelocity;
        currentVel.x = Mathf.MoveTowards(currentVel.x, 0f, airDrag * Time.deltaTime);
        currentVel.z = Mathf.MoveTowards(currentVel.z, 0f, airDrag * Time.deltaTime);
        controller.VerticalVelocity = currentVel;


        // --- DASH CHECK ---
        if (input.DashTriggered && !controller.DashState.IsOnCooldown())
        {
            controller.TransitionToState(controller.DashState);
            return;
        }

        // --- DOUBLE JUMP CHECK ---
        if (input.JumpTriggered && remainingJumps > 0)
        {
            ExecuteJump();
        }

        // Air Movement Control
        Vector3 moveDir = controller.GetIsometricInputDirection();
        if (moveDir.sqrMagnitude > 0.01f)
        {
            controller.RotateTowards(moveDir);
            controller.CharacterController.Move(moveDir * controller.stats.walkSpeed * Time.deltaTime);
        }

        // Ground Check Transition
        if (controller.CharacterController.isGrounded && controller.VerticalVelocity.y <= 0)
        {
            controller.TransitionToState(controller.GroundedState);
        }
    }
}