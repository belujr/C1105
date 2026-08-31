using UnityEngine;

[CreateAssetMenu(fileName = "NewCombatStyle", menuName = "Combat/Combat Style")]
public class CombatStyle : ScriptableObject
{
	public string styleName = "Street Brawler";

	[Header("Action Button (X)")]
	public AttackData[] lightComboSequence;

	[Header("Heavy Button (RT)")]
	[Tooltip("Put all possible heavy attacks for this style here.")]
	public AttackData[] availableChargeAttacks;

	[Tooltip("Which attack from the list above is currently active? (0 = First, 1 = Second)")]
	public int selectedChargeAttackIndex = 0;

	// A handy helper method so your PowerPunch script can easily grab the correct data!
	public AttackData GetActiveChargeAttack()
	{
		if (availableChargeAttacks == null || availableChargeAttacks.Length == 0) return null;

		// This ensures the index doesn't crash the game if you type a number that is too high
		int safeIndex = Mathf.Clamp(selectedChargeAttackIndex, 0, availableChargeAttacks.Length - 1);
		return availableChargeAttacks[safeIndex];
	}

	// This safely fetches the correct punch from your combo list based on the index!
	public AttackData GetAttackData(int comboIndex)
	{
		if (lightComboSequence == null || lightComboSequence.Length == 0)
		{
			return null;
		}

		// The '%' ensures that if the index goes higher than your list length, 
		// it loops cleanly back to the first punch instead of crashing the game.
		int safeIndex = comboIndex % lightComboSequence.Length;
		return lightComboSequence[safeIndex];
	}
}