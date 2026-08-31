using UnityEngine;

public class HitEffect : MonoBehaviour
{
	public float lifetime = 0.2f;
	private Camera mainCam;
	private float randomSpin;

	private void Start()
	{
		mainCam = Camera.main;

		// Pick a random angle for the impact star/spark to make it feel dynamic
		randomSpin = Random.Range(0f, 360f);

		Destroy(gameObject, lifetime);
	}

	private void LateUpdate()
	{
		if (mainCam != null)
		{
			// 1. Perfectly match the camera's exact tilt and angle
			transform.rotation = mainCam.transform.rotation;

			// 2. Apply the random 2D spin
			transform.Rotate(0, 0, randomSpin);
		}
	}
}