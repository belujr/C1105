using UnityEngine;

public class FullComboState : PlayerState
{
	private float sequenceTimer;
	private float totalDuration = 2.5f; // Adjust this to match your exact animation length in seconds
	private bool hasLaunchedEnemy = false;

	public FullComboState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

	public override void Enter()
	{
		base.Enter();
		sequenceTimer = totalDuration;
		hasLaunchedEnemy = false;

		// Face the input direction or nearest enemy immediately
		Vector3 dir = controller.GetIsometricInputDirection();
		if (dir.sqrMagnitude > 0.01f)
		{
			controller.transform.rotation = Quaternion.LookRotation(dir);
		}

		// Fire the Animator trigger for the full sequence
		if (controller.Animator != null)
		{
			controller.Animator.SetTrigger("FullCombo");
		}
	}

	public override void LogicUpdate()
	{
		base.LogicUpdate();
		sequenceTimer -= Time.deltaTime;

		// --- PHASED FORWARD MOVEMENT ---
		// During the initial elbow hook and speed punches, lunge forward smoothly
		float progress = 1f - (sequenceTimer / totalDuration);

		if (progress < 0.6f) // First 60% of the animation is the rushing punches
		{
			controller.CharacterController.Move(controller.transform.forward * 3.5f * Time.deltaTime);
		}

		// --- THE LAUNCHER TIMING ---
		// Assuming the final upward push happens around 70% into the animation timeline
		if (progress >= 0.7f && !hasLaunchedEnemy)
		{
			hasLaunchedEnemy = true;
			ExecuteLauncher();
		}

		// Exit back to grounded state when the full animation finishes
		if (sequenceTimer <= 0f)
		{
			controller.TransitionToState(controller.GroundedState);
		}
	}

	private void ExecuteLauncher()
	{
		// Find the enemy right in front of the player and launch them into the air!
		Collider[] hits = Physics.OverlapSphere(controller.transform.position + controller.transform.forward * 1f, 1.5f);
		foreach (var hit in hits)
		{
			IDamageable damageable = hit.GetComponent<IDamageable>();
			if (damageable != null && hit.transform != controller.transform)
			{
				// Apply damage and pass an upward vector to send them flying
				Vector3 launchDirection = (controller.transform.forward + Vector3.up * 2f).normalized;
				damageable.TakeDamage(15, hit.transform.position, launchDirection);

				// Optional: If your enemy has a custom launcher coroutine, trigger it here!
				break;
			}
		}
	}
}