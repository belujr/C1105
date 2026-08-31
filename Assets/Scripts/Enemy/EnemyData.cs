using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemy", menuName = "Combat/Enemy Data")]
public class EnemyData : ScriptableObject
{
	public string enemyName = "Dummy";
	public int maxHealth = 50;
}