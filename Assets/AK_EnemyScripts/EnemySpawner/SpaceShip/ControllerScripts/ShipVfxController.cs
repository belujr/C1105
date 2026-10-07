using UnityEngine;

public class ShipVFXController : MonoBehaviour
{
    [Header("Core References")]
    [SerializeField] private SpaceshipController shipController;
    [SerializeField] private Light engineGlow;

    [Header("Particle Systems")]
    [SerializeField] private ParticleSystem exhaustFireParticles;
    [SerializeField] private ParticleSystem exhaustSmokeParticles;
    [SerializeField] private ParticleSystem wingTrailParticles;
    [SerializeField] private ParticleSystem groundDustParticles;

    [Header("Thresholds")]
    [SerializeField] private float trailSpeedThreshold = 15f;
    [SerializeField] private float dustHeightThreshold = 12f;
    [SerializeField] private LayerMask groundMask = ~0;

    private ParticleSystem.EmissionModule fireEm, smokeEm, trailEm, dustEm;
    private ParticleSystem.MainModule fireMain, smokeMain;
    
    private float fireBaseRate, smokeBaseRate;
    private float fireBaseSize, smokeBaseSize;
    private float initialGlowIntensity;

    private void Awake()
    {
        if (engineGlow) initialGlowIntensity = engineGlow.intensity;

        if (exhaustFireParticles)
        {
            fireEm = exhaustFireParticles.emission;
            fireMain = exhaustFireParticles.main;
            fireBaseRate = fireEm.rateOverTimeMultiplier;
            fireBaseSize = fireMain.startSizeMultiplier;
            fireMain.useUnscaledTime = true;
        }

        if (exhaustSmokeParticles)
        {
            smokeEm = exhaustSmokeParticles.emission;
            smokeMain = exhaustSmokeParticles.main;
            smokeBaseRate = smokeEm.rateOverTimeMultiplier;
            smokeBaseSize = smokeMain.startSizeMultiplier;
            smokeMain.useUnscaledTime = true;
        }

        if (wingTrailParticles)
        {
            trailEm = wingTrailParticles.emission;
            var trailMain = wingTrailParticles.main;
            trailMain.useUnscaledTime = true;
        }

        if (groundDustParticles)
        {
            dustEm = groundDustParticles.emission;
            var dustMain = groundDustParticles.main;
            dustMain.useUnscaledTime = true;
        }
    }

    private void OnEnable()
    {
        if (exhaustFireParticles) exhaustFireParticles.Clear();
        if (exhaustSmokeParticles) exhaustSmokeParticles.Clear();
        if (wingTrailParticles) wingTrailParticles.Clear();
        if (groundDustParticles) groundDustParticles.Clear();
    }

    private void Update()
    {
        if (shipController == null) return;

        float thrust = Mathf.Max(0f, shipController.CurrentThrust);
        float speed = shipController.CurrentSpeed;
        bool isDroppingPhase = shipController.IsDroppingOrHovering; // Added check for drop phase

        // 1. Core Engine (Fire, Smoke, Light)
        if (engineGlow) engineGlow.intensity = initialGlowIntensity * thrust;
        
        if (exhaustFireParticles)
        {
            fireEm.rateOverTimeMultiplier = fireBaseRate * thrust;
            fireMain.startSizeMultiplier = fireBaseSize * (0.2f + thrust * 0.8f);
        }

        if (exhaustSmokeParticles)
        {
            smokeEm.rateOverTimeMultiplier = smokeBaseRate * thrust;
            smokeMain.startSizeMultiplier = smokeBaseSize * (0.5f + thrust * 0.5f);
        }

        // 2. Vapor Trails (Disabled during hover/drop, active during high-speed approach/exit)
        if (wingTrailParticles)
        {
            // By disabling emission instead of clearing, existing particles naturally fade out from back to front
            trailEm.enabled = !isDroppingPhase && (speed >= trailSpeedThreshold);
        }

        // 3. Ground Dust
        if (groundDustParticles)
        {
            bool isLowEnough = Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, dustHeightThreshold, groundMask, QueryTriggerInteraction.Ignore);
            
            if (isLowEnough && thrust > 0.1f)
            {
                groundDustParticles.transform.position = hit.point + Vector3.up * 0.2f;
                float heightIntensity = 1f - (hit.distance / dustHeightThreshold);
                dustEm.rateOverTimeMultiplier = 50f * thrust * heightIntensity;
                dustEm.enabled = true;
            }
            else
            {
                dustEm.enabled = false;
            }
        }
    }
}