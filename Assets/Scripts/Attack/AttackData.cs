using UnityEngine;

[CreateAssetMenu(fileName = "NewAttack", menuName = "Combat/Attack Data")]
public class AttackData : ScriptableObject
{
	[Header("UI & Preview Representation")]
	public string attackName = "Punch";
	[TextArea(3, 5)] public string description = "A quick jab that interrupts weak enemy attacks.";
	public Sprite attackIcon;

	[Header("Stat Levels (1 to 5)")]
    [Range(1, 5)] public int damageLevel = 3;
    [Range(1, 5)] public int knockbackLevel = 2;
    [Range(1, 5)] public int rangeLevel = 4;

	[Header("Identity")]
	[Tooltip("Unique ID used to match hit reactions or enemy custom dodges.")]
	public int attackID = 0; // <--- ADD THIS

	[Header("Animation & Lunge")]
	public string animationTriggerName = "Attack";
	public float animationDuration = 0.5f;
	public float forwardLungeSpeed = 2.0f;

	[Tooltip("How fast the character blends into this attack (e.g. 0.05 for snappy attacks).")]
	public float transitionDuration = 0.05f; // <--- ADD THIS

	[Tooltip("For Hold Attacks: At what second should the animation freeze? (e.g., 0.3)")]
	public float chargePauseTime = 0.3f; // <--- ADD THIS LINE

	[Header("Single Target Stats")]
	public int damage = 10;
	public float knockbackForce = 1.5f;
	public float strikeDistance = 1.2f;

	[Header("Area of Effect (Optional)")]
	public bool isAOE = false;
	public float aoeRadius = 6.0f;
	public float coneAngle = 120f;
	public int maxEnemiesHit = 4;
	[Tooltip("Push enemies into the air (e.g., Uppercuts)")]
	public float verticalLift = 0f;

	[Header("Juice & Feedback")]
	public float hitStopDuration = 0.05f;
	public float cameraShakeIntensity = 0.1f;
	public float cameraShakeDuration = 0.08f;
	public AudioClip customHitSound;
	public ParticleSystem customVFX;
}