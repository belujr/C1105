using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
    public EnemyData baseStats;
    private int currentHealth;
    private bool isDead = false;

    private EnemyFeedback visualFeedback;

    private void Start()
    {
        if (baseStats != null)
            currentHealth = baseStats.maxHealth;

        visualFeedback = GetComponent<EnemyFeedback>();
    }

    // Signature updated to match IDamageable interface exactly
    public void TakeDamage(float damageAmount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce, AudioClip hitSound, int hitType, bool isCritical)
    {
        if (isDead) return;

        // Convert the float damage to int to match currentHealth
        currentHealth -= Mathf.RoundToInt(damageAmount);

        if (currentHealth <= 0)
        {
            isDead = true;
            if (visualFeedback != null)
            {
                visualFeedback.PlayDeathReaction(hitDirection, knockbackForce);
            }
        }
        else
        {
            if (visualFeedback != null)
            {
                visualFeedback.PlayHitReaction(hitPoint, hitDirection, knockbackForce, hitSound);
            }
        }
    }
}