using UnityEngine;
using System.Collections;

public class CombatEffectsManager : MonoBehaviour
{
	public static CombatEffectsManager Instance; // Singleton so any script can call it easily

	[Header("Audio References")]
	public AudioSource bgmSource;           // Your Background Music
	public AudioSource sfxSource;           // For the punch sounds
	public AudioLowPassFilter bgmFilter;    // The "muffler" effect

	[Header("Hit Stop Settings")]
	[Tooltip("How slow time gets (0 = full pause, 0.1 = super slow motion).")]
	public float timeScaleDuringHit = 0.05f;
	public float bgmPitchDuringHit = 0.6f;

	private void Awake()
	{
		// Simple Singleton pattern
		if (Instance == null) Instance = this;
		else Destroy(gameObject);
	}

	public void TriggerHitStop(float realTimeDuration, AudioClip punchSound)
	{
		StartCoroutine(HitStopRoutine(realTimeDuration, punchSound));
	}

	private IEnumerator HitStopRoutine(float duration, AudioClip punchSound)
	{
		// 1. Play the massive punch sound (Unity audio naturally ignores timeScale)
		if (punchSound != null && sfxSource != null)
		{
			sfxSource.PlayOneShot(punchSound);
		}

		// 2. The Vacuum: Drop music pitch and turn on the muffle filter
		if (bgmSource != null) bgmSource.pitch = bgmPitchDuringHit;
		if (bgmFilter != null) bgmFilter.enabled = true;

		// 3. Freeze / Slow down time
		Time.timeScale = timeScaleDuringHit;

		// 4. Wait using REAL time (since game time is currently frozen/slowed!)
		yield return new WaitForSecondsRealtime(duration);

		// 5. Restore time and audio clarity instantly
		Time.timeScale = 1f;
		if (bgmSource != null) bgmSource.pitch = 1f;
		if (bgmFilter != null) bgmFilter.enabled = false;
	}

	// Change your TriggerHitStop to this:
	public void TriggerHitEffects(float duration, float shakeIntensity, AudioClip punchSound)
	{
		StartCoroutine(HitStopRoutine(duration, punchSound));

		if (shakeIntensity > 0f)
		{
			StartCoroutine(CameraShakeRoutine(shakeIntensity, duration));
		}
	}

	// --- NEW: Camera Shake Coroutine ---
	private IEnumerator CameraShakeRoutine(float intensity, float duration)
	{
		Transform camTransform = Camera.main.transform;
		Vector3 originalPos = camTransform.localPosition;
		float elapsed = 0f;

		while (elapsed < duration)
		{
			// Pick a random offset based on intensity
			float x = Random.Range(-1f, 1f) * intensity;
			float y = Random.Range(-1f, 1f) * intensity;

			camTransform.localPosition = originalPos + new Vector3(x, y, 0);

			// Wait for real time since game time is frozen!
			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		camTransform.localPosition = originalPos;
	}
}