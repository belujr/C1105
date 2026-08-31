using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
	public Vector2 MoveInput { get; private set; }
	public bool SprintTriggered { get; private set; }
	public bool JumpTriggered { get; private set; }
	public bool DashTriggered { get; private set; }
	public bool SlideTriggered { get; private set; }
	public bool AttackTriggered { get; private set; }

	public bool AttackHeld { get; private set; }
	public bool PowerPunchHeld { get; private set; }

	// AOE Input
	public bool AOETriggered { get; private set; }

	// LT / Grapple Inputs
	public bool GrappleHeld { get; private set; }
	public bool GrappleTriggered { get; private set; }
	public bool GrappleReleased { get; private set; }
	public bool LockOnHeld { get; private set; }

	[Header("Input Action References")]
	[SerializeField] private InputActionReference moveAction;
	[SerializeField] private InputActionReference sprintAction;
	[SerializeField] private InputActionReference jumpAction;
	[SerializeField] private InputActionReference dashAction;
	[SerializeField] private InputActionReference slideAction;
	[SerializeField] private InputActionReference attackAction;
	[SerializeField] private InputActionReference grappleAction;
	[SerializeField] private InputActionReference powerPunchAction;
	[SerializeField] private InputActionReference lockOnAction;
	[SerializeField] private InputActionReference aoeAction;

	private void OnEnable()
	{
		moveAction.action?.Enable();
		sprintAction.action?.Enable();
		jumpAction.action?.Enable();
		dashAction.action?.Enable();
		slideAction.action?.Enable();
		attackAction.action?.Enable();
		grappleAction.action?.Enable();
		powerPunchAction.action?.Enable();
		lockOnAction.action?.Enable();
		aoeAction.action?.Enable();
	}

	private void OnDisable()
	{
		moveAction.action?.Disable();
		sprintAction.action?.Disable();
		jumpAction.action?.Disable();
		dashAction.action?.Disable();
		slideAction.action?.Disable();
		attackAction.action?.Disable();
		grappleAction.action?.Disable();
		powerPunchAction.action?.Disable();
		lockOnAction.action?.Disable();
		aoeAction.action?.Disable();
	}

	private void Update()
	{
		MoveInput = moveAction.action?.ReadValue<Vector2>() ?? Vector2.zero;
		SprintTriggered = sprintAction.action?.IsPressed() ?? false;

		JumpTriggered = jumpAction.action?.WasPressedThisFrame() ?? false;
		DashTriggered = dashAction.action?.WasPressedThisFrame() ?? false;
		SlideTriggered = slideAction.action?.WasPressedThisFrame() ?? false;
		AttackTriggered = attackAction.action?.WasPressedThisFrame() ?? false;
		AttackHeld = attackAction.action?.IsPressed() ?? false;
		PowerPunchHeld = powerPunchAction.action?.IsPressed() ?? false;

		AOETriggered = aoeAction.action?.WasPressedThisFrame() ?? false;

		GrappleHeld = grappleAction.action?.IsPressed() ?? false;
		GrappleTriggered = grappleAction.action?.WasPressedThisFrame() ?? false;
		GrappleReleased = grappleAction.action?.WasReleasedThisFrame() ?? false;
		LockOnHeld = lockOnAction.action?.IsPressed() ?? false;
	}
}