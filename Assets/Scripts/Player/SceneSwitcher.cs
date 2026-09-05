using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public class SceneSwitcher : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("The exact name of the scene to load.")]
    public string targetSceneName;

    [Header("Input Settings")]
    [Tooltip("Drag your Input Action Asset reference here.")]
    public InputActionReference switchSceneAction;

    private void OnEnable()
    {
        if (switchSceneAction != null)
        {
            switchSceneAction.action.Enable();
            switchSceneAction.action.performed += OnSwitchScenePressed;
        }
    }

    private void OnDisable()
    {
        if (switchSceneAction != null)
        {
            switchSceneAction.action.performed -= OnSwitchScenePressed;
            switchSceneAction.action.Disable();
        }
    }

    private void OnSwitchScenePressed(InputAction.CallbackContext context)
    {
        if (!string.IsNullOrEmpty(targetSceneName))
        {
            SceneManager.LoadScene(targetSceneName);
        }
        else
        {
            Debug.LogWarning("Target Scene Name is missing in the SceneSwitcher script!");
        }
    }
}