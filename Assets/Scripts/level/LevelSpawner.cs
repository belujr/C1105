using UnityEngine;

public class LevelSpawner : MonoBehaviour
{
    public GameObject[] roomsLayoutVariations;
    public GameObject playerPrefab; // Assign your player character here

    void Start()
    {
        // 1. Pick a random layout and spawn it
        int randomIndex = Random.Range(0, roomsLayoutVariations.Length);
        Instantiate(roomsLayoutVariations[randomIndex]);

        // 2. Find the empty GameObject tagged "PlayerSpawn"
        GameObject spawnPoint = GameObject.FindGameObjectWithTag("PlayerSpawn");

        // 3. Spawn the player at that exact position and rotation
        if (spawnPoint != null)
        {
            Instantiate(playerPrefab, spawnPoint.transform.position, spawnPoint.transform.rotation);
        }
        else
        {
            Debug.LogWarning("Could not find an object tagged 'PlayerSpawn' in the scene!");
        }
    }
}