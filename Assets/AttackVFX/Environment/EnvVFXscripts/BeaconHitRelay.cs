using UnityEngine;

/// Put this on the child object "BeaconHitbox" (the one with the SphereCollider on your enemy layer).
///
/// Why it exists: CombatHitboxController looks for IDamageable on the collider's OWN GameObject.
/// This relay receives the player's hit and forwards it to BeaconHealth.
[RequireComponent(typeof(Collider))]
public class BeaconHitRelay : MonoBehaviour, IDamageable
{
    [Tooltip("Leave empty to auto-find BeaconHealth on a parent.")]
    [SerializeField] private BeaconHealth beacon;

    [Tooltip("Play the player's hit VFX on the shield surface, where the fist struck, instead of at the beacon's center.")]
    public bool hitVfxAtContactPoint = true;

    private SphereCollider sphere;

    private void Awake()
    {
        if (beacon == null) beacon = GetComponentInParent<BeaconHealth>();
        sphere = GetComponent<SphereCollider>();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, float stunDuration = 1.5f, AudioClip hitSound = null, int attackID = -1, bool isAOE = false)
    {
        if (beacon != null)
            beacon.TakeDamage(damage, hitPoint, hitNormal, stunDuration, hitSound, attackID, isAOE);
    }

    /// Nearest point on the hitbox sphere surface in the direction of 'from'.
    /// Used so the hit VFX sits ON the shield even when the fist is inside the bubble.
    public Vector3 GetSurfacePoint(Vector3 from)
    {
        if (sphere == null) sphere = GetComponent<SphereCollider>();
        if (sphere == null) return from;

        Vector3 center = transform.TransformPoint(sphere.center);
        Vector3 s = transform.lossyScale;
        float radius = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));

        Vector3 dir = from - center;
        if (dir.sqrMagnitude < 0.0001f) return from;
        return center + dir.normalized * radius;
    }
}