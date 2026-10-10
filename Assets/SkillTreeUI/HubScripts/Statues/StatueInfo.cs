using UnityEngine;

[CreateAssetMenu(fileName = "NewStatueInfo", menuName = "Hub/Statue Info")]
public class StatueInfo : ScriptableObject
{
    [Header("Statue Lore Details")]
    public string statueName; //[cite: 24]
    
    [TextArea(3, 8)]
    public string statueDescription; //[cite: 24]
}