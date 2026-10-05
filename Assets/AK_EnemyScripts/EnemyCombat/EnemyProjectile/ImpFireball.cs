using UnityEngine;

public class ImpFireball : MonoBehaviour
{
    private Transform target;
    private float damage;
    private float speed = 16f;
    private float lifeTimer = 5f;
    private Collider fireballCollider;
    private LineRenderer lineRenderer;

    [Header("Homing & Arc Tracking Settings")]
    public float trackingDuration = 1.5f;
    private float trackingTimer;
    private Vector3 currentVelocity;

    [Header("Trail Settings")]
    [Tooltip("How far behind the bullet the trail stretches.")]
    public float trailLength = 0.8f;

    public string reactionAnimName = "Hit_Light";
    public float stunDuration = 0.2f;

    public void Initialize(Transform shooter, Transform target, float damage, string animName, float stun)
    {
        this.target = target;
        this.damage = damage;
        trackingTimer = trackingDuration;
        fireballCollider = GetComponent<Collider>();
        lineRenderer = GetComponent<LineRenderer>();

        // Setup Line Renderer defaults if attached
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.useWorldSpace = true;
        }

        // Ignore shooter's colliders to prevent self-destruction on spawn
        if (fireballCollider != null)
        {
            Collider[] shooterColliders = shooter.GetComponentsInChildren<Collider>();
            foreach (var col in shooterColliders)
            {
                Physics.IgnoreCollision(fireballCollider, col);
            }
        }

        reactionAnimName = animName;
        stunDuration = stun;

        Vector3 targetPos = target != null ? target.position + Vector3.up * 1.0f : transform.position + transform.forward * 10f;
        currentVelocity = (targetPos - transform.position).normalized * speed;
        transform.rotation = Quaternion.LookRotation(currentVelocity);
    }

    private void Update()
    {
        lifeTimer -= Time.deltaTime;
        if (lifeTimer <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        // Homing arc tracking behavior
        if (trackingTimer > 0f && target != null)
        {
            trackingTimer -= Time.deltaTime;
            Vector3 targetPos = target.position + Vector3.up * 1.0f;
            Vector3 desiredDir = (targetPos - transform.position).normalized;
            currentVelocity = Vector3.RotateTowards(currentVelocity.normalized, desiredDir, 4f * Time.deltaTime, 0.0f) * speed;
        }

        // Move projectile position
        transform.position += currentVelocity * Time.deltaTime;

        // Update the Line Renderer trail positions dynamically every frame
        if (lineRenderer != null)
        {
            Vector3 currentPos = transform.position;
            Vector3 trailStartPos = currentPos - (currentVelocity.normalized * trailLength);
            
            lineRenderer.SetPosition(0, trailStartPos); // Tail of the trail
            lineRenderer.SetPosition(1, currentPos);    // Head of the trail at the bullet
        }

        // Face the exact direction of travel path
        if (currentVelocity.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(currentVelocity);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy") || other.GetComponent<ImpBrain>() != null) return;

        if (other.CompareTag("Player") || other.GetComponent<PlayerController>() != null)
        {
            ApplyDamage(other);
            Destroy(gameObject);
        }
        else if (!other.isTrigger)
        {
            Destroy(gameObject);
        }
    }

    private void ApplyDamage(Collider playerCollider)
    {
        if (playerCollider.TryGetComponent<IDamageable>(out var damageable))
        {
            Vector3 hitDir = (playerCollider.transform.position - transform.position).normalized;
            if (string.IsNullOrEmpty(reactionAnimName)) reactionAnimName = "Hit_Light";
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