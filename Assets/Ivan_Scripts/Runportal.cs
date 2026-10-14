using UnityEngine;
 
// Trigger collider that sends the player to another scene with a seamless fade.
[RequireComponent(typeof(Collider))]
public class RunPortal : MonoBehaviour
{
    [SerializeField] private string targetScene = "scene_level";
 
    private void OnTriggerEnter(Collider other)
    {
        // Ignore everything except the player, and ignore re-triggers mid-transition
        if (SceneTransitionManager.IsTransitioning || !other.CompareTag("Player")) return;
 
        if (SceneTransitionManager.LoadScene(targetScene))
        {
            CameraTransitionManager.RequestGameplayEntry(targetScene); // skips a title shot if that scene has one
        }
    }
}