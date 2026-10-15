using UnityEngine;

/// <summary>
/// Sits on the buff room portal (BuffRoomPedestal adds it for you). When the player walks through,
/// they are sent back to the scene that shows the chamber selection map, with the normal seamless fade.
///
/// Needs a trigger collider on the same GameObject (BuffRoomPedestal adds one when the portal has none).
/// </summary>
public class BuffRoomPortalExit : MonoBehaviour
{
    [Tooltip("The scene that shows the chamber selection map (the same one MapController uses).")]
    public string returnScene = "Scene_Level";

    [Tooltip("Seconds after the portal appears before it works, so it is mostly open first.")]
    public float armDelay = 0.8f;

    private float armedAt;

    private void OnEnable()
    {
        armedAt = Time.time + armDelay;
    }

    private void OnTriggerEnter(Collider other)
    {
        Try(other);
    }

    private void OnTriggerStay(Collider other)
    {
        Try(other);   // also works if the player was already inside when the portal finished opening
    }

    private void Try(Collider other)
    {
        if (Time.time < armedAt) return;
        if (SceneTransitionManager.IsTransitioning || !other.CompareTag("Player")) return;

        SceneTransitionManager.LoadScene(returnScene);
    }
}
