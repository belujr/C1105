using UnityEngine;

public class LevelSpawner : MonoBehaviour
{
    public GameObject[] roomsLayoutVariations; // Your 6 chamber prefabs[cite: 14]
    public GameObject playerPrefab; //[cite: 14]
    public GameObject globalLight; //[cite: 14]

    [Header("UI Minimap Reference")]
    public MinimapUIController minimapUIController;

    private GameObject currentLayoutInstance; //[cite: 14]
    private GameObject currentPlayerInstance; //[cite: 14]

    public void GenerateLevel() //[cite: 14]
    {
        int randomIndex = Random.Range(0, roomsLayoutVariations.Length); //[cite: 14]
        currentLayoutInstance = Instantiate(roomsLayoutVariations[randomIndex]); //[cite: 14]

        GameObject spawnPoint = GameObject.FindGameObjectWithTag("PlayerSpawn"); //[cite: 14]

        if (spawnPoint != null) //[cite: 14]
        {
            currentPlayerInstance = Instantiate(playerPrefab, spawnPoint.transform.position, spawnPoint.transform.rotation); //[cite: 14]

            IsoCameraRig camRig = FindObjectOfType<IsoCameraRig>(); //[cite: 14]
            if (camRig != null) camRig.SetTarget(currentPlayerInstance.transform); //[cite: 14]
        }

        if (globalLight != null) globalLight.SetActive(true); //[cite: 14]

        // Initialize Minimap UI using the spawned layout's start room
        if (currentLayoutInstance.TryGetComponent<ChamberLayout>(out ChamberLayout layout))
        {
            if (layout.startRoom != null && minimapUIController != null)
            {
                minimapUIController.InitializeMap(layout.startRoom);
            }
        }
    }

    public void ClearLevel() //[cite: 14]
    {
        if (currentLayoutInstance != null) Destroy(currentLayoutInstance); //[cite: 14]
        if (currentPlayerInstance != null) Destroy(currentPlayerInstance); //[cite: 14]
        if (globalLight != null) globalLight.SetActive(false); //[cite: 14]
    }
}