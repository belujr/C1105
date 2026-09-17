using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    private int currentHealth;

    [Header("Hit Feedback Settings")]
    public Animator animator;
    public AudioSource audioSource;
    public ParticleSystem hitEffectPrefab;
    public Color damageFlashColor = Color.red;
    public float flashDuration = 0.1f;

    [Header("Death & Pause Settings")]
    public float deathDisappearDelay = 0.5f;

    [SerializeField]
    private bool isImmortal = false;

    private bool isDead = false;
    private bool isCaughtInEnemyCombo = false;
    private Transform cachedTransform;
    private Renderer[] playerRenderers;
    private Collider playerCollider;
    private PlayerController playerController;

    private Vector3 lastPosition;
    private Vector3 playerVelocity;

    private Dictionary<Renderer, Color[]> originalColors = new Dictionary<Renderer, Color[]>();
    private Coroutine activeFlashRoutine;

    private void Awake()
    {
        cachedTransform = transform;
        currentHealth = maxHealth;
        playerRenderers = GetComponentsInChildren<Renderer>();
        playerCollider = GetComponent<Collider>();
        playerController = GetComponent<PlayerController>();
        lastPosition = cachedTransform.position;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        foreach (var r in playerRenderers)
        {
            Color[] colors = new Color[r.materials.Length];
            for (int i = 0; i < r.materials.Length; i++)
            {
                if (r.materials[i].HasProperty("_Color"))
                {
                    colors[i] = r.materials[i].color;
                }
            }
            originalColors.Add(r, colors);
        }
    }

    private void Update()
    {
        if (Time.deltaTime > 0f)
        {
            playerVelocity = (cachedTransform.position - lastPosition) / Time.deltaTime;
            playerVelocity.y = 0f;
        }
        lastPosition = cachedTransform.position;
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        isDead = false;
        isCaughtInEnemyCombo = false;
        lastPosition = cachedTransform.position;
        if (playerCollider != null) playerCollider.enabled = true;
        SetRenderersVisible(true);
        ResetColors();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        TakeDamage(damage, hitPoint, hitDirection, force, hitSound, attackID, isAOE, 0.2f);
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE, float stunDuration)
    {
        if (isDead || isImmortal) return;

        if (EvaluateDirectionalDodge(hitDirection))
        {
            Debug.Log("[PlayerHealth] Attack successfully evaded via directional i-frames!");
            return;
        }

        int finalDamage = Mathf.RoundToInt(damage);
        currentHealth -= finalDamage;
        Debug.Log($"Player took {finalDamage} damage! Remaining Health: {currentHealth}/{maxHealth}");

        if (force > 0f)
        {
            StartCoroutine(ApplyPlayerKnockback(hitDirection, force));
        }

        if (audioSource != null && hitSound != null)
        {
            audioSource.PlayOneShot(hitSound);
        }

        // Trigger the modular HitState using the hash passed from the enemy
        if (playerController != null && !isDead)
        {
            float reactionTime = stunDuration > 0f ? stunDuration : 0.4f;
            playerController.TakeHit(attackID, reactionTime);
        }

        if (hitEffectPrefab != null)
        {
            Vector3 spawnDir = hitDirection == Vector3.zero ? Vector3.up : -hitDirection;
            ParticleSystem effect = Instantiate(hitEffectPrefab, hitPoint, Quaternion.LookRotation(spawnDir));
            effect.Play();
            Destroy(effect.gameObject, 2.0f);
        }

        if (activeFlashRoutine != null)
        {
            StopCoroutine(activeFlashRoutine);
        }
        activeFlashRoutine = StartCoroutine(DamageFlashRoutine());

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private bool EvaluateDirectionalDodge(Vector3 hitDirection)
    {
        // 1. If we aren't actively in the Dash State, we get hit. Walking doesn't grant i-frames!
        if (playerController != null && playerController.CurrentState != playerController.DashState)
        {
            return false;
        }

        // 2. If we are dashing but moving too slow, we get hit
        if (playerVelocity.sqrMagnitude < 0.2f) return false;

        // 3. If we are dashing sideways or backwards relative to the attack, we successfully dodge!
        Vector3 moveDir = playerVelocity.normalized;
        float dot = Vector3.Dot(moveDir, hitDirection.normalized);
        return dot >= -0.3f;
    }

    public void SetComboStunLock(bool isLocked)
    {
        isCaughtInEnemyCombo = isLocked;
        // playerController.enabled modification removed to allow state-based stuns
    }

    private IEnumerator ApplyPlayerKnockback(Vector3 hitDirection, float force)
    {
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -cachedTransform.forward;
        pushDir.Normalize();

        float elapsed = 0f;
        float duration = 0.12f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            Vector3 moveDelta = pushDir * (force * Time.deltaTime / duration);
            cachedTransform.position += moveDelta;
            yield return null;
        }
    }

    private IEnumerator DamageFlashRoutine()
    {
        foreach (var r in playerRenderers)
        {
            if (r == null) continue;
            foreach (var mat in r.materials)
            {
                if (mat.HasProperty("_Color")) mat.color = damageFlashColor;
            }
        }

        yield return new WaitForSeconds(flashDuration);
        ResetColors();
    }

    private void ResetColors()
    {
        foreach (var r in playerRenderers)
        {
            if (r == null || !originalColors.ContainsKey(r)) continue;
            for (int i = 0; i < r.materials.Length; i++)
            {
                if (r.materials[i].HasProperty("_Color"))
                {
                    r.materials[i].color = originalColors[r][i];
                }
            }
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (playerCollider != null) playerCollider.enabled = false;
        StartCoroutine(PlayerDeathRoutine());
    }

    private IEnumerator PlayerDeathRoutine()
    {
        yield return new WaitForSecondsRealtime(deathDisappearDelay);
        SetRenderersVisible(false);
        Time.timeScale = 0f;
        Debug.Log("Player died. Game paused.");
    }

    private void SetRenderersVisible(bool isVisible)
    {
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = isVisible;
            }
        }
    }
}