using UnityEngine;

/// <summary>
/// Put this on EACH chamber node (same GameObject as MapNode) and drag that chamber's power-up 3D model
/// into "Power Up Model". The model stays hidden until the node is hovered or selected, then it pops up
/// above the node, spins, floats and bobs. Moving away shrinks it again.
///
/// "Power Up Model" can be a prefab from the Project window OR an object already in the scene.
/// The model is kept OUTSIDE the node's hierarchy (re-parented next to it), so the node's own
/// hover-scaling and intro animation never distort it.
///
/// MapNodeHighlighter (on the MapController object) calls Show / Hide / Punch for you.
/// </summary>
[DisallowMultipleComponent]
public class MapNodePowerUp : MonoBehaviour
{
    [Header("Power-up model")]
    [Tooltip("The 3D model: a prefab from the Project window, or an object already in the scene.")]
    public GameObject powerUpModel;
    [Tooltip("Scale the model to match the chamber icon. Untick to keep the model's own size.")]
    public bool autoFitSize = true;
    [Tooltip("Model size compared to the chamber icon width (1 = same width).")]
    public float sizeVsNode = 1f;
    [Tooltip("Gap between the top of the icon and the bottom of the model, in model sizes.")]
    public float floatHeight = 0.35f;

    [Header("Pop animation")]
    [Tooltip("Wait after the hover starts, so the spotlight arrives first.")]
    public float showDelay = 0.12f;
    public float popDuration = 0.6f;
    public float hideDuration = 0.2f;
    [Tooltip("Degrees the model spins while popping in.")]
    public float popSpin = 360f;
    [Tooltip("How far below its spot the model starts, in model sizes.")]
    public float popRise = 0.8f;

    [Header("Idle animation")]
    [Tooltip("Degrees per second.")]
    public float spinSpeed = 45f;
    [Tooltip("Up-and-down bobbing, in model sizes.")]
    public float bobHeight = 0.08f;
    public float bobSpeed = 1.6f;
    [Tooltip("Slow lazy wobble, in degrees.")]
    public float tiltAmount = 6f;

    [Header("Look")]
    [Tooltip("Dims this model's glow (its HDR / emission colors) so it does not blow out into the bloom. 1 = unchanged. The bloom settings themselves are not touched.")]
    [Range(0f, 1f)] public float glowScale = 0.35f;

    [Header("Optional")]
    [Tooltip("Particle effect played once at the moment the model pops up.")]
    public GameObject popEffectPrefab;

    private Transform model;
    private bool ownsInstance;
    private Renderer[] nodeRenderers;

    private Vector3 modelOrigScale = Vector3.one;
    private Quaternion modelBaseRot = Quaternion.identity;
    private Vector3 modelLocalCenter;     // visual center of the model, relative to its pivot
    private float modelExtent = 1f;

    private bool created;
    private bool showing;
    private float delay;
    private float appear;                 // 0 = hidden, 1 = fully popped
    private float yaw;
    private float punch;
    private float phase;

    // ------------------------------------------------------------------ public API

    public void Show()
    {
        EnsureCreated();
        if (model == null) return;

        showing = true;
        if (appear <= 0f) delay = showDelay;
    }

    public void Hide()
    {
        showing = false;
        delay = 0f;
    }

    /// <summary>Quick scale bump and spin boost, used when the chamber is selected.</summary>
    public void Punch()
    {
        punch = 1f;
    }

    /// <summary>
    /// A fresh, untouched copy of this chamber's power-up model, used by the buff room so the buff the player
    /// picked on the map is the one waiting on the slab. The caller owns the copy and places it.
    /// </summary>
    public GameObject CreateBuffCopy()
    {
        if (powerUpModel == null) return null;
        EnsureCreated();

        GameObject copy = Instantiate(powerUpModel);
        if (powerUpModel.scene.IsValid())
        {
            // cloned from the scene object, which the map has been animating: put its look back
            copy.transform.localScale = modelOrigScale;
            copy.transform.rotation = modelBaseRot;
        }
        copy.SetActive(true);
        return copy;
    }

    public void HideInstant()
    {
        showing = false;
        delay = 0f;
        appear = 0f;
        punch = 0f;
        if (model != null) model.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ setup

    private void Start()
    {
        EnsureCreated();
    }

    private void EnsureCreated()
    {
        if (created || powerUpModel == null) return;
        created = true;
        phase = Random.value * 6.28f;

        GameObject go = powerUpModel;
        Transform parent = transform.parent;

        if (go.scene.IsValid())
        {
            // an object already in the scene: move it next to the node, keep its look
            go.transform.SetParent(parent, true);
        }
        else
        {
            // a prefab asset: spawn our own copy
            go = Instantiate(powerUpModel, parent);
            ownsInstance = true;
        }

        model = go.transform;

        // the map camera only renders the map's layer, so the model must be on the node's layer
        SetLayerRecursive(go, gameObject.layer);
        TameGlow(go);

        modelOrigScale = model.localScale;
        modelBaseRot = model.rotation;

        // cache the node's own renderers (after the re-parent, so the model is not part of them)
        nodeRenderers = GetComponentsInChildren<Renderer>();

        // measure the model once: size and visual center
        go.SetActive(true);                  // bounds are only valid while active

        bool any = false;
        Bounds b = new Bounds(model.position, Vector3.zero);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        if (any)
        {
            modelExtent = Mathf.Max(0.0001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            modelLocalCenter = Quaternion.Inverse(modelBaseRot) * (b.center - model.position);
        }
        else
        {
            modelExtent = 1f;
            modelLocalCenter = Vector3.zero;
        }

        go.SetActive(false);                 // hidden until the first hover
    }

    // ------------------------------------------------------------------ animation

    private void LateUpdate()
    {
        if (model == null) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        if (showing)
        {
            if (delay > 0f)
            {
                delay -= dt;
                if (delay > 0f) return;
            }

            if (!model.gameObject.activeSelf)
            {
                model.gameObject.SetActive(true);
                PlayPopEffect();
            }
            appear = Mathf.MoveTowards(appear, 1f, dt / Mathf.Max(0.01f, popDuration));
        }
        else
        {
            if (appear <= 0f) return;
            appear = Mathf.MoveTowards(appear, 0f, dt / Mathf.Max(0.01f, hideDuration));
            if (appear <= 0f)
            {
                model.gameObject.SetActive(false);
                return;
            }
        }

        // where is the node right now (it scales on hover)
        Bounds nb = GetNodeBounds();
        float nodeWidth = Mathf.Max(0.0001f, Mathf.Max(nb.size.x, nb.size.z));
        float size = autoFitSize ? nodeWidth * sizeVsNode : modelExtent;
        float factor = autoFitSize ? size / modelExtent : 1f;

        // pop curve (overshoots going in, smooth going out)
        float pop = showing
            ? EaseOutBack(appear)
            : appear * appear * (3f - 2f * appear);

        punch = Mathf.MoveTowards(punch, 0f, dt * 2.2f);
        pop *= 1f + 0.2f * punch;

        yaw += spinSpeed * (1f + punch * 6f) * dt;
        float popSpinNow = popSpin * (1f - EaseOutCubic(appear));

        float time = Time.unscaledTime;
        float tiltX = Mathf.Sin(time * 0.9f + phase) * tiltAmount;
        float tiltZ = Mathf.Cos(time * 0.7f + phase) * tiltAmount;

        Quaternion rot = Quaternion.AngleAxis(yaw + popSpinNow, Vector3.up)
                       * Quaternion.Euler(tiltX, 0f, tiltZ)
                       * modelBaseRot;

        float rise = (1f - (showing ? EaseOutCubic(appear) : appear)) * popRise * size;
        float bob = Mathf.Sin(time * bobSpeed + phase) * bobHeight * size * appear;

        // visual center of the model sits above the top of the node
        Vector3 targetCenter = new Vector3(
            nb.center.x,
            nb.max.y + size * (floatHeight + 0.5f) + rise + bob,
            nb.center.z);

        float scaleRatio = factor * pop;
        Vector3 pivot = targetCenter - rot * (modelLocalCenter * scaleRatio);

        model.position = pivot;
        model.rotation = rot;
        model.localScale = modelOrigScale * Mathf.Max(0.0001f, scaleRatio);
    }

    private Bounds GetNodeBounds()
    {
        bool any = false;
        Bounds b = new Bounds(transform.position, Vector3.zero);

        if (nodeRenderers != null)
        {
            foreach (Renderer r in nodeRenderers)
            {
                if (r == null) continue;
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
        }

        if (!any) b = new Bounds(transform.position, Vector3.one * Mathf.Max(0.01f, transform.lossyScale.x));
        return b;
    }

    private void PlayPopEffect()
    {
        if (popEffectPrefab == null) return;
        Bounds nb = GetNodeBounds();
        GameObject fx = Instantiate(popEffectPrefab, new Vector3(nb.center.x, nb.max.y, nb.center.z), Quaternion.identity);
        SetLayerRecursive(fx, gameObject.layer);
        Destroy(fx, 4f);
    }

    // Scales down every HDR color (emission, rim, glow...) on the model. Only this model is affected,
    // through per-renderer property blocks, so the shared materials and the bloom are left alone.
    private void TameGlow(GameObject go)
    {
        if (glowScale >= 0.999f) return;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat == null || mat.shader == null) continue;
                Shader sh = mat.shader;

                MaterialPropertyBlock block = null;
                int count = sh.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                    if ((sh.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HDR) == 0) continue;

                    string propName = sh.GetPropertyName(i);
                    if (!mat.HasProperty(propName)) continue;

                    if (block == null)
                    {
                        block = new MaterialPropertyBlock();
                        r.GetPropertyBlock(block, m);
                    }
                    Color c = mat.GetColor(propName);
                    block.SetColor(propName, new Color(c.r * glowScale, c.g * glowScale, c.b * glowScale, c.a));
                }
                if (block != null) r.SetPropertyBlock(block, m);
            }
        }
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private void OnDestroy()
    {
        if (ownsInstance && model != null) Destroy(model.gameObject);
    }

    // ------------------------------------------------------------------ easing

    private static float EaseOutCubic(float p)
    {
        p = 1f - p;
        return 1f - p * p * p;
    }

    private static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}