using UnityEngine;

public class LevelSpawner : MonoBehaviour
{
    public GameObject[] roomsLayoutVariations;
    public GameObject playerPrefab;
    public GameObject globalLight; // Assign your Directional Light in the Inspector

    public void GenerateLevel()
    {
        int randomIndex = Random.Range(0, roomsLayoutVariations.Length);
        Instantiate(roomsLayoutVariations[randomIndex]);

        GameObject spawnPoint = GameObject.FindGameObjectWithTag("PlayerSpawn");

        if (spawnPoint != null)
        {
            GameObject spawnedPlayer = Instantiate(playerPrefab, spawnPoint.transform.position, spawnPoint.transform.rotation);

            IsoCameraRig camRig = FindObjectOfType<IsoCameraRig>();
            if (camRig != null)
            {
                camRig.SetTarget(spawnedPlayer.transform);
            }
        }

        // Turn on the global level light
        if (globalLight != null)
        {
            globalLight.SetActive(true);
        }
    }
}