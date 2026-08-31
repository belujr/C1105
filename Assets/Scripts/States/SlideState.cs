using UnityEngine;

public class SlideState : PlayerState
{
    private float slideTimer;
    private Vector3 slideDirection;

    public SlideState(PlayerController controller, PlayerInputHandler input) : base(controller, input)
    {
        MaxCooldown = controller.stats.slideCooldown;
    }

    public override void Enter()
    {
        base.Enter();
        slideTimer = controller.stats.slideDuration;
        CooldownTimer = controller.stats.slideCooldown;

        Vector3 inputDir = controller.GetIsometricInputDirection();
        slideDirection = inputDir.sqrMagnitude > 0.01f ? inputDir : controller.transform.forward;
        controller.transform.rotation = Quaternion.LookRotation(slideDirection);

        if (controller.Animator != null)
        {
            controller.Animator.SetTrigger("Slide");
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        slideTimer -= Time.deltaTime;

        float speedMultiplier = slideTimer / controller.stats.slideDuration;
        float currentSpeed = Mathf.Lerp(controller.stats.walkSpeed, controller.stats.slideSpeed, speedMultiplier);

        controller.CharacterController.Move(slideDirection * currentSpeed * Time.deltaTime);

        if (slideTimer <= 0f)
        {
            controller.TransitionToState(controller.GroundedState);
        }
    }
}