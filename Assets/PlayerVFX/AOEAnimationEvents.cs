using UnityEngine;

/// <summary>
/// Receives the "AOEImpact" Animation Event from the player's attack animations and tells the AOE VFX
/// that the hit lands RIGHT NOW. This is what makes the VFX frame-exact with the animation.
///
/// You do not need to add this by hand: PlayerController adds it to the object that has the Animator
/// (Animation Events can only call methods on the same object as the Animator).
///
/// How to use: on every AOE attack animation, add an Animation Event on the exact frame where the
/// fist / foot / weapon hits the ground, and set its Function to:  AOEImpact
/// </summary>
public class AOEAnimationEvents : MonoBehaviour
{
	private PlayerController player;

	private void Awake()
	{
		player = GetComponentInParent<PlayerController>();
	}

	/// <summary>Animation Event target (no parameters).</summary>
	public void AOEImpact()
	{
		if (player == null) player = GetComponentInParent<PlayerController>();
		if (player != null) player.OnAOEImpactEvent();
	}
}
