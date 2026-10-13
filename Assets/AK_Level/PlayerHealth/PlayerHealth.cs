using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    /// <summary>Raised once when the player dies. The death VFX (and later a game-over screen) subscribe to this.</summary>
    public static event System.Action<PlayerHealth> Died;

    public bool IsDead { get { return isDead; } }

    /// <summary>Horizontal direction of the last hit (the direction the attack was travelling). The death effect lets the body fall that way.</summary>
    public Vector3 LastHitDirection { get; private set; }

    /// <summary>True while the player is dead. Enemies can check this to stop attacking or chasing.</summary>
    public static bool PlayerIsDead { get; private set; }

    private readonly List<Collider> collidersDisabledByDeath = new List<Collider>();

    [Header("Health Settings")]
    public int maxHealth = 100;
    private int currentHealth;

    [Header("Hit Feedback Settings")]
    public Animator animator;
    public AudioSource audioSource;
    public ParticleSystem hitEffectPrefab;
    public Color damageFlashColor = Color.red;
    public float flashDuration = 0.1f;

    [Header("Death")]
    [Tooltip("Optional. Name of an Animator trigger to play when the player dies. Leave empty for none.")]
    public string deathAnimatorTrigger = "";

    [Tooltip("Seconds to let the death effects play before loading the Hub.")]
    public float hubTransitionDelay = 1.5f;

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
    private Coroutine activeKnockback;

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
        PlayerIsDead = false;
        if (playerController != null) playerController.Revive();
        if (playerCollider != null) playerCollider.enabled = true;
        for (int i = 0; i < collidersDisabledByDeath.Count; i++)
            if (collidersDisabledByDeath[i] != null) collidersDisabledByDeath[i].enabled = true;
        collidersDisabledByDeath.Clear();
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
        Vector3 flatHit = new Vector3(hitDirection.x, 0f, hitDirection.z);
        LastHitDirection = flatHit.sqrMagnitude > 0.0001f ? flatHit.normalized : -cachedTransform.forward;
        currentHealth -= finalDamage;
        Debug.Log($"Player took {finalDamage} damage! Remaining Health: {currentHealth}/{maxHealth}");

        bool lethal = currentHealth <= 0;

        if (force > 0f && !lethal)
        {
            activeKnockback = StartCoroutine(ApplyPlayerKnockback(hitDirection, force));
        }

        if (audioSource != null && hitSound != null)
        {
            audioSource.PlayOneShot(hitSound);
        }

        if (playerController != null && !isDead && !lethal)
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
        if (playerController != null && playerController.CurrentState != playerController.DashState)
        {
            return false;
        }

        if (playerVelocity.sqrMagnitude < 0.2f) return false;

        Vector3 moveDir = playerVelocity.normalized;
        float dot = Vector3.Dot(moveDir, hitDirection.normalized);
        return dot >= -0.3f;
    }

    public void SetComboStunLock(bool isLocked)
    {
        isCaughtInEnemyCombo = isLocked;
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
        if (isDead) return; // guarantees the run counter is incremented only once per death
        isDead = true;

        PlayerIsDead = true;

        if (activeKnockback != null)
        {
            StopCoroutine(activeKnockback);
            activeKnockback = null;
        }

        if (playerController != null) playerController.OnPlayerDied();

        foreach (Collider c in GetComponentsInChildren<Collider>())
        {
            if (c.enabled)
            {
                c.enabled = false;
                collidersDisabledByDeath.Add(c);
            }
        }

        if (animator != null && !string.IsNullOrEmpty(deathAnimatorTrigger)) animator.SetTrigger(deathAnimatorTrigger);

        if (GetComponent<PlayerDeathFX>() == null) gameObject.AddComponent<PlayerDeathFX>();

        Debug.Log("Player died.");

        if (HubNarrativeManager.Instance == null)
            Debug.LogError("[PlayerHealth] No HubNarrativeManager found - this death will NOT be counted as a run. Add the manager to the first scene (Scene_Level).");

        // HubNarrativeManager subscribes to this event: it ends the current run, starts the next one and saves.
        if (Died != null) Died(this);

        StartCoroutine(DeathTransitionRoutine());
    }

    private IEnumerator DeathTransitionRoutine()
    {
        yield return new WaitForSeconds(hubTransitionDelay); // Allow death visual effects to play
        UnityEngine.SceneManagement.SceneManager.LoadScene(HubNarrativeManager.HubSceneNameOrDefault);
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