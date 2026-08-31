using UnityEngine;

public class PowerPunchState : PlayerState
{
	private bool isCharging;
	private float chargeTimer;
	private float animationTimer;
	private float totalAnimationTime = 0.8f;
	private float pauseTime = 0.3f;

	public PowerPunchState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

	public override void Enter()
	{
		base.Enter();
		isCharging = true;
		chargeTimer = 0f;
		animationTimer = 0f;

		controller.CurrentChargeMultiplier = 1f;
		controller.VerticalVelocity = new Vector3(0f, controller.VerticalVelocity.y, 0f);

		AttackData chargeData = controller.equippedStyle != null ? controller.equippedStyle.GetActiveChargeAttack() : null;

		if (chargeData != null)
		{
			totalAnimationTime = chargeData.animationDuration;
			pauseTime = chargeData.chargePauseTime;
		}

		if (controller.Animator != null)
		{
			// Ensure normal speed when starting
			controller.Animator.speed = 1f;

			string animName = chargeData != null ? chargeData.animationTriggerName : "PowerPunchFire";
			controller.Animator.CrossFade(animName, 0.1f);
		}
	}

	public override void LogicUpdate()
	{
		base.LogicUpdate();

		// Allow rotation while charging/punching
		Vector3 inputDir = controller.GetIsometricInputDirection();
		if (inputDir.sqrMagnitude > 0.01f)
		{
			controller.RotateTowards(inputDir);
		}

		if (isCharging)
		{
			// Track how long the button is held for maximum damage multiplier
			chargeTimer += Time.deltaTime;

			// Progress the animation timer up to the freeze point
			if (animationTimer < pauseTime)
			{
				animationTimer += Time.deltaTime;
			}
			else
			{
				// FREEZE THE ANIMATION!
				if (controller.Animator != null) controller.Animator.speed = 0f;
			}

			// WHEN YOU RELEASE THE BUTTON:
			if (!input.PowerPunchHeld)
			{
				isCharging = false;
				controller.CurrentChargeMultiplier = Mathf.Clamp(1f + (chargeTimer * 1.5f), 1f, 2.5f);

				// UNFREEZE THE ANIMATION!
				if (controller.Animator != null) controller.Animator.speed = 1f;
			}
		}
		else
		{
			// The button is released, keep tracking time until the animation finishes
			animationTimer += Time.deltaTime;
			if (animationTimer >= totalAnimationTime)
			{
				controller.TransitionToState(controller.GroundedState);
			}
		}
	}

	public override void Exit()
	{
		base.Exit();
		// Failsafe: Always make sure the Animator speed is reset when leaving the state!
		if (controller.Animator != null) controller.Animator.speed = 1f;
	}
}