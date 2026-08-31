using UnityEngine;

public interface IDamageable
{
	void TakeDamage(int damageAmount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce = 1.5f, AudioClip hitSound = null);
}