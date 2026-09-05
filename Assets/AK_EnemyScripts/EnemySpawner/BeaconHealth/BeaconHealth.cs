using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(SphereCollider))]
public class BeaconHealth : MonoBehaviour, IDamageable
{
    [Header("Shield & Quota Settings")]
    [SerializeField] private int requiredKillQuota = 20;
    [SerializeField] private float maxCoreHealth = 500f;

    [Header("Radar Settings")]
    [SerializeField] private float radarRadius = 12f;

    [Header("Core Damage Protection")]
    [Tooltip("Maximum distance from beacon center where impact damage is accepted (prevents AOE splash bleed from nearby enemies).")]
    [SerializeField] private float coreHitRadius = 3.5f;

    [Header("Events")]
    public UnityEvent OnBeaconActivated;
    public UnityEvent<int, int> OnQuotaUpdated;
    public UnityEvent OnShieldDropped;
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

    private SphereCollider radarTrigger;

    private void Awake()
    {
        radarTrigger = GetComponent<SphereCollider>();
        radarTrigger.isTrigger = true;
        radarTrigger.radius = radarRadius;
        currentCoreHealth = maxCoreHealth;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isActivated || isDestroyed) return;

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

    public void TakeDamage(int damage, Vector3 hitPoint, Vector3 hitNormal, float stunDuration, AudioClip hitSound)
    {
        if (!isActivated || isDestroyed) return;

        if (isShieldActive)
        {
            return;
        }

        // Filter out collateral AOE damage originating from fighting nearby enemies
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
}