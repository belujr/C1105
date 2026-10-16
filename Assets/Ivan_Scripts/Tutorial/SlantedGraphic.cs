using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shears any Image into a parallelogram (the "slant" look) without needing a special sprite.
/// Works on plain white Images and on sliced sprites.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class SlantedGraphic : BaseMeshEffect
{
    [Tooltip("Horizontal shear in pixels. Positive: top leans right ( / ). Negative: top leans left ( \\ ).")]
    [SerializeField] private float slant = 28f;

    public float Slant
    {
        get => slant;
        set
        {
            slant = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;

        Rect rect = graphic.rectTransform.rect;
        if (rect.height < 0.001f) return;

        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            float t = (v.position.y - rect.center.y) / rect.height; // -0.5 (bottom) .. +0.5 (top)
            v.position.x += t * slant;
            vh.SetUIVertex(v, i);
        }
    }
}