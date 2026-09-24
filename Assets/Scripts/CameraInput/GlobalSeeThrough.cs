using UnityEngine;

public class GlobalSeeThrough : MonoBehaviour
{
    public Transform player;
    public Material treeMaterial; // Drag your Fluffy Tree Material here in the Inspector
    private Camera cam;

    void Start()
    {
        cam = Camera.main;

        // This will print a red error in your console if the tag is missing
        if (cam == null)
        {
            Debug.LogError("SeeThrough Script: No camera tagged 'MainCamera' was found!");
        }
    }
    void Update()
    {
        if (player == null || cam == null) return;

        Vector3 targetPos = player.position + Vector3.up * 1.5f;
        Vector3 screenPos = cam.WorldToViewportPoint(targetPos);

        // Replace the treeMaterial line with this global command:
        // This broadcasts the coordinates to EVERY shader in the game simultaneously
        Shader.SetGlobalVector("_PlayerScreenPos", new Vector4(screenPos.x, screenPos.y, 0, 0));
    }
}