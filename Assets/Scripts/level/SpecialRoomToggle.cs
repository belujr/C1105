using UnityEngine;

public class SpecialRoomToggle : MonoBehaviour
{
    public GameObject buffRoom;
    public GameObject fightRoom;

    [Range(0f, 1f)]
    public float buffRoomChance = 0.25f; // 25% chance for a buff room

    void Start()
    {
        if (Random.value <= buffRoomChance)
        {
            buffRoom.SetActive(true);
        }
        else
        {
            fightRoom.SetActive(true);
        }
    }
}