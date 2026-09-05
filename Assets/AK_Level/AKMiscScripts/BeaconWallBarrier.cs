using UnityEngine;

public class BeaconWallBarrier : MonoBehaviour
{
    [Header("Wall Configuration")]
    [Tooltip("The wall GameObject to control. If left empty, this script will control its own GameObject.")]
    [SerializeField] private GameObject wallObject;

    private void Awake()
    {
        if (wallObject == null)
        {
            wallObject = gameObject;
        }

        // Ensure the wall starts disabled before the beacon is activated
        wallObject.SetActive(false);
    }

    /// <summary>
    /// Makes the wall appear at its set position. Hook this up to Beacon Activated event.
    /// </summary>
    public void BuildWall()
    {
        if (wallObject != null)
        {
            wallObject.SetActive(true);
        }
    }

    /// <summary>
    /// Makes the wall disappear. Hook this up to Beacon Destroyed event.
    /// </summary>
    public void Disappear()
    {
        if (wallObject != null)
        {
            wallObject.SetActive(false);
        }
    }
}