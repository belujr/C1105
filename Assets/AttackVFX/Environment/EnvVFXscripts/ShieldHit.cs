using System.Collections;
using UnityEngine;

/// Put this on the beacon root. Plays a glowing ripple on the shield where it was hit.
public class ShieldHit : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If set, the ripple plays automatically whenever BeaconHealth fires OnShieldHit.")]
    [SerializeField] BeaconHealth beaconHealth;
    [Tooltip("Child sphere using the shield material with 'SphereMask' ticked. Scale it ~1.01x the shield to avoid z-fighting.")]
    [SerializeField] Renderer ripple;
    [Tooltip("Optional. Main shield renderer; the ripple copies its current front color (follows the green -> red quota color).")]
    [SerializeField] Renderer mainShield;

    [Header("Ripple")]
    [Tooltip("World-space radius the ripple grows to (try about half the shield radius).")]
    [SerializeField] float maxRadius = 2f;
    [SerializeField] float duration = 0.6f;
    [Tooltip("Snap the hit point onto the shield surface (recommended: melee hits often land inside the sphere).")]
    [SerializeField] bool projectOntoSurface = true;

    [Header("Look")]
    [SerializeField, ColorUsage(true, true)] Color hitColor = Color.white;
    [Tooltip("Pushes the color above 1 so Bloom makes it glow.")]
    [SerializeField] float hitBrightness = 3f;
    [SerializeField] bool useMainShieldColor = true;

    // Reference names from your Shader Graph (check the Graph Inspector if something doesn't react).
    static readonly int CenterId = Shader.PropertyToID("_SphereCenter");
    static readonly int RadiusId = Shader.PropertyToID("_SphereRadius");
    static readonly int FrontColorId = Shader.PropertyToID("_FronColor");

    Material rippleMat;
    MeshFilter rippleMesh;
    Coroutine routine;

    void Awake()
    {
        rippleMat = ripple.material;                 // instance
        rippleMesh = ripple.GetComponent<MeshFilter>();
        rippleMat.SetFloat(RadiusId, 0f);
        ripple.enabled = false;                      // invisible until a hit
    }

    void OnEnable()
    {
        if (beaconHealth != null) beaconHealth.OnShieldHit.AddListener(Hit);
    }

    void OnDisable()
    {
        if (beaconHealth != null) beaconHealth.OnShieldHit.RemoveListener(Hit);
    }

    public void Hit(Vector3 worldPoint)
    {
        if (projectOntoSurface && rippleMesh != null && rippleMesh.sharedMesh != null)
        {
            Vector3 center = ripple.transform.position;
            float radius = rippleMesh.sharedMesh.bounds.extents.x * ripple.transform.lossyScale.x;
            Vector3 dir = worldPoint - center;
            if (dir.sqrMagnitude > 0.0001f) worldPoint = center + dir.normalized * radius;
        }

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Ripple(worldPoint));
    }

    IEnumerator Ripple(Vector3 point)
    {
        Color c = hitColor;
        if (useMainShieldColor && mainShield != null && mainShield.material.HasProperty(FrontColorId))
            c = mainShield.material.GetColor(FrontColorId);
        c = new Color(c.r * hitBrightness, c.g * hitBrightness, c.b * hitBrightness, c.a);

        rippleMat.SetVector(CenterId, point);
        rippleMat.SetColor(FrontColorId, c);
        rippleMat.SetFloat(RadiusId, 0f);
        ripple.enabled = true;

        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float eased = 1f - (1f - t) * (1f - t);  // fast start, slow end
            rippleMat.SetFloat(RadiusId, Mathf.Lerp(0f, maxRadius, eased));
            yield return null;
        }

        ripple.enabled = false;
        routine = null;
    }
}