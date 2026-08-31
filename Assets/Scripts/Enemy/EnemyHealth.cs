using UnityEngine;

public class EnemyHealth : MonoBehaviour, IDamageable
{
	public EnemyData baseStats;
	private int currentHealth;
	private bool isDead = false;

	// We replace the UnityEvents with a direct, automatic link
	private EnemyFeedback visualFeedback;

	private void Start()
	{
		if (baseStats != null)
			currentHealth = baseStats.maxHealth;

		// This automatically finds the EnemyFeedback script on the dummy!
		visualFeedback = GetComponent<EnemyFeedback>();
	}

	public void TakeDamage(int damageAmount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce = 1.5f, AudioClip hitSound = null)
	{
		if (isDead) return;

		currentHealth -= damageAmount;

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