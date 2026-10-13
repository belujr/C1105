using UnityEngine;

public class HubGraveManager : MonoBehaviour
{
    [Header("Tombstones (Runs 1 to 7)")]
    [Tooltip("Assign your tombstone GameObjects in sequential order from Run 1 to Run 7.")]
    public GameObject[] tombstones;

    private void Start()
    {
        UpdateTombstones();
    }

    private void UpdateTombstones()
    {
        int currentRun = 0;

        if (HubNarrativeManager.Instance != null)
        {
            currentRun = HubNarrativeManager.Instance.currentRunNumber;
        }
        else
        {
            Debug.LogWarning("[HubGraveManager] HubNarrativeManager instance not found in scene.");
        }

        // Loop through the array and activate tombstones up to the current run count
        for (int i = 0; i < tombstones.Length; i++)
        {
            if (tombstones[i] != null)
            {
                // i = 0 corresponds to Run 1, i = 1 to Run 2, etc.
                bool shouldBeActive = currentRun >= (i + 1);
                tombstones[i].SetActive(shouldBeActive);
            }
        }
    }
}