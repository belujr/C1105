using UnityEngine;

public class AttackState : PlayerState
{
	private float attackTimer;
	private bool hasBufferedNextAttack;
	private Transform targetEnemy;

	private readonly float attackSpeedMultiplier = 1.4f;

	public AttackState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

	public override void Enter()
	{
		base.Enter();

		controller.CharacterController.Move(Vector3.down * 1.5f);
		controller.VerticalVelocity = Vector3.zero;
		controller.LastMoveDirection = Vector3.zero; // <-- Helps prevent ice-skating!

		if (controller.Animator != null)
		{
			controller.Animator.SetFloat("Speed", 0f);
			controller.Animator.SetBool("IsGrounded", true);
			controller.Animator.speed = attackSpeedMultiplier;
		}

		if (Time.time > controller.LastAttackEndTime + controller.stats.comboResetWindow)
		{
			controller.CurrentComboIndex = 0;
		}

		hasBufferedNextAttack = false;
		controller.RefreshCombatStance();
		targetEnemy = FindNearestEnemy();

		if (targetEnemy != null)
		{
			Vector3 toEnemy = targetEnemy.position - controller.transform.position;
			toEnemy.y = 0;
			if (toEnemy.sqrMagnitude > 0.001f) controller.transform.rotation = Quaternion.LookRotation(toEnemy.normalized);
		}
		else
		{
			Vector3 attackDir = controller.GetIsometricInputDirection();
			if (attackDir.sqrMagnitude > 0.01f) controller.transform.rotation = Quaternion.LookRotation(attackDir);
		}

		// --- FETCH FROM NEW COMBAT STYLE ---
		if (controller.equippedStyle != null && controller.equippedStyle.lightComboSequence != null && controller.equippedStyle.lightComboSequence.Length > 0)
		{
			controller.CurrentComboIndex = Mathf.Clamp(controller.CurrentComboIndex, 0, controller.equippedStyle.lightComboSequence.Length - 1);
			AttackData currentAttack = controller.equippedStyle.lightComboSequence[controller.CurrentComboIndex];

			attackTimer = currentAttack.animationDuration / attackSpeedMultiplier;

			if (controller.Animator != null)
			{
				// --- NEW: DIRECT CROSSFADE USING DATA ---
				// We completely removed the WeaponStance, AttackIndex, and Triggers!
				// It now smoothly snaps right into the animation using your custom transitionDuration.
				controller.Animator.CrossFadeInFixedTime(currentAttack.animationTriggerName, currentAttack.transitionDuration);
			}
		}
		else
		{
			attackTimer = 0.5f;
		}
	}

	public override void LogicUpdate()
	{
		base.LogicUpdate();
		attackTimer -= Time.deltaTime;

		if (input.AttackTriggered) hasBufferedNextAttack = true;

		if (controller.equippedStyle != null && controller.equippedStyle.lightComboSequence != null && controller.equippedStyle.lightComboSequence.Length > controller.CurrentComboIndex)
		{
			AttackData currentAttack = controller.equippedStyle.lightComboSequence[controller.CurrentComboIndex];

			float totalDuration = currentAttack.animationDuration / attackSpeedMultiplier;
			float timeElapsed = totalDuration - attackTimer;
			float progress = timeElapsed / totalDuration;

			if (progress < 0.4f)
			{
				float lungeSpeed = currentAttack.forwardLungeSpeed;
				Vector3 moveDirection = controller.transform.forward;

				if (targetEnemy != null)
				{
					Vector3 toEnemy = targetEnemy.position - controller.transform.position;
					toEnemy.y = 0;
					float distanceToEnemy = toEnemy.magnitude;
					float distanceToStop = currentAttack.strikeDistance;

					if (distanceToEnemy > distanceToStop)
					{
						moveDirection = toEnemy.normalized;
						controller.RotateTowards(moveDirection);

						float gapToClose = distanceToEnemy - distanceToStop;
						lungeSpeed = gapToClose > 0.5f ? Mathf.Clamp(gapToClose / 0.05f, 5f, 25f) : currentAttack.forwardLungeSpeed;

						float maxMoveThisFrame = lungeSpeed * Time.deltaTime;
						if (maxMoveThisFrame > gapToClose) lungeSpeed = gapToClose / Time.deltaTime;
					}
					else if (distanceToEnemy < distanceToStop - 0.2f)
					{
						moveDirection = -toEnemy.normalized;
						lungeSpeed = 2.0f;
						controller.RotateTowards(toEnemy.normalized);
					}
					else
					{
						lungeSpeed = 0f;
						moveDirection = toEnemy.normalized;
						controller.RotateTowards(moveDirection);
					}
				}

				Vector3 finalVelocity = moveDirection * lungeSpeed;
				if (controller.CharacterController.isGrounded && lungeSpeed > 3f) finalVelocity.y = -15f;
				controller.CharacterController.Move(finalVelocity * Time.deltaTime);
			}
		}

		if (attackTimer <= 0f)
		{
			controller.LastAttackEndTime = Time.time;

			// Check against lightComboSequence length
			if (hasBufferedNextAttack && controller.equippedStyle != null && controller.equippedStyle.lightComboSequence != null && controller.CurrentComboIndex < controller.equippedStyle.lightComboSequence.Length - 1)
			{
				controller.CurrentComboIndex++;
				Enter();
			}
			else
			{
				controller.CurrentComboIndex = 0;
				controller.TransitionToState(controller.GroundedState);
			}
		}
	}

	public override void Exit()
	{
		base.Exit();
		if (controller.Animator != null) controller.Animator.speed = 1f;
	}

	private Transform FindNearestEnemy()
	{
		Collider[] hits = Physics.OverlapSphere(controller.transform.position, controller.stats.magnetismRadius);
		Transform bestTarget = null;
		float bestScore = Mathf.Infinity;

		Vector3 inputDir = controller.GetIsometricInputDirection();
		bool hasInput = inputDir.sqrMagnitude > 0.01f;
		Vector3 searchDir = hasInput ? inputDir.normalized : controller.transform.forward;

		foreach (Collider hit in hits)
		{
			if (hit.GetComponent<IDamageable>() != null && hit.transform != controller.transform)
			{
				Vector3 toEnemy = hit.transform.position - controller.transform.position;
				toEnemy.y = 0;
				float distance = toEnemy.magnitude;
				if (distance == 0) continue;

				float angleToEnemy = Vector3.Angle(searchDir, toEnemy.normalized);
				if (angleToEnemy < 90f)
				{
					float score = distance + (angleToEnemy * 0.05f);
					if (score < bestScore)
					{
						bestScore = score;
						bestTarget = hit.transform;
					}
				}
			}
		}
		return bestTarget;
	}
}