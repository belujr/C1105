using UnityEngine;
using UnityEngine.SceneManagement;

// Trigger collider that sends the player to another scene.
[RequireComponent(typeof(Collider))]
public class RunPortal : MonoBehaviour
{
    [SerializeField] private string targetScene = "scene_level";
    private bool used;

    private void OnTriggerEnter(Collider other)
    {
        if (used || !other.CompareTag("Player")) return;
        used = true;
        CameraTransitionManager.RequestGameplayEntry(targetScene); // skips a title shot if that scene has one
        SceneManager.LoadScene(targetScene);
    }
}