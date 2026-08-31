using UnityEngine;

public class AOEAttackState : PlayerState
{
	private float stateTimer;
	private float totalAnimationTime = 0.85f;

	public AOEAttackState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

	public override void Enter()
	{
		base.Enter();
		stateTimer = 0f;
		controller.VerticalVelocity = new Vector3(0f, controller.VerticalVelocity.y, 0f);

		if (controller.specialAttackY != null)
		{
			totalAnimationTime = controller.specialAttackY.animationDuration;

			if (controller.Animator != null)
			{
				controller.Animator.SetFloat("Speed", 0f);

				// --- NEW: DIRECT CROSSFADE TO AOE STATE ---
				controller.Animator.CrossFade(controller.specialAttackY.animationTriggerName, 0.1f);
			}
		}
	}

	public override void LogicUpdate()
	{
		base.LogicUpdate();
		stateTimer += Time.deltaTime;

		if (stateTimer >= totalAnimationTime)
		{
			if (controller.CharacterController.isGrounded)
				controller.TransitionToState(controller.GroundedState);
			else
				controller.TransitionToState(controller.AirborneState);
		}
	}
}