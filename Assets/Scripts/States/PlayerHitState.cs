using UnityEngine;

public class PlayerHitState : PlayerState
{
    private float hitTimer;
    private int animHash;

    // This guarantees taking damage cancels whatever else you were doing!
    public override bool CanBeInterrupted => false;

    public PlayerHitState(PlayerController controller, PlayerInputHandler input) : base(controller, input) { }

    public void SetUp(int animationHash, float duration)
    {
        animHash = animationHash;
        hitTimer = duration;
    }

    public override void Enter()
    {
        base.Enter();

        // Kill forward momentum so you don't slide while flinching
        controller.VerticalVelocity = new Vector3(0f, controller.VerticalVelocity.y, 0f);

        if (controller.Animator != null)
        {
            controller.Animator.speed = 1f;
            controller.Animator.SetFloat("Speed", 0f); // Force walking/running to stop!

            // Check if the animation state exists before playing it
            if (controller.Animator.HasState(0, animHash))
            {
                controller.Animator.CrossFade(animHash, 0.05f);
            }
            else
            {
                Debug.LogWarning($"[PlayerHitState] Animator is missing the hit reaction state!");
            }
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        hitTimer -= Time.deltaTime;

        if (hitTimer <= 0f)
        {
            // Stun is over. Return control to the player!
            controller.TransitionToState(controller.GroundedState);
        }
    }
}