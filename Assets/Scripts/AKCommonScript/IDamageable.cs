using UnityEngine;

public interface IDamageable
{
	void TakeDamage(float damageAmount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce = 1.5f, AudioClip hitSound = null, int attackID = -1, bool isAOE = false);
}