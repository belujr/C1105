using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Rigidbody))]
public class BeaconHealth : MonoBehaviour, IDamageable
{
    [Header("Shield & Quota Settings")]
    [SerializeField] private int requiredKillQuota = 20;
    [SerializeField] private float maxCoreHealth = 500f;

    [Header("Radar Settings")]
    [Tooltip("Size of the activation zone in WORLD units. The object's scale is compensated automatically.")]
    [SerializeField] private float radarRadius = 12f;
    [SerializeField] private float radarHeight = 2f;

    [Header("Core Damage Protection")]
    [Tooltip("Maximum distance from beacon center where impact damage is accepted (prevents AOE splash bleed from nearby enemies).")]
    [SerializeField] private float coreHitRadius = 3.5f;

    [Header("Events")]
    public UnityEvent OnBeaconActivated;
    public UnityEvent<int, int> OnQuotaUpdated;
    public UnityEvent OnShieldDropped;
    [Tooltip("Fired when something hits the beacon while its shield is still up. Passes the world hit point (ShieldHit uses it for the ripple).")]
    public UnityEvent<Vector3> OnShieldHit;
    public UnityEvent<float, float> OnCoreDamaged;
    public UnityEvent OnBeaconDestroyed;

    private int currentKills = 0;
    private float currentCoreHealth;
    private bool isShieldActive = true;
    private bool isActivated = false;
    private bool isDestroyed = false;

    public bool IsActivated => isActivated;
    public bool IsShieldActive => isShieldActive;
    public int RequiredKillQuota => requiredKillQuota;
    public int CurrentKills => currentKills;

    private CapsuleCollider radarTrigger;
    private Rigidbody rb;

    private void Awake()
    {
        // Setup Rigidbody so triggers fire reliably in Unity physics
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Setup the radar trigger collider
        radarTrigger = GetComponent<CapsuleCollider>();
        radarTrigger.isTrigger = true;

        // Radar size is given in WORLD units. A collider is multiplied by its object's scale,
        // so divide that out (otherwise a scaled-up shield object makes the radar far too big).
        Vector3 objScale = transform.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(objScale.x), Mathf.Abs(objScale.y), Mathf.Abs(objScale.z));
        if (scale < 0.0001f) scale = 1f;
        radarTrigger.radius = radarRadius / scale;
        radarTrigger.height = Mathf.Max(radarHeight / scale, radarTrigger.radius * 2f);
        radarTrigger.enabled = true;

        currentCoreHealth = maxCoreHealth;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isActivated || isDestroyed) return;

        // Checks if the player entered the radar trigger zone
        if (other.CompareTag("Player") || other.GetComponent<PlayerController>() != null)
        {
            ActivateBeacon();
        }
    }

    private void ActivateBeacon()
    {
        isActivated = true;
        OnBeaconActivated?.Invoke();
        OnQuotaUpdated?.Invoke(currentKills, requiredKillQuota);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal, float stunDuration = 1.5f, AudioClip hitSound = null, int attackID = -1, bool isAOE = false)
    {
        if (!isActivated || isDestroyed) return;

        if (isShieldActive)
        {
            OnShieldHit?.Invoke(hitPoint != Vector3.zero ? hitPoint : transform.position);
            return;
        }

        if (hitPoint != Vector3.zero && Vector3.Distance(transform.position, hitPoint) > coreHitRadius)
        {
            return;
        }

        currentCoreHealth -= damage;
        currentCoreHealth = Mathf.Max(0f, currentCoreHealth);
        OnCoreDamaged?.Invoke(currentCoreHealth, maxCoreHealth);

        if (currentCoreHealth <= 0f)
        {
            DestroyBeacon();
        }
    }

    public void RegisterEnemyKilled()
    {
        if (!isActivated || isDestroyed)
        {
            ActivateBeacon();
        }

        if (!isShieldActive || isDestroyed) return;

        currentKills++;
        currentKills = Mathf.Min(currentKills, requiredKillQuota);

        OnQuotaUpdated?.Invoke(currentKills, requiredKillQuota);

        if (currentKills >= requiredKillQuota)
        {
            DropShield();
        }
    }

    private void DropShield()
    {
        if (isDestroyed) return;
        isShieldActive = false;
        OnShieldDropped?.Invoke();
    }

    private void DestroyBeacon()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        isActivated = false;
        OnBeaconDestroyed?.Invoke();

        if (radarTrigger != null)
        {
            radarTrigger.enabled = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Cyan = activation zone (walls build when the player enters). Orange = where the core can be hit from.
        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radarRadius);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, coreHitRadius);
    }
}