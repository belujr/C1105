using UnityEngine;

public class ImpFireball : MonoBehaviour
{
    private Transform target;
    private float damage;
    private float speed = 15f;
    private float lifeTimer = 5f;
    private Collider fireballCollider;
    // Give them default values so they are never empty on frame 1
    public string reactionAnimName = "Hit_Light";
    public float stunDuration = 0.2f;

    public void Initialize(Transform shooter, Transform target, float damage, string animName, float stun)
    {
        this.target = target;
        this.damage = damage;
        fireballCollider = GetComponent<Collider>();

        Collider shooterCollider = shooter.GetComponent<Collider>();
        if (fireballCollider != null && shooterCollider != null)
        {
            Physics.IgnoreCollision(fireballCollider, shooterCollider);
        }

        reactionAnimName = animName;
        stunDuration = stun;
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos = target != null ? target.position + Vector3.up * 1.0f : transform.position + transform.forward * 10f;
        Vector3 dir = (targetPos - transform.position).normalized;

        float moveDistance = speed * Time.deltaTime;

        if (Physics.Raycast(transform.position, dir, out RaycastHit hit, moveDistance + 0.1f))
        {
            if (hit.collider.CompareTag("Player") || hit.collider.GetComponent<PlayerController>() != null)
            {
                ApplyDamage(hit.collider);
            }
            Destroy(gameObject);
            return;
        }

        transform.position += dir * moveDistance;
        transform.rotation = Quaternion.LookRotation(dir);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") || other.GetComponent<PlayerController>() != null)
        {
            ApplyDamage(other);
            Destroy(gameObject);
        }
        else if (other.gameObject.layer != gameObject.layer)
        {
            Destroy(gameObject);
        }
    }

    private void ApplyDamage(Collider playerCollider)
    {
        if (playerCollider.TryGetComponent<IDamageable>(out var damageable))
        {
            Vector3 hitDir = (playerCollider.transform.position - transform.position).normalized;

            // SAFETY CHECK: If the string somehow got lost, force it to Hit_Light
            if (string.IsNullOrEmpty(reactionAnimName))
            {
                reactionAnimName = "Hit_Light";
            }

            // Convert the string to a hash
            int animHash = Animator.StringToHash(reactionAnimName);

            if (damageable is PlayerHealth ph)
            {
                ph.TakeDamage(damage, transform.position, hitDir, 1f, null, animHash, false, stunDuration);
            }
            else
            {
                damageable.TakeDamage(damage, transform.position, hitDir, 1f, null, animHash, false);
            }
        }
    }
}