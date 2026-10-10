using System.Collections.Generic;
using UnityEngine;

public class IsoCameraRig : MonoBehaviour
{
    public static IsoCameraRig Instance { get; private set; }

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(-10f, 12f, -10f); // 45° iso offset
    [SerializeField] private float followSpeed = 8f;

    [Header("Cinematic POI")]
    [Tooltip("How strongly the camera pulls towards active ships when NO combat is happening.")]
    [SerializeField] private float poiPullWeight = 0.3f;
    [SerializeField] private float poiZoomMultiplier = 1.35f;
    
    [Tooltip("How strongly the camera pulls towards ships mid-combat (keep low to favor player).")]
    [SerializeField] private float combatPoiPullWeight = 0.15f; 
    [Tooltip("How much the camera zooms out mid-combat to fit both player and ship.")]
    [SerializeField] private float combatZoomMultiplier = 1.6f;
    
    [SerializeField] private float poiTransitionSpeed = 2f;

    public bool IsMidCombat { get; set; } = false;
    public Transform Target => target;

    private float shakeTimer;
    private float shakeIntensity;
    
    private List<Transform> pointsOfInterest = new List<Transform>();
    private float currentZoom = 1f;
    private float currentPoiWeight = 0f;
    private Vector3 lastKnownPoiCenter;

    private void Awake()
    {
        Instance = this;
    }

    public void TriggerShake(float duration, float intensity)
    {
        shakeTimer = duration;
        shakeIntensity = intensity;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void RegisterPOI(Transform poi)
    {
        if (!pointsOfInterest.Contains(poi)) pointsOfInterest.Add(poi);
    }

    public void UnregisterPOI(Transform poi)
    {
        pointsOfInterest.Remove(poi);
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Clean up inactive/destroyed POIs
        pointsOfInterest.RemoveAll(p => p == null || !p.gameObject.activeInHierarchy);
        
        float targetPoiWeight = 0f;
        float targetZoom = 1f;

        if (pointsOfInterest.Count > 0)
        {
            targetPoiWeight = IsMidCombat ? combatPoiPullWeight : poiPullWeight;
            targetZoom = IsMidCombat ? combatZoomMultiplier : poiZoomMultiplier;
        }

        // Smoothly ease the pan weight and zoom so it feels cinematic
        currentPoiWeight = Mathf.Lerp(currentPoiWeight, targetPoiWeight, poiTransitionSpeed * Time.unscaledDeltaTime);
        currentZoom = Mathf.Lerp(currentZoom, targetZoom, poiTransitionSpeed * Time.unscaledDeltaTime);

        Vector3 baseTargetPos = target.position;

        if (pointsOfInterest.Count > 0)
        {
            Vector3 poiCenter = Vector3.zero;
            foreach (var p in pointsOfInterest) poiCenter += p.position;
            poiCenter /= pointsOfInterest.Count;
            
            lastKnownPoiCenter = poiCenter;
        }

        // Apply the weight using the cached center to prevent snapping on exit
        if (currentPoiWeight > 0.001f)
        {
            baseTargetPos = Vector3.Lerp(target.position, lastKnownPoiCenter, currentPoiWeight);
        }

        Vector3 desired = baseTargetPos + (offset * currentZoom);
        transform.position = Vector3.Lerp(transform.position, desired, followSpeed * Time.unscaledDeltaTime);

        Vector3 lookPosition = baseTargetPos + Vector3.up * 1.5f;

        if (shakeTimer > 0)
        {
            Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
            lookPosition += shakeOffset;
            shakeTimer -= Time.unscaledDeltaTime;
        }

        transform.LookAt(lookPosition);
    }
}