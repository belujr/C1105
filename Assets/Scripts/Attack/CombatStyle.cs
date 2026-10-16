using System.Collections.Generic;
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

    public AttackData GetActiveChargeAttack()
    {
        if (availableChargeAttacks == null || availableChargeAttacks.Length == 0) return null;
        int safeIndex = Mathf.Clamp(selectedChargeAttackIndex, 0, availableChargeAttacks.Length - 1);
        return availableChargeAttacks[safeIndex];
    }

    public AttackData GetAttackData(int comboIndex)
    {
        if (lightComboSequence == null || lightComboSequence.Length == 0)
        {
            return null;
        }

        int safeIndex = comboIndex % lightComboSequence.Length;
        return lightComboSequence[safeIndex];
    }

    /// <summary>
    /// Trims trailing null elements and resizes the array so the combo only loops up to the last assigned attack.
    /// </summary>
    public void TrimEmptyTrailingSlots()
    {
        if (lightComboSequence == null || lightComboSequence.Length == 0) return;

        int lastValidIndex = -1;
        for (int i = 0; i < lightComboSequence.Length; i++)
        {
            if (lightComboSequence[i] != null)
            {
                lastValidIndex = i;
            }
        }

        // If everything is empty, keep a minimum array size of 0 or 1 safely, or resize to last valid index + 1
        int newSize = Mathf.Max(0, lastValidIndex + 1);
        if (newSize != lightComboSequence.Length)
        {
            System.Array.Resize(ref lightComboSequence, newSize);
        }
    }
}