using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Moves and zooms the camera for the death (hit kick, tremble, zoom punch). The offset is applied just before the camera
/// renders and removed right after, so it never fights your camera script. Needs the URP render pipeline.
/// Used by PlayerDeathFX.
/// </summary>
public class DeathCameraKick
{
    private Camera cam;
    private Vector3 offset;
    private float zoomMul = 1f;
    private bool applied;
    private Vector3 savedPos;
    private float savedSize, savedFov;

    public void Enable()
    {
        RenderPipelineManager.beginCameraRendering += OnBegin;
        RenderPipelineManager.endCameraRendering += OnEnd;
    }

    public void Disable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBegin;
        RenderPipelineManager.endCameraRendering -= OnEnd;
        Clear();
    }

    /// <param name="worldOffset">Camera offset in world units.</param>
    /// <param name="zoom">Field of view / size multiplier (below 1 = zoomed in).</param>
    public void Set(Camera camera, Vector3 worldOffset, float zoom)
    {
        cam = camera;
        offset = worldOffset;
        zoomMul = zoom;
    }

    public void Clear()
    {
        offset = Vector3.zero;
        zoomMul = 1f;
    }

    private void OnBegin(ScriptableRenderContext context, Camera c)
    {
        applied = false;
        if (c == null || c != cam) return;
        if (offset == Vector3.zero && Mathf.Approximately(zoomMul, 1f)) return;

        applied = true;
        savedPos = c.transform.position;
        savedSize = c.orthographicSize;
        savedFov = c.fieldOfView;

        c.transform.position = savedPos + offset;
        if (c.orthographic) c.orthographicSize = savedSize * zoomMul;
        else c.fieldOfView = savedFov * zoomMul;
    }

    private void OnEnd(ScriptableRenderContext context, Camera c)
    {
        if (!applied || c == null || c != cam) return;
        applied = false;

        c.transform.position = savedPos;
        if (c.orthographic) c.orthographicSize = savedSize;
        else c.fieldOfView = savedFov;
    }
}
