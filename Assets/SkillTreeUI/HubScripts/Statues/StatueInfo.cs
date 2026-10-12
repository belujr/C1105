using UnityEngine;

[CreateAssetMenu(fileName = "NewStatueInfo", menuName = "Hub/Statue Info")]
public class StatueInfo : ScriptableObject
{
    [Header("Statue Lore Details")]
    public string statueName; //[cite: 24]
    
    [TextArea(3, 8)]
    public string statueDescription; //[cite: 24]

    [Header("Text Reveal")]
    [Tooltip("Per-statue reveal speed for the description. 1 = normal, 2 = twice as fast, 0.5 = half speed.")]
    [Min(0.1f)]
    public float typingSpeedMultiplier = 1f;
}