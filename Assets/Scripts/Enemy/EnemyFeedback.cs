using UnityEngine;
using System.Collections;

public class EnemyFeedback : MonoBehaviour
{
	[Header("Hit Feedback")]
	public Color hitColor = Color.red;
	[Tooltip("Scales how far this specific enemy is pushed back when hit.")]
	public float knockbackDistanceMultiplier = 1.0f;
	public float knockbackDuration = 0.2f;

	[Header("Knockdown Settings")]
	public float deathGroundOffset = 0.5f;

	[Header("Visual Effects")]
	public GameObject hitSparkPrefab;

	[Header("Audio Effects")]
	public AudioClip defaultHeavyPunchSound;

	private Quaternion baseRotation;
	private Color originalColor;
	private MeshRenderer meshRenderer;
	private Coroutine activeReaction;

	private void Start()
	{
		baseRotation = transform.rotation;
		meshRenderer = GetComponentInChildren<MeshRenderer>();
		if (meshRenderer != null) originalColor = meshRenderer.material.color;
	}

	public void PlayHitReaction(Vector3 hitPoint, Vector3 hitDirection, float knockbackForce, AudioClip hitSound)
	{
		if (hitSparkPrefab != null) Instantiate(hitSparkPrefab, hitPoint, Quaternion.identity);

		if (CombatEffectsManager.Instance != null)
		{
			AudioClip soundToPlay = hitSound != null ? hitSound : defaultHeavyPunchSound;
			CombatEffectsManager.Instance.TriggerHitEffects(0.08f, 0.1f, soundToPlay);
		}

		if (activeReaction != null) StopCoroutine(activeReaction);

		float calculatedForce = knockbackForce * knockbackDistanceMultiplier;

		bool isUppercut = hitDirection.y > 0.1f;
		if (isUppercut) activeReaction = StartCoroutine(UppercutReactionRoutine(hitDirection, calculatedForce));
		else activeReaction = StartCoroutine(HitReactionRoutine(hitDirection, calculatedForce));
	}

	public void PlayDeathReaction(Vector3 hitDirection, float knockbackForce)
	{
		if (activeReaction != null) StopCoroutine(activeReaction);

		float calculatedForce = knockbackForce * knockbackDistanceMultiplier;

		bool isUppercut = hitDirection.y > 0.1f;
		if (isUppercut) activeReaction = StartCoroutine(UppercutKnockdownRoutine(hitDirection, calculatedForce));
		else activeReaction = StartCoroutine(KnockdownRoutine(hitDirection, calculatedForce));
	}

	private IEnumerator HitReactionRoutine(Vector3 hitDirection, float force)
	{
		if (meshRenderer != null) meshRenderer.material.color = hitColor;

		Vector3 startPosition = transform.position;
		Vector3 targetPosition = startPosition + (hitDirection * force);

		float dynamicTiltAngle = Mathf.Clamp(20f * force, 30f, 85f);
		Vector3 tiltAxis = Vector3.Cross(Vector3.up, hitDirection);
		Quaternion maxTiltRotation = Quaternion.AngleAxis(dynamicTiltAngle, tiltAxis) * baseRotation;

		float actualDuration = Mathf.Max(knockbackDuration, force * 0.08f);
		float elapsedTime = 0f;

		while (elapsedTime < actualDuration)
		{
			elapsedTime += Time.unscaledDeltaTime;
			float t = elapsedTime / actualDuration;
			float easeOutT = 1f - Mathf.Pow(1f - t, 3f);

			transform.position = Vector3.Lerp(startPosition, targetPosition, easeOutT);
			transform.rotation = Quaternion.Slerp(maxTiltRotation, baseRotation, easeOutT);
			yield return null;
		}

		transform.position = targetPosition;
		transform.rotation = baseRotation;
		if (meshRenderer != null) meshRenderer.material.color = originalColor;
	}

	private IEnumerator KnockdownRoutine(Vector3 hitDirection, float force)
	{
		if (meshRenderer != null) meshRenderer.material.color = Color.white;
		Collider col = GetComponent<Collider>();
		if (col != null) col.enabled = false;

		Vector3 startPos = transform.position;
		Vector3 targetPos = startPos + (hitDirection * (force * 1.5f));
		targetPos.y -= deathGroundOffset;

		Vector3 fallAxis = Vector3.Cross(Vector3.up, hitDirection);
		Quaternion flatRotation = Quaternion.AngleAxis(90f, fallAxis) * baseRotation;

		float fallDuration = 0.4f;
		float elapsed = 0f;

		while (elapsed < fallDuration)
		{
			elapsed += Time.unscaledDeltaTime;
			float t = elapsed / fallDuration;
			float rotT = t * t;
			transform.rotation = Quaternion.Slerp(baseRotation, flatRotation, rotT);

			float height = 1.0f;
			float arcY = 4f * height * t * (1f - t);

			Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
			currentPos.y += arcY;
			transform.position = currentPos;
			yield return null;
		}

		transform.position = targetPos;
		transform.rotation = flatRotation;

		float bounceDuration = 0.15f;
		float bounceElapsed = 0f;
		Quaternion bounceUpRot = Quaternion.AngleAxis(75f, fallAxis) * baseRotation;

		while (bounceElapsed < bounceDuration)
		{
			bounceElapsed += Time.unscaledDeltaTime;
			float t = bounceElapsed / bounceDuration;
			float bounceT = Mathf.Sin(t * Mathf.PI);
			transform.rotation = Quaternion.Slerp(flatRotation, bounceUpRot, bounceT);
			yield return null;
		}
		transform.rotation = flatRotation;
	}

	private IEnumerator UppercutReactionRoutine(Vector3 hitDirection, float force)
	{
		if (meshRenderer != null) meshRenderer.material.color = hitColor;

		Vector3 startPosition = transform.position;
		Vector3 flatDirection = new Vector3(hitDirection.x, 0f, hitDirection.z);
		Vector3 targetPosition = startPosition + (flatDirection * force);

		float jumpHeight = Mathf.Max(hitDirection.y * force * 0.4f, 0f);
		float actualDuration = Mathf.Max(knockbackDuration, 0.2f + (jumpHeight * 0.12f));

		Vector3 tiltAxis = Vector3.Cross(Vector3.up, flatDirection.normalized);
		float dynamicTiltAngle = Mathf.Clamp(20f * force, 30f, 85f);
		Quaternion maxTiltRotation = Quaternion.AngleAxis(dynamicTiltAngle, tiltAxis) * baseRotation;

		float elapsedTime = 0f;

		while (elapsedTime < actualDuration)
		{
			elapsedTime += Time.unscaledDeltaTime;
			float t = elapsedTime / actualDuration;

			float posT = 1f - Mathf.Pow(1f - t, 3f);
			float rotT = t * t;

			Vector3 currentPos = Vector3.Lerp(startPosition, targetPosition, posT);
			float arcY = 4f * jumpHeight * t * (1f - t);
			currentPos.y += arcY;

			transform.position = currentPos;
			transform.rotation = Quaternion.Slerp(baseRotation, maxTiltRotation, rotT);
			yield return null;
		}

		transform.position = targetPosition;
		transform.rotation = maxTiltRotation;

		float groundDelay = 0.3f;
		float delayElapsed = 0f;
		while (delayElapsed < groundDelay)
		{
			delayElapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		float recoveryDuration = 0.5f;
		float recoveryElapsed = 0f;
		while (recoveryElapsed < recoveryDuration)
		{
			recoveryElapsed += Time.unscaledDeltaTime;
			float t = recoveryElapsed / recoveryDuration;
			float standT = Mathf.SmoothStep(0f, 1f, t);
			transform.rotation = Quaternion.Slerp(maxTiltRotation, baseRotation, standT);
			yield return null;
		}

		transform.rotation = baseRotation;
		if (meshRenderer != null) meshRenderer.material.color = originalColor;
	}

	private IEnumerator UppercutKnockdownRoutine(Vector3 hitDirection, float force)
	{
		if (meshRenderer != null) meshRenderer.material.color = Color.white;
		Collider col = GetComponent<Collider>();
		if (col != null) col.enabled = false;

		Vector3 startPos = transform.position;
		Vector3 flatDirection = new Vector3(hitDirection.x, 0f, hitDirection.z);
		Vector3 targetPos = startPos + (flatDirection * (force * 1.5f));
		targetPos.y -= deathGroundOffset;

		float jumpHeight = Mathf.Max(hitDirection.y * force * 0.5f, 1.0f);
		float fallDuration = Mathf.Max(0.4f, 0.2f + (jumpHeight * 0.12f));

		Vector3 fallAxis = Vector3.Cross(Vector3.up, flatDirection.normalized);
		Quaternion flatRotation = Quaternion.AngleAxis(90f, fallAxis) * baseRotation;

		float elapsed = 0f;

		while (elapsed < fallDuration)
		{
			elapsed += Time.unscaledDeltaTime;
			float t = elapsed / fallDuration;
			float rotT = t * t;
			transform.rotation = Quaternion.Slerp(baseRotation, flatRotation, rotT);

			float arcY = 4f * jumpHeight * t * (1f - t);
			Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
			currentPos.y += arcY;
			transform.position = currentPos;
			yield return null;
		}

		transform.position = targetPos;
		transform.rotation = flatRotation;
		if (meshRenderer != null) meshRenderer.material.color = originalColor;

		float bounceDuration = 0.15f;
		float bounceElapsed = 0f;
		Quaternion bounceUpRot = Quaternion.AngleAxis(75f, fallAxis) * baseRotation;

		while (bounceElapsed < bounceDuration)
		{
			bounceElapsed += Time.unscaledDeltaTime;
			float t = bounceElapsed / bounceDuration;
			float bounceT = Mathf.Sin(t * Mathf.PI);
			transform.rotation = Quaternion.Slerp(flatRotation, bounceUpRot, bounceT);
			yield return null;
		}
		transform.rotation = flatRotation;
	}
}