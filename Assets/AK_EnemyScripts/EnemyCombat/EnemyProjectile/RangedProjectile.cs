using UnityEngine;

public class RangedProjectile : MonoBehaviour
{
    [Header("Projectile Settings")]
    public float speed = 12f;
    [Tooltip("How long the projectile homes in on the player before flying straight.")]
    public float homingDuration = 1.5f;
    [Tooltip("Maximum lifetime before the projectile automatically vanishes.")]
    public float maxLifetime = 5f;
    public int damage = 15;
    public float knockbackForce = 2f;
    public AudioClip hitSound;

    private Transform target;
    private float elapsed = 0f;
    private float homingElapsed = 0f;
    private bool hasHit = false;

    public void Initialize(Transform targetTransform)
    {
        target = targetTransform;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 moveDir = transform.forward;

        if (target != null && homingElapsed < homingDuration)
        {
            homingElapsed += Time.deltaTime;
            Vector3 dirToTarget = (target.position - transform.position).normalized;
            Quaternion targetRotation = Quaternion.LookRotation(dirToTarget);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 200f * Time.deltaTime);
            moveDir = transform.forward;
        }

        transform.position += moveDir * speed * Time.deltaTime;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;

        if (other.TryGetComponent<IDamageable>(out var damageable))
        {
            hasHit = true;
            Vector3 hitDir = transform.forward;
            damageable.TakeDamage(damage, transform.position, hitDir, knockbackForce, hitSound, 0, false);
            Destroy(gameObject);
        }
    }
}