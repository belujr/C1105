using UnityEngine;

[CreateAssetMenu(fileName = "NewPlayerStats", menuName = "Player/Player Stats")]
public class PlayerStatsData : ScriptableObject
{
	[Header("Movement Settings")]
	public float walkSpeed = 4.5f;
	public float sprintSpeed = 8f;
	public float rotationSpeed = 15f;
	public float gravity = -20f;

	[Header("Jump Settings")]
	public float jumpHeight = 2f;
	public int maxJumps = 2;

	[Header("Dash Settings")]
	public float dashSpeed = 30f;
	public float dashDuration = 0.1f;
	public float dashCooldown = 1f;

	[Header("Slide Settings")]
	public float slideSpeed = 20f;
	public float slideDuration = 1f;
	public float slideCooldown = 1.5f;

	[Header("Grapple Settings (Snappy)")]
	public float grappleSpeed = 30f;
	public float grappleMaxRange = 25f;
	public float grappleReleaseDistance = 2.5f;
	public float grappleArcDip = 0.5f;

	[Header("Attack & Combat Settings")]
	public float comboResetWindow = 2f;
	public float combatStanceDuration = 1.5f;

	[Header("Combat Magnetism")]
	public float magnetismRadius = 3.5f;
	public float idealStrikeDistance = 0.5f;
}