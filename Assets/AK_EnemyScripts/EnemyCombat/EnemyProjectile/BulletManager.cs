using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class BulletManager : MonoBehaviour
{
    public static BulletManager Instance { get; private set; }

    [Header("Settings")]
    public int maxLiveBullets = 1000;
    public LayerMask wallLayerMask;

    [Header("Hit detection")]
    [Tooltip("Bullets that start high (a floating minion) glide down to the player's chest height, so they can actually hit. Before, a bullet that flew at the minion's height passed over the player's head.")]
    public bool dropToPlayerHeight = true;
    [Tooltip("How fast bullets glide down to the player's height (meters per second).")]
    public float dropSpeed = 8f;
    [Tooltip("A bullet hits when it is close enough sideways AND within this height of the player's chest.")]
    public float verticalTolerance = 1.6f;

    private Transform playerTransform;
    private IDamageable playerDamageable;

    private class BulletNode
    {
        public bool active;
        public int prefabId;
        public GameObject go;
        public Transform tr;

        public BulletPattern pattern;
        public Vector3 position;
        public Vector3 velocity;
        public float currentSpeed;
        public float lifeTimer;
        public float trackTimer;
    }

    private BulletNode[] bullets;
    private Dictionary<int, Queue<GameObject>> visualPools = new Dictionary<int, Queue<GameObject>>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        bullets = new BulletNode[maxLiveBullets];
        for (int i = 0; i < maxLiveBullets; i++) bullets[i] = new BulletNode { active = false };
    }

    private void Start()
    {
        PlayerController pc = FindObjectOfType<PlayerController>();
        if (pc != null)
        {
            playerTransform = pc.transform;
            playerDamageable = pc.GetComponent<IDamageable>();
        }
    }

    public void FirePattern(BulletPattern patternPrefab, Vector3 spawnPoint, Vector3 targetDirection)
    {
        if (patternPrefab == null) return;
        StartCoroutine(FirePatternRoutine(patternPrefab, patternPrefab.GetInstanceID(), spawnPoint, targetDirection.normalized));
    }

    private IEnumerator FirePatternRoutine(BulletPattern pattern, int pId, Vector3 origin, Vector3 fwd)
    {
        int count = Mathf.Max(1, pattern.count);
        int waves = Mathf.Max(1, pattern.waves);
        float arc = pattern.arc;

        float startAngle = count > 1 ? -arc * 0.5f + pattern.angleOffset : pattern.angleOffset;
        float step = count > 1 ? arc / (count - 1) : 0f;

        // Override for perfect 360 rings
        if (pattern.mode == FireMode.Ring && count > 1)
        {
            startAngle = pattern.angleOffset;
            step = 360f / count;
        }

        for (int w = 0; w < waves; w++)
        {
            float waveRot = w * pattern.perWaveRotationDelta;

            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + (step * i) + waveRot;
                if (pattern.jitter > 0f) angle += UnityEngine.Random.Range(-pattern.jitter, pattern.jitter);

                Vector3 dir = Quaternion.Euler(0, angle, 0) * fwd;
                SpawnBullet(pattern, pId, origin, dir);
            }

            if (w < waves - 1 && pattern.interval > 0f)
            {
                yield return new WaitForSeconds(pattern.interval);
            }
        }
    }

    private void SpawnBullet(BulletPattern data, int pId, Vector3 pos, Vector3 dir)
    {
        // 1. Find logic slot
        BulletNode node = null;
        for (int i = 0; i < maxLiveBullets; i++)
        {
            if (!bullets[i].active) { node = bullets[i]; break; }
        }
        if (node == null) return; // Cap reached, graceful degradation

        // 2. Fetch or Instantiate Visual (Warm-up phase creates, Runtime reuses)
        if (!visualPools.ContainsKey(pId)) visualPools[pId] = new Queue<GameObject>();

        if (visualPools[pId].Count > 0)
        {
            node.go = visualPools[pId].Dequeue();
            node.tr = node.go.transform;
        }
        else
        {
            node.go = Instantiate(data.gameObject);
            node.tr = node.go.transform;
            // Strip logic from the visual clone to prevent double-execution
            var clonePattern = node.go.GetComponent<BulletPattern>();
            if (clonePattern != null) Destroy(clonePattern);
        }

        // 3. Initialize state
        node.active = true;
        node.prefabId = pId;
        node.pattern = data;
        node.position = pos;
        node.currentSpeed = data.bulletSpeed;
        node.velocity = dir * node.currentSpeed;
        node.lifeTimer = data.lifetime;
        node.trackTimer = data.homingDuration;

        node.tr.position = pos;
        node.tr.rotation = Quaternion.LookRotation(dir);
        node.go.SetActive(true);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool hasPlayer = playerTransform != null && playerDamageable != null;
        Vector3 playerPos = hasPlayer ? playerTransform.position + Vector3.up * 1.0f : Vector3.zero;

        // Wall cast striding (test 25% of bullets this frame to save CPU)
        int strideOffset = Time.frameCount % 4;

        for (int i = 0; i < maxLiveBullets; i++)
        {
            BulletNode b = bullets[i];
            if (!b.active) continue;

            // 1. Lifetime
            b.lifeTimer -= dt;
            if (b.lifeTimer <= 0f) { RetireBullet(b); continue; }

            // 2. Homing
            if (b.pattern.homingStrength > 0f && hasPlayer && b.trackTimer > 0f)
            {
                b.trackTimer -= dt;
                Vector3 desiredDir = (playerPos - b.position).normalized;
                b.velocity = Vector3.RotateTowards(b.velocity.normalized, desiredDir, b.pattern.homingTurnCap * Mathf.Deg2Rad * dt, 0f) * b.currentSpeed;
            }

            // 3. Kinematics
            if (b.pattern.acceleration != 0f)
            {
                b.currentSpeed += b.pattern.acceleration * dt;
                b.velocity = b.velocity.normalized * b.currentSpeed;
            }

            Vector3 nextPos = b.position + b.velocity * dt;

            // bullets that started high glide down to the player's chest height
            if (dropToPlayerHeight && hasPlayer)
                nextPos.y = Mathf.MoveTowards(nextPos.y, playerPos.y, dropSpeed * dt);

            // 4. Analytic Player Collision
            if (hasPlayer)
            {
                // a cylinder around the player: close enough sideways, and within a height tolerance
                Vector3 toPlayer = playerPos - nextPos;
                float dy = Mathf.Abs(toPlayer.y);
                toPlayer.y = 0f;
                float r = b.pattern.hitRadius + 0.5f; // player radius approx
                if (dy <= verticalTolerance && toPlayer.sqrMagnitude <= r * r)
                {
                    ApplyDamageToPlayer(b);
                    RetireBullet(b);
                    continue;
                }
            }

            // 5. Throttled Wall Raycast (Batched logic)
            if ((i % 4) == strideOffset && wallLayerMask != 0)
            {
                Vector3 delta = nextPos - b.position;
                if (Physics.Raycast(b.position, delta.normalized, out RaycastHit hit, delta.magnitude * 4f, wallLayerMask, QueryTriggerInteraction.Ignore))
                {
                    RetireBullet(b);
                    continue;
                }
            }

            // 6. Sync Visuals
            b.position = nextPos;
            b.tr.position = b.position;
            if (b.velocity.sqrMagnitude > 0.001f) b.tr.rotation = Quaternion.LookRotation(b.velocity);
        }
    }

    private void ApplyDamageToPlayer(BulletNode b)
    {
        Vector3 hitDir = b.velocity.normalized;
        int animHash = Animator.StringToHash(string.IsNullOrEmpty(b.pattern.playerReactionName) ? "Hit_Light" : b.pattern.playerReactionName);

        if (playerDamageable is PlayerHealth ph)
        {
            ph.TakeDamage(b.pattern.damage, b.position, hitDir, b.pattern.knockback, null, animHash, false, b.pattern.stun);
        }
        else
        {
            playerDamageable.TakeDamage(b.pattern.damage, b.position, hitDir, b.pattern.knockback, null, animHash, false);
        }
    }

    private void RetireBullet(BulletNode b)
    {
        if (!b.active) return;

        b.active = false;
        b.go.SetActive(false);
        // Pool the visual itself, never the mutable logic slot that owned it.
        visualPools[b.prefabId].Enqueue(b.go);
        b.go = null;
        b.tr = null;
    }
}