using UnityEngine;

[RequireComponent(typeof(MinionEnemyBrain))]
public class EyeAnchorVisualizer : MonoBehaviour
{
    [Header("Custom Material Slots")]
    [Tooltip("Assign your own soft/transparent material here. If left empty, a default fallback material is generated.")]
    public Material customConeMaterial;
    [Tooltip("Assign your custom pulse ring material here. If left empty, a default fallback material is generated.")]
    public Material customPulseMaterial;

    [Header("Vision Cone Colors (Alpha = Opacity)")]
    public Color normalConeColor = new Color(1f, 0f, 0f, 0.12f); // 12% Opacity
    public Color alertConeColor = new Color(1f, 0f, 0f, 0.35f);  // 35% Opacity

    [Header("Hearing Cue Settings")]
    public Color hearingPulseColor = new Color(1f, 0.85f, 0f, 0.4f); // 40% Opacity
    public float pulseDuration = 0.5f;

    private MinionEnemyBrain brain;
    private MeshFilter coneFilter;
    private MeshRenderer coneRenderer;
    private Material coneMatInstance;

    private GameObject ringObj;
    private LineRenderer ringRenderer;
    private Material pulseMatInstance;
    private float pulseTimer;

    private string colorPropName = "_Color";

    private void Awake()
    {
        brain = GetComponent<MinionEnemyBrain>();
        SetupVisionCone();
        SetupHearingRing();
    }

    private void SetupVisionCone()
    {
        GameObject coneObj = new GameObject("VisionConeVisualizer");
        Transform parentAnchor = brain.eyeAnchor != null ? brain.eyeAnchor : transform;
        coneObj.transform.SetParent(parentAnchor, false);

        if (brain.eyeAnchor == null)
            coneObj.transform.localPosition = Vector3.up * 1.5f;

        coneFilter = coneObj.AddComponent<MeshFilter>();
        coneRenderer = coneObj.AddComponent<MeshRenderer>();

        // Use custom material if assigned, otherwise fallback to Unlit/Transparent shader
        if (customConeMaterial != null)
        {
            coneMatInstance = new Material(customConeMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? 
                           Shader.Find("Sprites/Default") ?? 
                           Shader.Find("Unlit/Transparent");
            coneMatInstance = new Material(shader);
        }

        // Detect if shader uses _BaseColor (URP) or _Color (Built-in/Sprites)
        colorPropName = coneMatInstance.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";

        SetMaterialColor(coneMatInstance, normalConeColor);
        coneRenderer.sharedMaterial = coneMatInstance;

        BuildConeMesh();
    }

    private void BuildConeMesh()
    {
        Mesh mesh = new Mesh();
        int segments = 24;

        float halfFovDeg = Mathf.Acos(Mathf.Clamp(brain.visionFOVThreshold, -1f, 1f)) * Mathf.Rad2Deg;
        float range = brain.visionRange;

        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];

        vertices[0] = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Lerp(-halfFovDeg, halfFovDeg, (float)i / segments);
            Quaternion rot = Quaternion.Euler(0f, angle, 0f);
            vertices[i + 1] = rot * Vector3.forward * range;

            if (i < segments)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        coneFilter.mesh = mesh;
    }

    private void SetupHearingRing()
    {
        ringObj = new GameObject("HearingPulseRing");
        ringObj.transform.SetParent(transform, false);
        ringObj.transform.localPosition = Vector3.up * 0.1f;

        ringRenderer = ringObj.AddComponent<LineRenderer>();
        ringRenderer.useWorldSpace = false;
        ringRenderer.loop = true;
        ringRenderer.positionCount = 32;
        ringRenderer.startWidth = 0.1f;
        ringRenderer.endWidth = 0.1f;

        if (customPulseMaterial != null)
        {
            pulseMatInstance = new Material(customPulseMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            pulseMatInstance = new Material(shader);
        }

        SetMaterialColor(pulseMatInstance, hearingPulseColor);
        ringRenderer.material = pulseMatInstance;

        for (int i = 0; i < 32; i++)
        {
            float rad = (i / 32f) * Mathf.PI * 2f;
            ringRenderer.SetPosition(i, new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * brain.hearingRadius);
        }

        ringObj.SetActive(false);
    }

    private void Update()
    {
        if (brain == null) return;

        // 1. Vision Cone state color & transparency
        bool isAlert = brain.CurrentState == MinionEnemyBrain.MinionState.Chase || 
                       brain.CurrentState == MinionEnemyBrain.MinionState.Engage;
        
        SetMaterialColor(coneMatInstance, isAlert ? alertConeColor : normalConeColor);

        // 2. Pulse hearing cue
        if (brain.CurrentState == MinionEnemyBrain.MinionState.Suspicious || 
            brain.CurrentState == MinionEnemyBrain.MinionState.Search)
        {
            TriggerHearingPulse();
        }

        if (pulseTimer > 0f)
        {
            pulseTimer -= Time.deltaTime;
            float t = 1f - (pulseTimer / pulseDuration);
            ringObj.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, t);

            if (pulseTimer <= 0f) 
                ringObj.SetActive(false);
        }
    }

    public void TriggerHearingPulse()
    {
        if (pulseTimer <= 0f)
        {
            pulseTimer = pulseDuration;
            ringObj.SetActive(true);
        }
    }

    private void SetMaterialColor(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty(colorPropName))
            mat.SetColor(colorPropName, color);
        else
            mat.color = color;
    }

    private void OnDestroy()
    {
        // Clean up instantiated material instances to avoid memory leaks
        if (coneMatInstance != null) Destroy(coneMatInstance);
        if (pulseMatInstance != null) Destroy(pulseMatInstance);
    }
}