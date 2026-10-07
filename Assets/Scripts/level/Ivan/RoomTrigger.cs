using UnityEngine;

public class RoomTrigger : MonoBehaviour
{
    public RoomNode parentRoom;
    private MinimapUIController minimapUI;

    private void Start()
    {
        minimapUI = FindObjectOfType<MinimapUIController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && parentRoom != null && minimapUI != null)
        {
            minimapUI.OnPlayerEnteredRoom(parentRoom);
        }
    }
}