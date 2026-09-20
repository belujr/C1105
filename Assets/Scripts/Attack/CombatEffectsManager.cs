using UnityEngine;
using System.Collections;

public class CombatEffectsManager : MonoBehaviour
{
	public static CombatEffectsManager Instance; 

	[Header("Audio References")]
	public AudioSource bgmSource;           
	public AudioSource sfxSource;           
	public AudioLowPassFilter bgmFilter;    

	[Header("Hit Stop Settings")]
	public float timeScaleDuringHit = 0.05f;
	public float bgmPitchDuringHit = 0.6f;

	private void Awake()
	{
		if (Instance == null) Instance = this;
		else Destroy(gameObject);
	}

	public void TriggerHitStop(float realTimeDuration, AudioClip punchSound)
	{
		StartCoroutine(HitStopRoutine(realTimeDuration, punchSound));
	}

	private IEnumerator HitStopRoutine(float duration, AudioClip punchSound)
	{
		if (punchSound != null && sfxSource != null)
		{
			sfxSource.PlayOneShot(punchSound);
		}

		if (bgmSource != null) bgmSource.pitch = bgmPitchDuringHit;
		if (bgmFilter != null) bgmFilter.enabled = true;

		Time.timeScale = timeScaleDuringHit;

		yield return new WaitForSecondsRealtime(duration);

		Time.timeScale = 1f;
		if (bgmSource != null) bgmSource.pitch = 1f;
		if (bgmFilter != null) bgmFilter.enabled = false;
	}

    // --- UPDATED SIGNATURE: Accepts GameObject ---
	public void TriggerHitEffects(float duration, float shakeIntensity, AudioClip punchSound, GameObject customVFX, Vector3 hitPosition)
	{
		StartCoroutine(HitStopRoutine(duration, punchSound));

		if (shakeIntensity > 0f)
		{
			StartCoroutine(CameraShakeRoutine(shakeIntensity, duration));
		}

		if (customVFX != null)
		{
            // Just instantiate the prefab; the HitEffect.cs script handles the rest
			Instantiate(customVFX, hitPosition, Quaternion.identity);
		}
	}

	private IEnumerator CameraShakeRoutine(float intensity, float duration)
	{
		Transform camTransform = Camera.main.transform;
		Vector3 originalPos = camTransform.localPosition;
		float elapsed = 0f;

		while (elapsed < duration)
		{
			float x = Random.Range(-1f, 1f) * intensity;
			float y = Random.Range(-1f, 1f) * intensity;

			camTransform.localPosition = originalPos + new Vector3(x, y, 0);

			elapsed += Time.unscaledDeltaTime;
			yield return null;
		}

		camTransform.localPosition = originalPos;
	}
}