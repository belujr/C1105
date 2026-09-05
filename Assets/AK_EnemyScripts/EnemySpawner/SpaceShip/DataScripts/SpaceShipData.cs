using UnityEngine;

[CreateAssetMenu(fileName = "NewSpaceshipData", menuName = "CombatRoguelike/Spawner/Spaceship Data")]
public class SpaceshipData : ScriptableObject
{
    [Header("Ship Identity")]
    [Tooltip("Name identifier for this ship type (e.g., Medium Dropper, Large Dropper, Elite Dropper).")]
    public string shipTypeName = "Medium Dropper";
    
    [Tooltip("The visual prefab representing the spaceship model.")]
    public GameObject shipPrefab;

    [Header("Payload Configuration")]
    [Tooltip("The list of possible enemy prefabs this ship can deploy.")]
    public GameObject[] enemyPrefabs;

    [Range(1, 10)]
    [Tooltip("Exact number of enemies this ship will drop per trip.")]
    public int payloadCapacity = 4;

    [Header("Flight Parameters")]
    [Tooltip("Speed at which the ship flies into the drop zone.")]
    public float flyInSpeed = 15f;

    [Tooltip("Speed at which the ship flies away after dropping its payload.")]
    public float flyOutSpeed = 20f;

    [Header("Drop Mechanics")]
    [Tooltip("Delay in seconds between each individual enemy drop.")]
    public float dropInterval = 0.5f;

    [Tooltip("Maximum scatter radius around the drop target where enemies land, preventing them from stacking.")]
    public float dropScatterRadius = 3f;
}