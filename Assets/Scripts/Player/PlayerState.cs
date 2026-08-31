
using UnityEngine;

public abstract class PlayerState
{
    protected PlayerController controller;
    protected PlayerInputHandler input;

    public float CooldownTimer { get; protected set; }
    public float MaxCooldown { get; protected set; }

	public virtual bool CanBeInterrupted => true;
	public PlayerState(PlayerController controller, PlayerInputHandler input)
    {
        this.controller = controller;
        this.input = input;
    }

    public virtual void Enter() { }
    public virtual void LogicUpdate() { } // Called in Update()
    public virtual void PhysicsUpdate() { } // Called in FixedUpdate()
    public virtual void Exit() { }

    public bool IsOnCooldown() => CooldownTimer > 0f;

    public void UpdateCooldown(float deltaTime)
    {
        if (CooldownTimer > 0f)
        {
            CooldownTimer -= deltaTime;
        }
    }
}