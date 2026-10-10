using UnityEngine;

[DefaultExecutionOrder(100)]
public class GlobalSeeThrough : MonoBehaviour
{
    public Transform player;

    [Header("Foliage")]
    public float holeSize = 0.15f;
    public float smoothness = 0.12f;

    [Header("Props (trunks, rocks, etc.)")]
    public float propHoleSize = 0.15f;
    public float propSmoothness = 0.08f;

    [Header("Behaviour")]
    public float fadeSpeed = 6f;
    public LayerMask blockerMask = ~0;
    public bool onlyWhenBlocked = false;

    private Camera cam;
    private float fade;

    void Start()
    {
        cam = Camera.main;
        if (cam == null) Debug.LogError("No camera tagged 'MainCamera' found!");
    }

    void LateUpdate()
    {
        // Always follow whatever the camera rig is following (handles spawned clones)
        if (IsoCameraRig.Instance != null && IsoCameraRig.Instance.Target != null)
            player = IsoCameraRig.Instance.Target;

        if (player == null || cam == null) return;
        // ... rest unchanged

        Vector3 targetPos = player.position + Vector3.up * 1.5f;
        Vector3 screenPos = cam.WorldToViewportPoint(targetPos);

        // Flat view direction and the player's depth along it
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        Vector2 fwdXZ = new Vector2(fwd.x, fwd.z);
        Vector2 camXZ = new Vector2(cam.transform.position.x, cam.transform.position.z);
        Vector2 playerXZ = new Vector2(player.position.x, player.position.z);

        Shader.SetGlobalVector("_SeeThroughCamForward", new Vector4(fwdXZ.x, fwdXZ.y, 0, 0));
        Shader.SetGlobalFloat("_SeeThroughPlayerDist", Vector2.Dot(playerXZ - camXZ, fwdXZ));

        bool blocked = true;
        if (onlyWhenBlocked)
        {
            blocked = Physics.Linecast(cam.transform.position, targetPos,
                                       blockerMask, QueryTriggerInteraction.Ignore);
        }

        fade = Mathf.MoveTowards(fade, blocked ? 1f : 0f, fadeSpeed * Time.deltaTime);

        Shader.SetGlobalVector("_PlayerScreenPos", new Vector4(screenPos.x, screenPos.y, 0, 0));
        Shader.SetGlobalVector("_MainCamPos", cam.transform.position);

        Shader.SetGlobalFloat("_SeeThroughHoleSize", holeSize * fade);
        Shader.SetGlobalFloat("_SeeThroughSmoothness", smoothness);
        Shader.SetGlobalFloat("_SeeThroughPropHoleSize", propHoleSize * fade);
        Shader.SetGlobalFloat("_SeeThroughPropSmoothness", propSmoothness);

        Debug.Log("PlayerDepth: " + Vector2.Dot(playerXZ - camXZ, fwdXZ));
    }
}