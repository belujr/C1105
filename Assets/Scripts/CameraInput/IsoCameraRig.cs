using UnityEngine;

public class IsoCameraRig : MonoBehaviour
{
	[SerializeField] private Transform target;
	[SerializeField] private Vector3 offset = new Vector3(-10f, 12f, -10f); // 45° iso offset
	[SerializeField] private float followSpeed = 8f;

	// --- NEW: Camera Shake Variables ---
	private float shakeTimer;
	private float shakeIntensity;

	// Call this from anywhere to shake the camera
	public void TriggerShake(float duration, float intensity)
	{
		shakeTimer = duration;
		shakeIntensity = intensity;
	}

	private void LateUpdate()
	{
		if (target == null) return;
		Vector3 desired = target.position + offset;

		// THE FIX: Use unscaledDeltaTime so the camera still moves smoothly even during Hit-Stop
		transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.unscaledDeltaTime);

		Vector3 lookPosition = target.position + Vector3.up * 1.5f;

		// --- THE SHAKE LOGIC ---
		if (shakeTimer > 0)
		{
			// Generate a random chaotic offset
			Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
			lookPosition += shakeOffset;

			// Decrease the timer using real-world time, ignoring the Hit-Stop freeze
			shakeTimer -= Time.unscaledDeltaTime;
		}

		transform.LookAt(lookPosition);
	}
}