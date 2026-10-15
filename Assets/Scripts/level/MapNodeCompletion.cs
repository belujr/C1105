using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Greys out a chamber node once it is completed.
///
/// You do NOT have to add this by hand: MapController adds it to a node the first time that node is
/// completed. Add it yourself only if you want to assign a "Completed Material".
///
/// Without a Completed Material, every color on the node's materials fades to a dark grey (done with
/// per-renderer property blocks, so the shared materials are not changed). Emission and glow colors
/// fade to black.
/// </summary>
[DisallowMultipleComponent]
public class MapNodeCompletion : MonoBehaviour
{
    [Tooltip("Optional. A material swapped in when the chamber is completed (for example a grey stone material). If empty, the node's own colors fade to grey.")]
    public Material completedMaterial;
    [Tooltip("Brightness of the grey (0 = black, 1 = same brightness as the original colors).")]
    [Range(0f, 1f)] public float greyBrightness = 0.4f;
    [Tooltip("Seconds the colors take to fade to grey.")]
    public float fadeTime = 0.9f;

    public bool IsCompleted { get; private set; }

    private class PropFade
    {
        public string name;
        public Color from;
        public Color to;
    }

    private class Slot
    {
        public Renderer renderer;
        public int index;
        public readonly List<PropFade> props = new List<PropFade>();
    }

    // created on first use: Unity does not allow creating one inside a field initializer of a MonoBehaviour
    private static MaterialPropertyBlock block;

    private readonly List<Slot> slots = new List<Slot>();
    private readonly Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();

    private bool captured;
    private float amount;     // 0 = normal, 1 = grey
    private float target;

    // ------------------------------------------------------------------ public API

    public void SetCompleted(bool completed, bool animate = true)
    {
        Capture();
        IsCompleted = completed;
        target = completed ? 1f : 0f;

        if (completedMaterial != null)
        {
            // swap to the grey material (no fade) and drop any color overrides
            SwapMaterials(completed);
            foreach (Slot s in slots)
                if (s.renderer != null) s.renderer.SetPropertyBlock(null, s.index);
            amount = target;
            return;
        }

        // going back to normal after a material swap
        if (!completed) SwapMaterials(false);

        if (!animate || fadeTime <= 0.01f)
        {
            amount = target;
            Apply();
        }
        // otherwise Update() fades it
    }

    // ------------------------------------------------------------------ internals

    private void Update()
    {
        if (!captured || Mathf.Approximately(amount, target)) return;

        amount = Mathf.MoveTowards(amount, target,
            Mathf.Min(Time.unscaledDeltaTime, 0.05f) / Mathf.Max(0.01f, fadeTime));
        Apply();
    }

    private void Capture()
    {
        if (captured) return;
        captured = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            Material[] mats = r.sharedMaterials;
            originalMaterials[r] = mats;

            for (int i = 0; i < mats.Length; i++)
            {
                Material mat = mats[i];
                if (mat == null || mat.shader == null) continue;
                Shader sh = mat.shader;

                Slot slot = new Slot { renderer = r, index = i };
                int count = sh.GetPropertyCount();

                for (int p = 0; p < count; p++)
                {
                    if (sh.GetPropertyType(p) != ShaderPropertyType.Color) continue;

                    string propName = sh.GetPropertyName(p);
                    if (!mat.HasProperty(propName)) continue;

                    Color c = mat.GetColor(propName);
                    bool hdr = (sh.GetPropertyFlags(p) & ShaderPropertyFlags.HDR) != 0;

                    float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    float grey = hdr ? 0f : lum * greyBrightness;   // glow colors go out completely

                    slot.props.Add(new PropFade
                    {
                        name = propName,
                        from = c,
                        to = new Color(grey, grey, grey, c.a)
                    });
                }

                if (slot.props.Count > 0) slots.Add(slot);
            }
        }
    }

    private void Apply()
    {
        if (block == null) block = new MaterialPropertyBlock();

        float e = amount * amount * (3f - 2f * amount);   // smoothstep

        foreach (Slot s in slots)
        {
            if (s.renderer == null) continue;

            s.renderer.GetPropertyBlock(block, s.index);
            foreach (PropFade f in s.props)
                block.SetColor(f.name, Color.Lerp(f.from, f.to, e));
            s.renderer.SetPropertyBlock(block, s.index);
        }
    }

    private void SwapMaterials(bool completed)
    {
        if (completed && completedMaterial == null) return;

        foreach (KeyValuePair<Renderer, Material[]> kv in originalMaterials)
        {
            Renderer r = kv.Key;
            if (r == null) continue;

            if (completed)
            {
                Material[] swapped = new Material[kv.Value.Length];
                for (int i = 0; i < swapped.Length; i++) swapped[i] = completedMaterial;
                r.sharedMaterials = swapped;
            }
            else
            {
                r.sharedMaterials = kv.Value;
            }
        }
    }
}