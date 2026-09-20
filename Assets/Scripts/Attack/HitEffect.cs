using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class HitEffect : MonoBehaviour
{
	[Header("Spritesheet Animation")]
	[Tooltip("Drag your sliced sprite frames here in order.")]
	public Sprite[] animationFrames;
	public float framesPerSecond = 15f;

	[Header("Billboarding & Rotation")]
	[Tooltip("If true, perfectly faces the camera (best for hit sparks).")]
	public bool faceCamera = true;
	[Tooltip("If true, spins randomly on spawn so hits don't look repetitive.")]
	public bool randomizeZRotation = true;

	private SpriteRenderer spriteRenderer;
	private Camera mainCam;
	private float randomSpin;
	private float startTime;

	private void Start()
	{
		mainCam = Camera.main;
		spriteRenderer = GetComponent<SpriteRenderer>();
		startTime = Time.time;

		if (randomizeZRotation)
		{
			// Pick a random angle for the impact star/spark to make it feel dynamic
			randomSpin = Random.Range(0f, 360f);
		}
	}

	private void Update()
	{
		if (animationFrames == null || animationFrames.Length == 0) return;

		// 1. Calculate which frame to show based on time alive
		float timeAlive = Time.time - startTime;
		int currentFrameIndex = Mathf.FloorToInt(timeAlive * framesPerSecond);

		// 2. If animation is finished, destroy the GameObject automatically
		if (currentFrameIndex >= animationFrames.Length)
		{
			Destroy(gameObject);
			return;
		}

		// 3. Update the sprite
		spriteRenderer.sprite = animationFrames[currentFrameIndex];
	}

	private void LateUpdate()
	{
		if (mainCam != null && faceCamera)
		{
			// Perfectly match the camera's exact tilt and angle
			transform.rotation = mainCam.transform.rotation;

			if (randomizeZRotation)
			{
				// Apply the random 2D spin
				transform.Rotate(0, 0, randomSpin);
			}
		}
	}
}