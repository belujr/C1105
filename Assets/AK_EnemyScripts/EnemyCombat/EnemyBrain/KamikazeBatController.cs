using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class KamikazeBatController : MonoBehaviour, IDamageable
{
    public enum BatState { Spawning, Chasing, Fusing, Dead }

    [Header("Core References")]
    [Tooltip("Auto-caches statically. You do not need to assign this manually for every bat.")]
    public PlayerController player;
    private Transform cachedTransform;
    private Transform playerTransform;

    private static PlayerController globalCachedPlayer;

    [Header("Stats")]
    public float maxHealth = 30f;
    private float currentHealth;

    [Header("Movement & Hovering")]
    public float chaseSpeed = 5.0f;
    [Tooltip("How high above the ground/player the bat tries to fly.")]
    public float hoverHeightOffset = 1.5f;
    public float floatSpeed = 4.0f;
    public float floatAmplitude = 0.3f;
    public float rotationSpeed = 10f;

    [Header("Kamikaze & Explosion Parameters")]
    [Tooltip("Distance to player required to trigger the explosion fuse.")]
    public float fuseTriggerDistance = 2.0f;
    [Tooltip("How long the bat waits before exploding once in range.")]
    public float fuseDuration = 1.5f;
    public float explosionRadius = 3.5f;
    public float explosionDamage = 40f;
    public float explosionKnockback = 4.0f;

    [Header("Player Reaction Hook")]
    public string playerReactionAnimName = "Hit_Heavy";
    public float stunDuration = 0.5f;

    [Header("Swarm Token Rules")]
    [Tooltip("If false, this enemy ignores token limits and swarms the player (Clash of Clans style).")]
    public bool requiresTokenToFuse = false;
    public TokenType tokenType = TokenType.AgileFlanker;
    private bool holdsToken = false;

    [Header("VFX & Audio (Local)")]
    public ParticleSystem explosionVFXPrefab;
    public AudioClip explosionSound;
    public AudioClip fuseTickSound;
    private AudioSource audioSource;

    [Tooltip("Color the bat flashes while the fuse is ticking down.")]
    public Color fuseFlashColor = Color.red;

    private Renderer[] batRenderers;
    private MaterialPropertyBlock propBlock;

    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    private static readonly Collider[] explosionBuffer = new Collider[16];

    private BatState currentState = BatState.Spawning;
    private Coroutine activeFuseRoutine;
    private Coroutine activeKnockbackRoutine;

    private float spawnTimer = 0f;
    private float randomizedSpeed;
    private float timeOffset;

    private void Awake()
    {
        cachedTransform = transform;
        audioSource = GetComponent<AudioSource>();
        batRenderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();

        FindPlayerReference();
        timeOffset = Random.Range(0f, 100f);
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        currentState = BatState.Spawning;
        holdsToken = false;
        activeFuseRoutine = null;
        activeKnockbackRoutine = null;

        spawnTimer = 0.5f;
        randomizedSpeed = chaseSpeed * Random.Range(0.85f, 1.15f);

        ResetColors();
        FindPlayerReference();
    }

    private void OnDisable()
    {
        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }
    }

    private void FindPlayerReference()
    {
        if (playerTransform != null) return;

        if (globalCachedPlayer == null)
        {
            globalCachedPlayer = player != null ? player : FindObjectOfType<PlayerController>();
        }

        player = globalCachedPlayer;
        if (player != null) playerTransform = player.transform;
    }

    private void Update()
    {
        if (currentState == BatState.Dead || playerTransform == null) return;

        if (currentState == BatState.Spawning)
        {
            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f) currentState = BatState.Chasing;
            return;
        }

        if (currentState == BatState.Chasing)
        {
            Vector3 playerPos = playerTransform.position;
            Vector3 currentPos = cachedTransform.position;

            Vector3 dirToPlayer = playerPos - currentPos;
            float distSqr = dirToPlayer.sqrMagnitude;

            HandleFlightMovement(playerPos, dirToPlayer, distSqr);
            CheckFuseTrigger(distSqr);
        }
    }

    private void HandleFlightMovement(Vector3 targetPos, Vector3 dirToPlayer, float distSqr)
    {
        if (activeKnockbackRoutine != null) return;

        targetPos.y += hoverHeightOffset;
        targetPos.y += Mathf.Sin((Time.time + timeOffset) * floatSpeed) * floatAmplitude;

        cachedTransform.position = Vector3.MoveTowards(cachedTransform.position, targetPos, randomizedSpeed * Time.deltaTime);

        dirToPlayer.y = 0f;
        if (distSqr > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToPlayer);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void CheckFuseTrigger(float distSqr)
    {
        if (distSqr <= fuseTriggerDistance * fuseTriggerDistance)
        {
            if (requiresTokenToFuse && GlobalTokenManager.Instance != null)
            {
                if (!GlobalTokenManager.Instance.RequestToken(cachedTransform, tokenType))
                {
                    return;
                }
                holdsToken = true;
            }

            currentState = BatState.Fusing;
            activeFuseRoutine = StartCoroutine(FuseAndExplodeRoutine());
        }
    }

    private IEnumerator FuseAndExplodeRoutine()
    {
        float elapsed = 0f;
        float flashTimer = 0f;
        bool isFlashingRed = false;

        if (audioSource != null && fuseTickSound != null)
        {
            audioSource.clip = fuseTickSound;
            audioSource.loop = true;
            audioSource.Play();
        }

        while (elapsed < fuseDuration)
        {
            if (currentState == BatState.Dead) yield break;

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            flashTimer += deltaTime;

            float currentFlashInterval = Mathf.Lerp(0.2f, 0.05f, elapsed / fuseDuration);

            if (flashTimer >= currentFlashInterval)
            {
                flashTimer = 0f;
                isFlashingRed = !isFlashingRed;

                if (isFlashingRed) SetFlashColor(fuseFlashColor);
                else ResetColors();
            }

            if (playerTransform != null)
            {
                Vector3 dragPos = playerTransform.position;
                dragPos.y += hoverHeightOffset;
                cachedTransform.position = Vector3.MoveTowards(cachedTransform.position, dragPos, (randomizedSpeed * 0.3f) * Time.deltaTime);
            }

            yield return null;
        }

        Explode();
    }

    private void Explode()
    {
        if (currentState == BatState.Dead) return;
        currentState = BatState.Dead;

        if (audioSource != null) audioSource.Stop();

        if (explosionVFXPrefab != null)
        {
            ParticleSystem vfx = Instantiate(explosionVFXPrefab, cachedTransform.position, Quaternion.identity);
            vfx.Play();
            Destroy(vfx.gameObject, 2.5f);
        }

        int hitCount = Physics.OverlapSphereNonAlloc(cachedTransform.position, explosionRadius, explosionBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = explosionBuffer[i];
            if (hit != null && hit.CompareTag("Player") && hit.TryGetComponent<IDamageable>(out var damageable))
            {
                Vector3 hitDir = (hit.transform.position - cachedTransform.position).normalized;
                hitDir.y = 0f;

                // Parse the hash based on your Inspector text!
                int animHash = Animator.StringToHash(playerReactionAnimName);

                if (damageable is PlayerHealth ph)
                {
                    ph.TakeDamage(explosionDamage, hit.transform.position, hitDir, explosionKnockback, explosionSound, animHash, true, stunDuration);
                }
                else
                {
                    damageable.TakeDamage(explosionDamage, hit.transform.position, hitDir, explosionKnockback, explosionSound, animHash, true);
                }
            }
        }

        ReturnToPool();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (currentState == BatState.Dead) return;

        currentHealth -= damage;

        if (activeKnockbackRoutine != null) StopCoroutine(activeKnockbackRoutine);
        activeKnockbackRoutine = StartCoroutine(ApplyKnockback(hitDirection, force));

        if (currentHealth <= 0)
        {
            DieByPlayer();
        }
    }

    private void DieByPlayer()
    {
        currentState = BatState.Dead;

        if (activeFuseRoutine != null) StopCoroutine(activeFuseRoutine);
        if (audioSource != null) audioSource.Stop();

        ReturnToPool();
    }

    private IEnumerator ApplyKnockback(Vector3 hitDirection, float knockbackForce)
    {
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -cachedTransform.forward;
        pushDir.Normalize();

        float elapsed = 0f;
        float duration = 0.15f;
        Vector3 startPos = cachedTransform.position;
        Vector3 targetPos = startPos + (pushDir * knockbackForce);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            cachedTransform.position = Vector3.Lerp(startPos, targetPos, elapsed / duration);
            yield return null;
        }
        activeKnockbackRoutine = null;
    }

    private void ReturnToPool()
    {
        ResetColors();

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void SetFlashColor(Color targetColor)
    {
        propBlock.SetColor(ColorPropID, targetColor);
        propBlock.SetColor(BaseColorPropID, targetColor);

        for (int i = 0; i < batRenderers.Length; i++)
        {
            if (batRenderers[i] != null)
            {
                batRenderers[i].SetPropertyBlock(propBlock);
            }
        }
    }

    private void ResetColors()
    {
        for (int i = 0; i < batRenderers.Length; i++)
        {
            if (batRenderers[i] != null)
            {
                batRenderers[i].SetPropertyBlock(null);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, fuseTriggerDistance);

        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawSphere(transform.position, explosionRadius);
    }
}