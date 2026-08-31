using UnityEngine;

[RequireComponent(typeof(CharacterController), typeof(PlayerInputHandler))]
public class PlayerController : MonoBehaviour
{
	[Header("Core Player Stats")]
	[Tooltip("Drag your new PlayerStatsData asset here!")]
	public PlayerStatsData stats;

	[Header("Component References")]
	public CharacterController CharacterController { get; private set; }
	public PlayerInputHandler InputHandler { get; private set; }
	public Animator Animator { get; private set; }
	[SerializeField] private Transform cameraTransform;

	[Header("Scene References (Cannot be in Data)")]
	public LayerMask groundAndObstacleLayers = ~0;
	public LayerMask grappleLayer;
	public Transform grappleOrigin;
	public LineRenderer ropeRenderer;
	public TrailRenderer dashTrailRenderer;
	public GameObject enemyGrappleReticle;

	[Header("Equipped Moveset")]
	[Tooltip("Drag your CombatStyle asset here!")]
	public CombatStyle equippedStyle;

	[Header("Special Ability (Y Button)")]
	[Tooltip("This attack is independent of your combat style. Drag an AttackData here!")]
	public AttackData specialAttackY;

	// Combat Tracking
	public float CurrentChargeMultiplier { get; set; } = 1f;
	public int CurrentComboIndex { get; set; } = 0;
	public float LastAttackEndTime { get; set; } = 0f;
	private float combatTimer = 0f;

	// Movement & Gravity tracking
	public Vector3 VerticalVelocity;
	public Vector3 LastMoveDirection { get; set; }

	public float DefaultHeight { get; private set; }
	public Vector3 DefaultCenter { get; private set; }

	// State Machine Properties
	public PlayerState CurrentState { get; private set; }

	public GroundedState GroundedState { get; private set; }
	public AirborneState AirborneState { get; private set; }
	public DashState DashState { get; private set; }
	public SlideState SlideState { get; private set; }
	public AttackState AttackState { get; private set; }
	public GrappleState GrappleState { get; private set; }
	public AOEAttackState AOEAttackState { get; private set; }
	public PowerPunchState PowerPunchState { get; private set; }

	public bool IsGravityEnabled { get; set; } = true;

	private void Awake()
	{
		CharacterController = GetComponent<CharacterController>();
		InputHandler = GetComponent<PlayerInputHandler>();
		Animator = GetComponentInChildren<Animator>();

		DefaultHeight = CharacterController.height;
		DefaultCenter = CharacterController.center;

		if (cameraTransform == null && Camera.main != null)
		{
			cameraTransform = Camera.main.transform;
		}

		// Initialize Remaining States
		GroundedState = new GroundedState(this, InputHandler);
		AirborneState = new AirborneState(this, InputHandler);
		DashState = new DashState(this, InputHandler);
		SlideState = new SlideState(this, InputHandler);
		AttackState = new AttackState(this, InputHandler);
		GrappleState = new GrappleState(this, InputHandler);
		AOEAttackState = new AOEAttackState(this, InputHandler);
		PowerPunchState = new PowerPunchState(this, InputHandler);
	}

	private void Start()
	{
		if (ropeRenderer != null) ropeRenderer.enabled = false;
		TransitionToState(GroundedState);
	}

	private void Update()
	{
		DashState?.UpdateCooldown(Time.deltaTime);
		SlideState?.UpdateCooldown(Time.deltaTime);

		// Global Cancel Window Check
		if (CurrentState != null && CurrentState.CanBeInterrupted)
		{
			if (CheckForInterrupts())
			{
				return;
			}
		}

		CurrentState?.LogicUpdate();

		if (IsGravityEnabled)
		{
			ApplyGravity();
		}

		if (Animator != null)
		{
			Animator.SetBool("IsGrounded", CharacterController.isGrounded);
		}

		UpdateCombatStance();
		UpdateGrappleReticle();
	}

	private void FixedUpdate()
	{
		CurrentState?.PhysicsUpdate();
	}

	private bool CheckForInterrupts()
	{
		// 1. DASH CANCELS ATTACKS
		if (InputHandler.DashTriggered && CurrentState != DashState)
		{
			TransitionToState(DashState);
			return true;
		}

		// 2. ATTACK CANCELS DASHES
		if (InputHandler.AttackTriggered && CurrentState != AttackState && CurrentState != SlideState)
		{
			TransitionToState(AttackState);
			return true;
		}

		// 3. OPPOSITE DIRECTION CANCEL
		if (CurrentState == DashState)
		{
			Vector3 inputDir = GetIsometricInputDirection();
			if (inputDir.sqrMagnitude > 0.1f && Vector3.Dot(inputDir, transform.forward) < -0.5f)
			{
				TransitionToState(GroundedState);
				return true;
			}
		}

		// --- RIGHT TRIGGER (POWER PUNCH CHARGE) ---
		if (InputHandler.PowerPunchHeld && CurrentState != PowerPunchState)
		{
			if (equippedStyle != null && equippedStyle.GetActiveChargeAttack() != null)
			{
				TransitionToState(PowerPunchState);
				return true;
			}
		}

		// ENEMY GRAPPLE
		if (InputHandler.LockOnHeld && InputHandler.GrappleTriggered && CurrentState != GrappleState)
		{
			Transform enemyTarget = FindEnemyToGrapple();
			if (enemyTarget != null)
			{
				GrappleState.SetTarget(enemyTarget);
				TransitionToState(GrappleState);
				return true;
			}
		}

		// NORMAL GRAPPLE
		if (!InputHandler.LockOnHeld && InputHandler.GrappleTriggered && CurrentState != GrappleState)
		{
			Transform grappleTarget = FindGrappleTarget();
			if (grappleTarget != null)
			{
				GrappleState.SetTarget(grappleTarget);
				TransitionToState(GrappleState);
				return true;
			}
		}

		// --- Y BUTTON (SPECIAL ATTACK) ---
		if (InputHandler.AOETriggered && CurrentState != AOEAttackState)
		{
			if (specialAttackY != null)
			{
				TransitionToState(AOEAttackState);
				return true;
			}
		}

		return false;
	}

	public Transform FindGrappleTarget()
	{
		if (stats == null) return null;

		Collider[] hits = Physics.OverlapSphere(transform.position, stats.grappleMaxRange, grappleLayer);
		Transform bestTarget = null;
		float bestScore = Mathf.Infinity;

		Vector3 inputDir = GetIsometricInputDirection();
		Vector3 searchDir = inputDir.sqrMagnitude > 0.01f ? inputDir.normalized : transform.forward;

		foreach (Collider hit in hits)
		{
			if (hit.transform == transform) continue;

			Vector3 toTarget = hit.transform.position - transform.position;
			float distance = toTarget.magnitude;

			if (distance < stats.grappleReleaseDistance) continue;

			Vector3 dirToTarget = toTarget.normalized;
			float angle = Vector3.Angle(searchDir, dirToTarget);

			if (angle < 90f)
			{
				float score = distance + (angle * 0.1f);

				if (score < bestScore)
				{
					bestScore = score;
					bestTarget = hit.transform;
				}
			}
		}
		return bestTarget;
	}

	public Transform FindEnemyToGrapple()
	{
		if (stats == null) return null;

		Collider[] hits = Physics.OverlapSphere(transform.position, stats.grappleMaxRange);
		Transform bestTarget = null;
		float bestScore = Mathf.Infinity;

		Vector3 inputDir = GetIsometricInputDirection();
		Vector3 searchDir = inputDir.sqrMagnitude > 0.01f ? inputDir.normalized : transform.forward;

		foreach (Collider hit in hits)
		{
			if (hit.transform == transform) continue;

			if (hit.GetComponent<IDamageable>() != null)
			{
				Vector3 toTarget = hit.transform.position - transform.position;
				float distance = toTarget.magnitude;

				if (distance < stats.grappleReleaseDistance) continue;

				Vector3 dirToTarget = toTarget.normalized;
				float angle = Vector3.Angle(searchDir, dirToTarget);

				if (angle < 35f)
				{
					float score = distance + (angle * 0.5f);

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

	private void UpdateGrappleReticle()
	{
		if (enemyGrappleReticle == null) return;

		if (InputHandler.LockOnHeld)
		{
			Transform target = FindEnemyToGrapple();
			if (target != null)
			{
				enemyGrappleReticle.SetActive(true);
				enemyGrappleReticle.transform.position = target.position + (Vector3.up * 1.5f);
				enemyGrappleReticle.transform.rotation = Camera.main.transform.rotation;
			}
			else
			{
				enemyGrappleReticle.SetActive(false);
			}
		}
		else
		{
			enemyGrappleReticle.SetActive(false);
		}
	}

	public void RefreshCombatStance()
	{
		if (stats != null) combatTimer = stats.combatStanceDuration;
		if (Animator != null) Animator.SetBool("IsInCombat", true);
	}

	private void UpdateCombatStance()
	{
		if (InputHandler.MoveInput.sqrMagnitude > 0.1f) combatTimer = 0f;

		if (combatTimer > 0f) combatTimer -= Time.deltaTime;

		if (combatTimer <= 0f && Animator != null) Animator.SetBool("IsInCombat", false);
	}

	public void TransitionToState(PlayerState newState)
	{
		CurrentState?.Exit();
		CurrentState = newState;
		CurrentState?.Enter();
	}

	public Vector3 GetIsometricInputDirection()
	{
		Vector2 rawInput = InputHandler.MoveInput;
		if (rawInput.sqrMagnitude < 0.01f) return Vector3.zero;

		Vector3 camForward = cameraTransform.forward;
		Vector3 camRight = cameraTransform.right;

		camForward.y = 0f;
		camRight.y = 0f;
		camForward.Normalize();
		camRight.Normalize();

		return (camForward * rawInput.y + camRight * rawInput.x).normalized;
	}

	public void RotateTowards(Vector3 direction)
	{
		if (direction.sqrMagnitude > 0.01f && stats != null)
		{
			Quaternion targetRotation = Quaternion.LookRotation(direction);
			transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, stats.rotationSpeed * Time.deltaTime);
		}
	}

	private void ApplyGravity()
	{
		if (stats == null) return;

		if (CharacterController.isGrounded && VerticalVelocity.y < 0)
		{
			VerticalVelocity.y = -2f;
		}
		else
		{
			VerticalVelocity.y += stats.gravity * Time.deltaTime;
		}
		CharacterController.Move(VerticalVelocity * Time.deltaTime);
	}

	private void OnDrawGizmosSelected()
	{
		if (stats == null) return;

		Gizmos.color = new Color(0f, 0.5f, 1f, 0.8f);
		Gizmos.DrawWireSphere(transform.position, stats.magnetismRadius);

		Gizmos.color = new Color(1f, 0f, 0f, 0.8f);
		Gizmos.DrawWireSphere(transform.position, stats.idealStrikeDistance);
	}
}