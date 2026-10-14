using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Hit feedback for when an ENEMY HITS THE PLAYER. It is deliberately different from the enemy hit effects
/// (amber energy + alien shards + distortion):  crimson / white-hot, with blood.
///
///   BODY FLASH      a crimson wound ripple over the player's body (reuses your VFX/HitGlow shader)
///   IMPACT BURST    white-hot flash + rays + shockwave ring at the hit point
///   BLOOD SPRAY     droplets + a little mist flying out the back of the player
///   SPARKS          a few hot sparks at the hit point
///   SPLATTER        ONE blood splatter on the ground behind the player (fades away after a while)
///   VIGNETTE        the screen edges flash red
///   CAMERA          small shake (along the hit direction) + a quick zoom punch
///   HIT STOP        a tiny freeze-frame
///
/// Setup: add this component to the Player (same object as PlayerController). Assign the materials
/// (or leave them empty - they are created from the shaders automatically).
/// PlayerController.TakeHit() calls PlayAuto(), which finds the nearest enemy to work out the hit direction.
/// For exact hit points call PlayHit(point, direction, intensity) from your PlayerHealth instead (see below).
/// </summary>
[DefaultExecutionOrder(1000)]
public class PlayerHitFX : MonoBehaviour
{
	public static PlayerHitFX Instance { get; private set; }

	[Header("Materials (optional - created from the shaders if empty)")]
	[Tooltip("Material using shader VFX/PlayerImpact")]
	public Material impactMaterial;
	[Tooltip("Material using shader VFX/PlayerParticle")]
	public Material particleMaterial;
	[Tooltip("Material using shader VFX/BloodDecal")]
	public Material bloodDecalMaterial;
	[Tooltip("Material using VFX/HitGlow (needs its noise texture). Leave empty to use the one on HitFeedbackManager.")]
	public Material bodyGlowMaterial;

	[Header("Switches")]
	public bool enableBodyFlash = true;
	public bool enableImpactBurst = true;
	public bool enableBloodSpray = true;
	public bool enableSparks = true;
	public bool enableBloodSplatter = true;
	public bool enableScreenVignette = true;
	public bool enableCameraShake = true;
	public bool enableZoomPunch = true;
	public bool enableHitStop = true;

	[Header("Colors")]
	[ColorUsage(true, true)] public Color impactCoreColor = new Color(3f, 2.2f, 1.8f, 1f);
	[ColorUsage(true, true)] public Color impactEdgeColor = new Color(2.4f, 0.12f, 0.1f, 1f);
	[ColorUsage(true, true)] public Color bodyHotColor = new Color(3f, 1.4f, 1.1f, 1f);
	[ColorUsage(true, true)] public Color bodyEdgeColor = new Color(2.2f, 0.12f, 0.1f, 1f);
	public Color bloodColor = new Color(0.5f, 0.02f, 0.04f, 1f);
	[Tooltip("Hot sparks")]
	[ColorUsage(true, true)] public Color sparkColor = new Color(3f, 1.4f, 0.5f, 1f);

	[Header("Body flash")]
	public float bodyFlashDuration = 0.3f;
	public float bodyFlashRadius = 1.6f;
	public float bodyFlashIntensity = 1.2f;

	[Header("Impact burst")]
	[Tooltip("Size of the burst at the hit point (world units).")]
	public float impactSize = 2f;
	public float impactDuration = 0.22f;
	[Tooltip("Moves the burst toward the camera so it is not buried inside the player.")]
	public float impactCameraOffset = 0.6f;

	[Header("Blood spray")]
	public int sprayCount = 22;
	public Vector2 spraySpeed = new Vector2(4f, 9f);
	public Vector2 spraySize = new Vector2(0.08f, 0.18f);
	public float sprayGravity = 2.2f;
	[Tooltip("How wide the spray spreads. 0 = straight out the back, 1 = every direction.")]
	[Range(0f, 1f)] public float spraySpread = 0.45f;
	public int mistCount = 4;
	[Tooltip("Droplets disappear when they touch these layers (set to your ground layer).")]
	public LayerMask sprayCollidesWith = ~0;

	[Header("Sparks")]
	public int sparkCount = 10;

	[Header("Blood splatter on the ground (behind the player)")]
	[Tooltip("How far behind the player the splatter lands (min, max).")]
	public Vector2 splatterDistance = new Vector2(1.2f, 1.9f);
	[Tooltip("Size of the splatter (min, max). It is scaled by hit strength.")]
	public Vector2 splatterSize = new Vector2(1.5f, 2.1f);
	[Tooltip("Seconds before it is fully gone.")]
	public float splatterLifetime = 8f;
	[Tooltip("Lifts the splatter off the ground. Raise it if tall grass hides it.")]
	public float splatterGroundOffset = 0.06f;
	public LayerMask splatterGroundMask = ~0;
	[Tooltip("How many splatters can exist at once. The oldest is reused.")]
	public int maxSplatters = 6;

	[Header("Screen vignette (red edges)")]
	public Color vignetteColor = new Color(0.85f, 0.02f, 0.04f, 1f);
	[Range(0f, 1f)] public float vignetteStrength = 0.5f;
	public float vignetteDuration = 0.55f;

	[Header("Camera (turn off if Cinemachine drives your camera)")]
	[Tooltip("How far the camera is shaken (world units).")]
	public float shakeAmount = 0.12f;
	public float shakeDuration = 0.25f;
	[Tooltip("Orthographic camera: how much the view zooms in at the peak.")]
	public float orthoPunch = 0.35f;
	[Tooltip("Perspective camera: degrees the field of view narrows at the peak.")]
	public float fovPunch = 2.5f;
	public float zoomDuration = 0.3f;

	[Header("Hit stop")]
	[Tooltip("Seconds of near-freeze.")]
	public float hitStopDuration = 0.06f;
	[Range(0f, 1f)] public float hitStopTimeScale = 0.05f;

	[Header("Auto detect (used by PlayAuto)")]
	public float autoDetectRadius = 6f;
	[Tooltip("Height of the player's chest above its position.")]
	public float chestHeight = 1.1f;
	[Tooltip("PlayAuto ignores calls that come sooner than this after another hit effect.")]
	public float minInterval = 0.08f;

	// ------------------------------------------------------------------ runtime
	private class Pulse
	{
		public Transform tf;
		public MeshRenderer mr;
		public float t0, dur, size, seed, roll;
		public bool active;
	}

	private class Decal
	{
		public Transform tf;
		public MeshRenderer mr;
		public float t0, life, size, seed;
		public Texture2D tex;
		public bool active;
	}

	private Transform runtimeRoot;
	private Mesh quadMesh;
	private MaterialPropertyBlock block;
	private float lastHitTime = -10f;

	private readonly List<Pulse> pulses = new List<Pulse>();
	private readonly List<Decal> decals = new List<Decal>();
	private Texture2D[] splatTextures;

	private ParticleSystem spray, mist, sparks;
	private Material sprayMat, mistMat, sparkMat;

	private RawImage vignetteImage;
	private float vignetteT0 = -10f;
	private float vignettePeak;

	private Camera cam;
	private Vector3 lastShakeOffset;
	private float shakeT0 = -10f, shakeAmp;
	private Vector3 shakeDir = Vector3.right;
	private float zoomT0 = -10f, zoomPeak, zoomApplied;

	private Coroutine hitStopRoutine;
	private float timeScaleBeforeStop = 1f;

	// ------------------------------------------------------------------ lifecycle
	private void Awake()
	{
		Instance = this;
		block = new MaterialPropertyBlock();
		quadMesh = BuildQuad();

		GameObject root = new GameObject("PlayerHitFX_Runtime");
		runtimeRoot = root.transform; // scene root on purpose: effects must not become children of the player

		BuildParticles();
		BuildVignette();
		if (enableBloodSplatter) splatTextures = new[] { BuildSplatTexture(128, 11), BuildSplatTexture(128, 23), BuildSplatTexture(128, 37) };
	}

	private void OnDisable()
	{
		ClearCameraEffects();
		if (hitStopRoutine != null)
		{
			StopCoroutine(hitStopRoutine);
			hitStopRoutine = null;
			Time.timeScale = timeScaleBeforeStop;
		}
	}

	private void OnDestroy()
	{
		if (Instance == this) Instance = null;
		if (sprayMat != null) Destroy(sprayMat);
		if (mistMat != null) Destroy(mistMat);
		if (sparkMat != null) Destroy(sparkMat);
		if (splatTextures != null) foreach (Texture2D t in splatTextures) if (t != null) Destroy(t);
		if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
	}

	// ------------------------------------------------------------------ public API
	/// <summary>
	/// Plays the full effect. hitDirection = the direction the blow travels (from the enemy toward the player).
	/// intensity 0..1 scales everything (use damage / maxDamage).
	/// </summary>
	public void PlayHit(Vector3 hitPoint, Vector3 hitDirection, float intensity = 1f)
	{
		lastHitTime = Time.unscaledTime;
		intensity = Mathf.Clamp01(intensity);

		hitDirection.y = 0f;
		if (hitDirection.sqrMagnitude < 0.0001f) hitDirection = -transform.forward;
		hitDirection.Normalize();

		if (enableBodyFlash) PlayBodyFlash(hitPoint, intensity);
		if (enableImpactBurst) SpawnImpact(hitPoint, intensity);
		if (enableBloodSpray) EmitSpray(hitPoint, hitDirection, intensity);
		if (enableSparks) EmitSparks(hitPoint, hitDirection, intensity);
		if (enableBloodSplatter) SpawnSplatter(hitDirection, intensity);
		if (enableScreenVignette) StartVignette(intensity);
		if (enableCameraShake) StartShake(hitDirection, intensity);
		if (enableZoomPunch) StartZoom(intensity);
		if (enableHitStop) StartHitStop();
	}

	/// <summary>Works out the hit direction by itself (nearest enemy). Used by PlayerController.TakeHit().</summary>
	public void PlayAuto(float intensity = 1f)
	{
		if (Time.unscaledTime - lastHitTime < minInterval) return;

		Transform nearest = null;
		float best = float.MaxValue;
		Collider[] cols = Physics.OverlapSphere(transform.position, autoDetectRadius, ~0, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < cols.Length; i++)
		{
			Collider c = cols[i];
			if (c.transform.IsChildOf(transform)) continue;
			IDamageable dmg = c.GetComponentInParent<IDamageable>();
			if (dmg == null) continue;

			Component comp = dmg as Component;
			Transform t = comp != null ? comp.transform : c.transform;
			if (t.IsChildOf(transform)) continue;

			Vector3 d = t.position - transform.position;
			d.y = 0f;
			float sq = d.sqrMagnitude;
			if (sq < 0.0001f) continue;
			if (sq < best) { best = sq; nearest = t; }
		}

		Vector3 dir;
		if (nearest != null)
		{
			dir = transform.position - nearest.position;
			dir.y = 0f;
		}
		else dir = -transform.forward;
		dir.Normalize();

		Vector3 point = transform.position + Vector3.up * chestHeight - dir * 0.25f; // the side facing the enemy
		PlayHit(point, dir, intensity);
	}

	// ------------------------------------------------------------------ update
	private void Update()
	{
		// take last frame's camera shake off again BEFORE the camera script moves the camera
		if (lastShakeOffset != Vector3.zero)
		{
			Camera c = GetCamera();
			if (c != null) c.transform.position -= lastShakeOffset;
			lastShakeOffset = Vector3.zero;
		}

		float now = Time.unscaledTime;

		for (int i = 0; i < pulses.Count; i++)
		{
			Pulse p = pulses[i];
			if (!p.active) continue;
			float progress = (now - p.t0) / Mathf.Max(p.dur, 0.01f);
			if (progress >= 1f)
			{
				p.active = false;
				p.tf.gameObject.SetActive(false);
				continue;
			}
			UpdatePulse(p, progress);
		}

		for (int i = 0; i < decals.Count; i++)
		{
			Decal d = decals[i];
			if (!d.active) continue;
			float age = now - d.t0;
			if (age >= d.life)
			{
				d.active = false;
				d.tf.gameObject.SetActive(false);
				continue;
			}
			UpdateDecal(d, age);
		}

		UpdateVignette(now);
	}

	private void LateUpdate()
	{
		Camera c = GetCamera();
		if (c == null) return;
		float now = Time.unscaledTime;

		// ---- shake (applied last, removed again at the start of the next frame)
		float st = (now - shakeT0) / Mathf.Max(shakeDuration, 0.01f);
		if (st >= 0f && st < 1f)
		{
			float decay = (1f - st) * (1f - st);
			float f = now * 38f;
			float nx = Mathf.PerlinNoise(f, 0.37f) * 2f - 1f;
			float ny = Mathf.PerlinNoise(0.91f, f) * 2f - 1f;
			float nz = Mathf.PerlinNoise(f, 7.3f) * 2f - 1f;

			Vector3 along = Vector3.ProjectOnPlane(shakeDir, c.transform.forward);
			along = along.sqrMagnitude > 0.0001f ? along.normalized : c.transform.right;

			Vector3 offset = (along * (0.75f * nx) + c.transform.right * (0.3f * ny) + c.transform.up * (0.3f * nz)) * (shakeAmp * decay);
			c.transform.position += offset;
			lastShakeOffset = offset;
		}

		// ---- zoom punch (applied as a change, so it works together with other camera scripts and DashVFX)
		float zt = (now - zoomT0) / Mathf.Max(zoomDuration, 0.01f);
		float zoomNow = 0f;
		if (zt >= 0f && zt < 1f) zoomNow = Mathf.Sin(Mathf.Sqrt(zt) * Mathf.PI) * (1f - zt) * zoomPeak;
		float delta = zoomNow - zoomApplied;
		if (Mathf.Abs(delta) > 0.00001f)
		{
			if (c.orthographic) c.orthographicSize -= delta * orthoPunch;
			else c.fieldOfView -= delta * fovPunch;
			zoomApplied = zoomNow;
		}
	}

	private Camera GetCamera()
	{
		if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
		return cam;
	}

	private void ClearCameraEffects()
	{
		Camera c = cam != null ? cam : Camera.main;
		if (c != null)
		{
			if (lastShakeOffset != Vector3.zero) c.transform.position -= lastShakeOffset;
			if (zoomApplied != 0f)
			{
				if (c.orthographic) c.orthographicSize += zoomApplied * orthoPunch;
				else c.fieldOfView += zoomApplied * fovPunch;
			}
		}
		lastShakeOffset = Vector3.zero;
		zoomApplied = 0f;
		shakeT0 = -10f;
		zoomT0 = -10f;
	}

	// ------------------------------------------------------------------ body flash
	private void PlayBodyFlash(Vector3 point, float intensity)
	{
		Material mat = bodyGlowMaterial;
		if (mat == null && HitFeedbackManager.Instance != null) mat = HitFeedbackManager.Instance.glowMaterial;
		if (mat == null)
		{
			Shader s = Shader.Find("VFX/HitGlow");
			if (s != null) { mat = new Material(s); bodyGlowMaterial = mat; }
		}
		if (mat == null) return;

		EnemyHitFX fx = GetComponent<EnemyHitFX>();
		if (fx == null) fx = gameObject.AddComponent<EnemyHitFX>();
		fx.PlayGlow(mat, point, bodyFlashIntensity * (0.7f + 0.6f * intensity), bodyFlashDuration,
					bodyFlashRadius * (0.8f + 0.4f * intensity), bodyHotColor, bodyEdgeColor);
	}

	// ------------------------------------------------------------------ impact burst
	private void SpawnImpact(Vector3 point, float intensity)
	{
		Material mat = impactMaterial;
		if (mat == null)
		{
			Shader s = Shader.Find("VFX/PlayerImpact");
			if (s == null) { Debug.LogError("[PlayerHitFX] Shader 'VFX/PlayerImpact' not found. Add VFX_PlayerImpact.shader to the project."); return; }
			mat = impactMaterial = new Material(s);
		}

		Camera c = GetCamera();
		if (c == null) return;

		Pulse p = null;
		for (int i = 0; i < pulses.Count; i++) if (!pulses[i].active) { p = pulses[i]; break; }
		if (p == null)
		{
			GameObject go = new GameObject("PlayerImpact");
			go.transform.SetParent(runtimeRoot, false);
			go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = mat;
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = LightProbeUsage.Off;
			mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
			p = new Pulse { tf = go.transform, mr = mr };
			pulses.Add(p);
		}

		p.active = true;
		p.size = impactSize * (0.85f + 0.3f * intensity);
		p.dur = impactDuration;
		p.t0 = Time.unscaledTime;           // real time, so it also plays during hit stop
		p.seed = Random.value;
		p.roll = Random.Range(0f, 360f);

		p.tf.position = point + (c.transform.position - point).normalized * impactCameraOffset;
		p.tf.localScale = Vector3.one * p.size;
		p.tf.gameObject.SetActive(true);
		UpdatePulse(p, 0f);
	}

	private void UpdatePulse(Pulse p, float progress)
	{
		Camera c = GetCamera();
		if (c != null)
			p.tf.rotation = Quaternion.AngleAxis(p.roll, c.transform.forward) * Quaternion.LookRotation(c.transform.forward, c.transform.up);

		p.mr.GetPropertyBlock(block);
		block.SetFloat("_Progress", progress);
		block.SetFloat("_Seed", p.seed);
		block.SetFloat("_Intensity", 1f);
		block.SetColor("_ColorCore", impactCoreColor);
		block.SetColor("_ColorEdge", impactEdgeColor);
		p.mr.SetPropertyBlock(block);
	}

	// ------------------------------------------------------------------ particles
	private void BuildParticles()
	{
		Shader shader = particleMaterial != null ? particleMaterial.shader : Shader.Find("VFX/PlayerParticle");
		if (shader == null)
		{
			Debug.LogError("[PlayerHitFX] Shader 'VFX/PlayerParticle' not found. Add VFX_PlayerParticle.shader to the project.");
			return;
		}

		// hard-edged dark blood droplets
		sprayMat = new Material(shader);
		sprayMat.SetFloat("_Additive", 0f);
		sprayMat.SetFloat("_Softness", 0.15f);
		sprayMat.SetFloat("_Intensity", 1f);

		// soft blood mist
		mistMat = new Material(shader);
		mistMat.SetFloat("_Additive", 0f);
		mistMat.SetFloat("_Softness", 1f);
		mistMat.SetFloat("_Intensity", 1f);

		// hot sparks (glow)
		sparkMat = new Material(shader);
		sparkMat.SetFloat("_Additive", 1f);
		sparkMat.SetFloat("_Softness", 1f);
		sparkMat.SetFloat("_Intensity", 1f);

		spray = MakeSystem("BloodSpray", sprayMat, ParticleSystemRenderMode.Stretch, 300);
		var sm = spray.main;
		sm.gravityModifier = sprayGravity;
		var sr = spray.GetComponent<ParticleSystemRenderer>();
		sr.lengthScale = 1.4f;
		sr.velocityScale = 0.05f;
		var ssz = spray.sizeOverLifetime;
		ssz.enabled = true;
		ssz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 1f), new Keyframe(1f, 0f)));
		var col = spray.collision;                       // droplets die when they touch the ground
		col.enabled = true;
		col.type = ParticleSystemCollisionType.World;
		col.mode = ParticleSystemCollisionMode.Collision3D;
		col.collidesWith = sprayCollidesWith;
		col.enableDynamicColliders = false;
		col.bounce = 0f;
		col.dampen = 1f;
		col.lifetimeLoss = 1f;
		col.radiusScale = 0.5f;
		col.quality = ParticleSystemCollisionQuality.Low;

		mist = MakeSystem("BloodMist", mistMat, ParticleSystemRenderMode.Billboard, 60);
		var mm = mist.main;
		mm.gravityModifier = -0.02f;
		var msz = mist.sizeOverLifetime;
		msz.enabled = true;
		msz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f)));
		FadeOverLife(mist);

		sparks = MakeSystem("HitSparks", sparkMat, ParticleSystemRenderMode.Stretch, 100);
		var spm = sparks.main;
		spm.gravityModifier = 1f;
		var spr = sparks.GetComponent<ParticleSystemRenderer>();
		spr.lengthScale = 1.2f;
		spr.velocityScale = 0.08f;
		var spsz = sparks.sizeOverLifetime;
		spsz.enabled = true;
		spsz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f)));
		FadeOverLife(sparks);

		spray.Play();
		mist.Play();
		sparks.Play();
	}

	private ParticleSystem MakeSystem(string name, Material mat, ParticleSystemRenderMode mode, int maxParticles)
	{
		GameObject go = new GameObject(name);
		go.transform.SetParent(runtimeRoot, false);

		ParticleSystem ps = go.AddComponent<ParticleSystem>();
		ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

		var main = ps.main;
		main.playOnAwake = false;
		main.loop = true;
		main.duration = 5f;
		main.simulationSpace = ParticleSystemSimulationSpace.World;
		main.startSpeed = 0f;
		main.maxParticles = maxParticles;

		var emission = ps.emission;
		emission.enabled = false;
		var shape = ps.shape;
		shape.enabled = false;

		ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
		r.sharedMaterial = mat;
		r.renderMode = mode;
		r.shadowCastingMode = ShadowCastingMode.Off;
		r.receiveShadows = false;
		return ps;
	}

	private static void FadeOverLife(ParticleSystem ps)
	{
		var c = ps.colorOverLifetime;
		c.enabled = true;
		Gradient g = new Gradient();
		g.SetKeys(
			new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
		c.color = g;
	}

	private void EmitSpray(Vector3 point, Vector3 away, float intensity)
	{
		if (spray == null) return;

		int count = Mathf.RoundToInt(sprayCount * (0.6f + 0.8f * intensity));
		ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
		for (int i = 0; i < count; i++)
		{
			// mostly out the back of the player, with an upward kick and a random spread
			Vector3 dir = away + Vector3.up * Random.Range(0.2f, 0.8f);
			dir = Vector3.Slerp(dir.normalized, Random.onUnitSphere, spraySpread * 0.6f).normalized;

			ep.position = point + Random.insideUnitSphere * 0.12f;
			ep.velocity = dir * Random.Range(spraySpeed.x, spraySpeed.y);
			ep.startLifetime = Random.Range(0.6f, 1.0f);
			ep.startSize = Random.Range(spraySize.x, spraySize.y);
			ep.startColor = Color.Lerp(bloodColor, bloodColor * 0.6f, Random.value);
			spray.Emit(ep, 1);
		}

		if (mist == null) return;
		ParticleSystem.EmitParams mp = new ParticleSystem.EmitParams();
		for (int i = 0; i < mistCount; i++)
		{
			mp.position = point + away * Random.Range(0f, 0.4f) + Random.insideUnitSphere * 0.15f;
			mp.velocity = away * Random.Range(0.6f, 1.8f) + Vector3.up * Random.Range(0.1f, 0.5f);
			mp.startLifetime = Random.Range(0.35f, 0.6f);
			mp.startSize = Random.Range(0.45f, 0.8f);
			mp.startColor = new Color(bloodColor.r, bloodColor.g, bloodColor.b, 0.35f);
			mist.Emit(mp, 1);
		}
	}

	private void EmitSparks(Vector3 point, Vector3 away, float intensity)
	{
		if (sparks == null) return;

		int count = Mathf.RoundToInt(sparkCount * (0.6f + 0.8f * intensity));
		ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
		for (int i = 0; i < count; i++)
		{
			Vector3 dir = Vector3.Slerp((away + Vector3.up * 0.3f).normalized, Random.onUnitSphere, 0.7f).normalized;
			ep.position = point + Random.insideUnitSphere * 0.08f;
			ep.velocity = dir * Random.Range(5f, 12f);
			ep.startLifetime = Random.Range(0.2f, 0.4f);
			ep.startSize = Random.Range(0.04f, 0.09f);
			ep.startColor = sparkColor;
			sparks.Emit(ep, 1);
		}
	}

	// ------------------------------------------------------------------ ground splatter (ONE per hit)
	private void SpawnSplatter(Vector3 away, float intensity)
	{
		Material mat = bloodDecalMaterial;
		if (mat == null)
		{
			Shader s = Shader.Find("VFX/BloodDecal");
			if (s == null) { Debug.LogError("[PlayerHitFX] Shader 'VFX/BloodDecal' not found. Add VFX_BloodDecal.shader to the project."); return; }
			mat = bloodDecalMaterial = new Material(s);
		}
		if (splatTextures == null || splatTextures.Length == 0) return;

		// where it lands: behind the player (away from the enemy), with a little sideways randomness
		Vector3 side = Vector3.Cross(Vector3.up, away);
		Vector3 probe = transform.position + away * Random.Range(splatterDistance.x, splatterDistance.y) + side * Random.Range(-0.25f, 0.25f);

		Vector3 point = new Vector3(probe.x, transform.position.y, probe.z);
		Vector3 normal = Vector3.up;
		RaycastHit hit;
		if (Physics.Raycast(probe + Vector3.up * 2f, Vector3.down, out hit, 6f, splatterGroundMask, QueryTriggerInteraction.Ignore)
			&& !hit.collider.transform.IsChildOf(transform)
			&& hit.collider.GetComponentInParent<IDamageable>() == null)
		{
			point = hit.point;
			normal = hit.normal;
		}
		point += normal * splatterGroundOffset;

		// pooled decal (the oldest is reused when the pool is full)
		Decal d = null;
		for (int i = 0; i < decals.Count; i++) if (!decals[i].active) { d = decals[i]; break; }
		if (d == null && decals.Count < maxSplatters)
		{
			GameObject go = new GameObject("BloodSplatter");
			go.transform.SetParent(runtimeRoot, false);
			go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = mat;
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
			mr.lightProbeUsage = LightProbeUsage.Off;
			mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
			d = new Decal { tf = go.transform, mr = mr };
			decals.Add(d);
		}
		if (d == null)
		{
			float oldest = float.MaxValue;
			for (int i = 0; i < decals.Count; i++) if (decals[i].t0 < oldest) { oldest = decals[i].t0; d = decals[i]; }
			if (d == null) return;
		}

		// orient flat on the ground, the long streaks pointing away from the enemy
		Vector3 up = Vector3.ProjectOnPlane(away, normal);
		if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(transform.forward, normal);
		Quaternion rot = Quaternion.LookRotation(-normal, up.normalized);
		rot = Quaternion.AngleAxis(Random.Range(-20f, 20f), normal) * rot;

		d.active = true;
		d.t0 = Time.unscaledTime;
		d.life = splatterLifetime;
		d.size = Random.Range(splatterSize.x, splatterSize.y) * (0.8f + 0.3f * intensity);
		d.seed = Random.value;
		d.tex = splatTextures[Random.Range(0, splatTextures.Length)];

		d.tf.SetPositionAndRotation(point, rot);
		d.tf.localScale = Vector3.one * d.size * 0.35f;
		d.tf.gameObject.SetActive(true);
		UpdateDecal(d, 0f);
	}

	private void UpdateDecal(Decal d, float age)
	{
		// quick splash outward, then it just sits there, dries, and fades in the last part of its life
		float grow = Mathf.Clamp01(age / 0.14f);
		grow = 1f - (1f - grow) * (1f - grow);
		d.tf.localScale = Vector3.one * d.size * Mathf.Lerp(0.35f, 1f, grow);

		float fadeStart = Mathf.Max(0.1f, d.life - 2.5f);
		float fade = age < fadeStart ? 1f : 1f - Mathf.Clamp01((age - fadeStart) / (d.life - fadeStart));
		float dry = Mathf.Clamp01(age / Mathf.Max(0.1f, d.life * 0.6f));

		d.mr.GetPropertyBlock(block);
		block.SetTexture("_MainTex", d.tex);
		block.SetFloat("_Fade", fade);
		block.SetFloat("_Dry", dry);
		d.mr.SetPropertyBlock(block);
	}

	// ------------------------------------------------------------------ screen vignette
	private void BuildVignette()
	{
		GameObject cgo = new GameObject("PlayerHitVignette");
		cgo.transform.SetParent(runtimeRoot, false);
		Canvas canvas = cgo.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 500;

		GameObject igo = new GameObject("Vignette");
		igo.transform.SetParent(cgo.transform, false);
		RawImage img = igo.AddComponent<RawImage>();
		img.texture = BuildVignetteTexture(128);
		img.raycastTarget = false;
		RectTransform rt = img.rectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
		img.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, 0f);
		img.enabled = false;
		vignetteImage = img;
	}

	private void StartVignette(float intensity)
	{
		if (vignetteImage == null) return;
		vignetteT0 = Time.unscaledTime;
		vignettePeak = vignetteStrength * (0.6f + 0.6f * intensity);
		vignetteImage.enabled = true;
		UpdateVignette(vignetteT0);
	}

	private void UpdateVignette(float now)
	{
		if (vignetteImage == null || !vignetteImage.enabled) return;
		float t = (now - vignetteT0) / Mathf.Max(vignetteDuration, 0.01f);
		if (t >= 1f)
		{
			vignetteImage.enabled = false;
			return;
		}
		float a = vignettePeak * (1f - t) * (1f - t);
		vignetteImage.color = new Color(vignetteColor.r, vignetteColor.g, vignetteColor.b, Mathf.Clamp01(a));
	}

	private static Texture2D BuildVignetteTexture(int size)
	{
		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		Color32[] px = new Color32[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float u = ((x + 0.5f) / size) * 2f - 1f;
				float v = ((y + 0.5f) / size) * 2f - 1f;
				float d = Mathf.Sqrt(u * u + v * v);
				float a = Mathf.Pow(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 1.15f, d)), 1.6f);
				px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
			}
		}
		tex.SetPixels32(px);
		tex.Apply(false, true);
		return tex;
	}

	// ------------------------------------------------------------------ camera + hit stop
	private void StartShake(Vector3 away, float intensity)
	{
		shakeT0 = Time.unscaledTime;
		shakeAmp = shakeAmount * (0.6f + 0.8f * intensity);
		shakeDir = away;
	}

	private void StartZoom(float intensity)
	{
		zoomT0 = Time.unscaledTime;
		zoomPeak = 0.6f + 0.6f * intensity;
	}

	private void StartHitStop()
	{
		if (hitStopDuration <= 0f || hitStopRoutine != null) return;
		if (Time.timeScale < 0.5f) return; // another slow-motion / hit stop is already running
		hitStopRoutine = StartCoroutine(HitStopRoutine());
	}

	private IEnumerator HitStopRoutine()
	{
		timeScaleBeforeStop = Time.timeScale;
		Time.timeScale = hitStopTimeScale;
		yield return new WaitForSecondsRealtime(hitStopDuration);
		if (Mathf.Approximately(Time.timeScale, hitStopTimeScale)) Time.timeScale = timeScaleBeforeStop;
		hitStopRoutine = null;
	}

	// ------------------------------------------------------------------ helpers
	private static Mesh BuildQuad()
	{
		Mesh m = new Mesh();
		m.name = "PlayerHitFXQuad";
		m.vertices = new[]
		{
			new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
			new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
		};
		m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
		m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
		m.RecalculateNormals();
		m.RecalculateBounds();
		return m;
	}

	/// <summary>
	/// Procedural blood splat: a small mass, spiky lobes, long streaks and scattered droplets.
	/// +Y of the texture is the splash direction. R = thickness, A = shape.
	/// </summary>
	private static Texture2D BuildSplatTexture(int size, int seed)
	{
		System.Random rng = new System.Random(seed);
		System.Func<float, float, float> R = (a, b) => a + (float)rng.NextDouble() * (b - a);

		List<Vector3> circles = new List<Vector3>(); // x, y, radius in -1..1 space

		circles.Add(new Vector3(0f, -0.10f, R(0.11f, 0.15f)));

		int lobes = rng.Next(8, 13);
		for (int i = 0; i < lobes; i++)
		{
			float ang = R(0f, Mathf.PI * 2f);
			float dist = R(0.08f, 0.22f);
			float stretch = 1f + Mathf.Max(0f, Mathf.Sin(ang)) * 1.1f;
			circles.Add(new Vector3(Mathf.Cos(ang) * dist * 0.9f, Mathf.Sin(ang) * dist * stretch - 0.10f, R(0.04f, 0.08f)));
		}

		int streaks = rng.Next(5, 9);
		for (int i = 0; i < streaks; i++)
		{
			float ang = R(-0.85f, 0.85f);
			float len = R(0.5f, 0.95f);
			float r0 = R(0.045f, 0.07f);
			const int steps = 26;
			for (int s = 0; s < steps; s++)
			{
				float t = s / (steps - 1f);
				float rad = r0 + (0.014f - r0) * Mathf.Pow(t, 0.7f);
				circles.Add(new Vector3(Mathf.Sin(ang) * len * t, Mathf.Cos(ang) * len * t - 0.10f, rad));
			}
		}

		int drops = rng.Next(14, 23);
		for (int i = 0; i < drops; i++)
		{
			float ang = rng.NextDouble() < 0.75 ? R(-1.1f, 1.1f) : R(0f, Mathf.PI * 2f);
			float dist = R(0.35f, 0.95f);
			circles.Add(new Vector3(Mathf.Sin(ang) * dist, Mathf.Cos(ang) * dist - 0.10f, R(0.012f, 0.035f)));
		}

		for (int i = 0; i < circles.Count; i++)
		{
			Vector3 c = circles[i];
			c.x = Mathf.Clamp(c.x, -0.97f, 0.97f);
			c.y = Mathf.Clamp(c.y, -0.97f, 0.97f);
			circles[i] = c;
		}

		Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
		tex.wrapMode = TextureWrapMode.Clamp;
		tex.filterMode = FilterMode.Bilinear;
		Color32[] px = new Color32[size * size];

		for (int y = 0; y < size; y++)
		{
			float v = ((y + 0.5f) / size) * 2f - 1f;
			for (int x = 0; x < size; x++)
			{
				float u = ((x + 0.5f) / size) * 2f - 1f;

				float f = 0f;
				for (int i = 0; i < circles.Count; i++)
				{
					Vector3 c = circles[i];
					float dx = u - c.x;
					float dy = v - c.y;
					float reach = 2.2f * c.z;
					float k = 1f - Mathf.Min((dx * dx + dy * dy) / (reach * reach), 1f);
					f += k * k;
				}

				float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.55f, f));
				float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
				mask *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1f, edge));
				float thick = Mathf.Clamp01((f - 0.5f) / 1.6f);

				px[y * size + x] = new Color32((byte)Mathf.RoundToInt(thick * 255f), 0, 0, (byte)Mathf.RoundToInt(mask * 255f));
			}
		}

		tex.SetPixels32(px);
		tex.Apply(false, true);
		return tex;
	}
}
