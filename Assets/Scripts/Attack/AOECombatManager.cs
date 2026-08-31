using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class AOECombatManager : MonoBehaviour
{
	// =========================================================
	// This script now acts ONLY as a visualizer for the Scene view
	// to draw your AOE cones until you replace them with VFX!
	// =========================================================

	private void OnDrawGizmosSelected()
	{
		PlayerController player = GetComponent<PlayerController>();
		if (player == null) return;

		// 1. Draw the Y-Button Special Attack (Cyan)
		if (player.specialAttackY != null && player.specialAttackY.isAOE)
		{
			Gizmos.color = new Color(0f, 1f, 1f, 0.4f); // Cyan
			DrawAOEGizmo(player.specialAttackY);
		}

		// 2. Draw the RT Heavy Attack (Orange)
		if (player.equippedStyle != null)
		{
			AttackData heavyAttack = player.equippedStyle.GetActiveChargeAttack();
			if (heavyAttack != null && heavyAttack.isAOE)
			{
				Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f); // Orange
				DrawAOEGizmo(heavyAttack);
			}
		}
	}

	private void DrawAOEGizmo(AttackData attackData)
	{
		// Draw the outer radius
		Gizmos.DrawWireSphere(transform.position, attackData.aoeRadius);

		// Calculate the left and right edges of the cone
		Vector3 rightEdge = Quaternion.Euler(0, attackData.coneAngle / 2f, 0) * transform.forward;
		Vector3 leftEdge = Quaternion.Euler(0, -attackData.coneAngle / 2f, 0) * transform.forward;

		// Draw the cone lines
		Gizmos.DrawRay(transform.position, rightEdge * attackData.aoeRadius);
		Gizmos.DrawRay(transform.position, leftEdge * attackData.aoeRadius);
	}
}