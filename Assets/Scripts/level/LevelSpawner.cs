using UnityEngine;

public class LevelSpawner : MonoBehaviour
{
    public GameObject[] roomsLayoutVariations;
    public GameObject playerPrefab;
    public GameObject globalLight;

    // Store references to the clones so we can delete them
    private GameObject currentLayoutInstance;
    private GameObject currentPlayerInstance;

    public void GenerateLevel()
    {
        int randomIndex = Random.Range(0, roomsLayoutVariations.Length);
        currentLayoutInstance = Instantiate(roomsLayoutVariations[randomIndex]);

        GameObject spawnPoint = GameObject.FindGameObjectWithTag("PlayerSpawn");

        if (spawnPoint != null)
        {
            currentPlayerInstance = Instantiate(playerPrefab, spawnPoint.transform.position, spawnPoint.transform.rotation);

            IsoCameraRig camRig = FindObjectOfType<IsoCameraRig>();
            if (camRig != null) camRig.SetTarget(currentPlayerInstance.transform);
        }

        if (globalLight != null) globalLight.SetActive(true);
    }

    // Call this when pressing R3 to wipe the level from existence
    public void ClearLevel()
    {
        if (currentLayoutInstance != null) Destroy(currentLayoutInstance);
        if (currentPlayerInstance != null) Destroy(currentPlayerInstance);
        if (globalLight != null) globalLight.SetActive(false);
    }
}