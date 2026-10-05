using UnityEngine;

/// Put this on a child object that has a SphereCollider (the solid "ShieldBlocker" and the "BeaconHitbox").
/// When the shield drops, the sphere shrinks to the core size (or switches off if the radius is 0),
/// so the player can walk up to the core and hit it.
[RequireComponent(typeof(SphereCollider))]
public class BeaconShieldCollider : MonoBehaviour
{
    [Tooltip("Leave empty to auto-find BeaconHealth on a parent.")]
    [SerializeField] private BeaconHealth beacon;

    [Tooltip("Sphere radius after the shield drops, in LOCAL units (the beacon object is scaled up, so 0.1 can be ~0.5 world units). 0 = switch this collider off.")]
    [SerializeField] private float radiusAfterShieldDrops = 0.1f;

    private SphereCollider sphere;

    private void Awake()
    {
        sphere = GetComponent<SphereCollider>();
        if (beacon == null) beacon = GetComponentInParent<BeaconHealth>();
    }

    private void OnEnable()
    {
        if (beacon != null) beacon.OnShieldDropped.AddListener(OnShieldDropped);
    }

    private void OnDisable()
    {
        if (beacon != null) beacon.OnShieldDropped.RemoveListener(OnShieldDropped);
    }

    private void OnShieldDropped()
    {
        if (radiusAfterShieldDrops <= 0f) sphere.enabled = false;
        else sphere.radius = radiusAfterShieldDrops;
    }
}