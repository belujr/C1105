using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A flat quad glued in front of the camera (flash, vignette, darkness, blackout). Used by PlayerDeathFX.
/// The render queue decides what it covers: below 3000 it covers the world but not the glowing effects,
/// above 4000 it covers absolutely everything.
/// </summary>
public class DeathCamQuad
{
    private static Mesh sharedMesh;
    private Transform tf;
    private MeshRenderer mr;
    private Material mat;

    public static DeathCamQuad Create(string name, int queue, Texture2D tex)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) return new DeathCamQuad();

        if (sharedMesh == null)
        {
            sharedMesh = new Mesh();
            sharedMesh.name = "DeathCamQuad";
            sharedMesh.hideFlags = HideFlags.HideAndDontSave;
            sharedMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
            };
            sharedMesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            sharedMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            sharedMesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
        }

        DeathCamQuad q = new DeathCamQuad();
        GameObject go = new GameObject(name);
        q.tf = go.transform;
        go.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
        q.mr = go.AddComponent<MeshRenderer>();
        q.mat = new Material(sh);
        if (tex != null) q.mat.mainTexture = tex;
        q.mat.color = new Color(0f, 0f, 0f, 0f);
        q.mat.renderQueue = queue;
        q.mr.sharedMaterial = q.mat;
        q.mr.shadowCastingMode = ShadowCastingMode.Off;
        q.mr.receiveShadows = false;
        q.mr.enabled = false;
        return q;
    }

    /// <param name="scale">1 = exactly the screen. Above 1 the quad is bigger than the screen (so camera shake never shows an edge).</param>
    public void Set(Camera cam, Color color, float scale)
    {
        if (mr == null) return;
        if (color.a <= 0.001f)
        {
            if (mr.enabled) mr.enabled = false;
            return;
        }

        if (tf.parent != cam.transform) tf.SetParent(cam.transform, false);

        float d = Mathf.Max(cam.nearClipPlane + 0.5f, 1f);
        float h = cam.orthographic
            ? cam.orthographicSize * 2f
            : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * cam.aspect;

        tf.localPosition = new Vector3(0f, 0f, d);
        tf.localRotation = Quaternion.identity;
        tf.localScale = new Vector3(w * scale, h * scale, 1f);

        mat.color = color;
        mr.enabled = true;
    }

    public void Hide()
    {
        if (mr != null && mr.enabled) mr.enabled = false;
    }

    public void Dispose()
    {
        if (mat != null) Object.Destroy(mat);
        if (tf != null) Object.Destroy(tf.gameObject);
    }
}
