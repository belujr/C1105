using UnityEngine;

public class GrappleState : PlayerState
{
	private Transform targetTransform;
	private Vector3 startPosition;

	private float grappleProgress;
	private float totalGrappleTime;

	private float initialGrappleDistance;
	private bool isEnemyGrapple;

	// --- NEW: Tracks if you pressed attack while flying in! ---
	private bool hasBufferedAttack;

	public override bool CanBeInterrupted => false;

	public GrappleState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

	public void SetTarget(Transform target)
	{
		targetTransform = target;
		isEnemyGrapple = target.GetComponent<IDamageable>() != null;
	}

	public override void Enter()
	{
		base.Enter();

		controller.IsGravityEnabled = false;
		controller.VerticalVelocity = Vector3.zero;
		hasBufferedAttack = false; // Reset buffer

		if (controller.Animator != null)
		{
			controller.Animator.SetFloat("Speed", 0f);
		}

		startPosition = controller.transform.position;
		grappleProgress = 0f;

		Vector3 targetPoint = targetTransform.position + (Vector3.up * 1.0f);
		initialGrappleDistance = Vector3.Distance(startPosition, targetPoint);
		totalGrappleTime = Mathf.Max(0.1f, initialGrappleDistance / controller.stats.grappleSpeed);

		if (controller.ropeRenderer != null)
		{
			controller.ropeRenderer.enabled = true;
			controller.ropeRenderer.positionCount = 2;
			controller.ropeRenderer.useWorldSpace = true;

			Vector3 startHandPos = controller.grappleOrigin != null ? controller.grappleOrigin.position : startPosition + Vector3.up;
			controller.ropeRenderer.SetPosition(0, startHandPos);
			controller.ropeRenderer.SetPosition(1, targetPoint);
		}
	}

	public override void LogicUpdate()
	{
		base.LogicUpdate();

		if (targetTransform == null)
		{
			controller.TransitionToState(controller.AirborneState);
			return;
		}

		// --- INSTANT COMBAT FIX: Listen for attack presses mid-air ---
		if (isEnemyGrapple && input.AttackTriggered)
		{
			hasBufferedAttack = true;
		}

		Vector3 visualTargetPoint = targetTransform.position + (Vector3.up * 1.0f);

		grappleProgress += Time.deltaTime;
		float t = Mathf.Clamp01(grappleProgress / totalGrappleTime);

		Vector3 linearPosition = Vector3.Lerp(startPosition, visualTargetPoint, t);
		float currentDip = isEnemyGrapple ? 0f : controller.stats.grappleArcDip;
		float dipAmount = currentDip * Mathf.Sin(t * Mathf.PI);
		Vector3 idealArcPosition = linearPosition - (Vector3.up * dipAmount);

		Vector3 pullDir = idealArcPosition - controller.transform.position;

		// --- CRITICAL COLLISION FIX: Stay flat on the ground! ---
		// We force the Y direction to 0 so you don't ramp up onto the enemy's head
		if (isEnemyGrapple)
		{
			pullDir.y = 0f;
		}

		if (pullDir.sqrMagnitude > 0.001f)
		{
			pullDir.Normalize();
		}
		else
		{
			pullDir = (visualTargetPoint - controller.transform.position).normalized;
			if (isEnemyGrapple) pullDir.y = 0f;
		}

		controller.CharacterController.Move(pullDir * controller.stats.grappleSpeed * Time.deltaTime);
		Vector3 rawSwingVelocity = pullDir * controller.stats.grappleSpeed;

		// Measure distance strictly on a flat 2D plane for enemies so we don't undershoot
		float distanceToTarget;
		if (isEnemyGrapple)
		{
			Vector3 flatPlayer = new Vector3(controller.transform.position.x, 0f, controller.transform.position.z);
			Vector3 flatTarget = new Vector3(targetTransform.position.x, 0f, targetTransform.position.z);
			distanceToTarget = Vector3.Distance(flatPlayer, flatTarget);
		}
		else
		{
			distanceToTarget = Vector3.Distance(controller.transform.position, visualTargetPoint);
		}

		// --- THE RELEASE TRIGGER ---
		if (distanceToTarget <= controller.stats.grappleReleaseDistance || t >= 1f || !input.GrappleHeld)
		{
			if (isEnemyGrapple)
			{
				// Stop momentum completely so we don't slide around them
				controller.VerticalVelocity = new Vector3(0f, controller.VerticalVelocity.y, 0f);

				// --- INSTANT ATTACK TRIGGER ---
				// If you pressed X during the zip-line, unleash the punch immediately!
				if (hasBufferedAttack || input.AttackTriggered)
				{
					controller.TransitionToState(controller.AttackState);
				}
				else
				{
					controller.TransitionToState(controller.GroundedState);
				}
				return;
			}
			else
			{
				// Standard Environmental Grapple Logic
				float distanceRatio = Mathf.Clamp01(initialGrappleDistance / controller.stats.grappleMaxRange);
				float dynamicHorizontalCap = Mathf.Lerp(6f, 11f, distanceRatio);
				float dynamicUpwardMin = Mathf.Lerp(3f, 4.5f, distanceRatio);

				Vector3 horizontalMomentum = new Vector3(rawSwingVelocity.x, 0f, rawSwingVelocity.z);
				horizontalMomentum = Vector3.ClampMagnitude(horizontalMomentum, dynamicHorizontalCap);
				float verticalMomentum = Mathf.Clamp(rawSwingVelocity.y, dynamicUpwardMin, 5f);

				controller.VerticalVelocity = horizontalMomentum + (Vector3.up * verticalMomentum);
				controller.TransitionToState(controller.AirborneState);
				return;
			}
		}

		// --- FACE THE TARGET ---
		Vector3 lookDir = visualTargetPoint - controller.transform.position;
		lookDir.y = 0f;
		if (lookDir.sqrMagnitude > 0.01f)
		{
			controller.transform.rotation = Quaternion.Slerp(
				controller.transform.rotation,
				Quaternion.LookRotation(lookDir),
				20f * Time.deltaTime
			);
		}

		// --- VISUALS ---
		if (controller.ropeRenderer != null)
		{
			Vector3 handPos = controller.grappleOrigin != null ?
				controller.grappleOrigin.position :
				controller.transform.position + (Vector3.up * 1.0f);

			controller.ropeRenderer.SetPosition(0, handPos);
			controller.ropeRenderer.SetPosition(1, visualTargetPoint);
		}
	}

	public override void Exit()
	{
		base.Exit();
		controller.IsGravityEnabled = true;

		if (controller.ropeRenderer != null)
		{
			controller.ropeRenderer.enabled = false;
		}
	}
}