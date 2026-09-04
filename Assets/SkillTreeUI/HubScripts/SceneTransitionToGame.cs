using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionToGame : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Load the "Game" scene when the player enters the trigger
            SceneManager.LoadScene("AK_LevelDesign");
        }
    }
}
