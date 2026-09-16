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

        // 3. Spawn the player and assign it to the camera
        if (spawnPoint != null)
        {
            // Store the spawned clone in a variable
            GameObject spawnedPlayer = Instantiate(playerPrefab, spawnPoint.transform.position, spawnPoint.transform.rotation);

            // Find the IsoCameraRig in the scene and set its target
            IsoCameraRig camRig = FindObjectOfType<IsoCameraRig>();
            if (camRig != null)
            {
                camRig.SetTarget(spawnedPlayer.transform);
            }
            else
            {
                Debug.LogWarning("Could not find the IsoCameraRig in the scene!");
            }
        }
        else
        {
            Debug.LogWarning("Could not find an object tagged 'PlayerSpawn' in the scene!");
        }
    }
}