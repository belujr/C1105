#pragma warning disable 0618, 0672
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct FlowerSettings
{
    public Mesh mesh;
    public float yOffset;
    public Material[] materials; // Array to hold Petal/Stem materials
}
[ExecuteAlways]
public class InfiniteGrassRenderer : MonoBehaviour
{
    [HideInInspector] public static InfiniteGrassRenderer instance;

    [Header("Internal Grass")]
    public Material grassMaterial;
    public ComputeBuffer argsBuffer;
    public ComputeBuffer tBuffer;

    [Header("Grass Properties")]
    public float spacing = 0.5f;
    public float drawDistance = 300;
    public float fullDensityDistance = 50;
    public int grassMeshSubdivision = 5;
    public float textureUpdateThreshold = 10.0f;

    [Header("Max Buffer Count (Millions)")]
    public float maxBufferCount = 2;

    [Header("Flower Garden (Optional Add-on)")]
    public Material flowerMaterial;
    public FlowerSettings[] flowerSettings;
    [HideInInspector] public List<ComputeBuffer> flowerArgsBuffers = new List<ComputeBuffer>();

    [Header("Flower Properties")]
    public float flowerSpacing = 1.0f;
    public float flowerDrawDistance = 150f;
    public float flowerFullDensityDistance = 30f;

    [Header("Debug")]
    public bool previewVisibleGrassCount = false;

    private Mesh cachedGrassMesh;
    private int oldSubdivision = -1;
    private uint[] args = new uint[5];
    private Camera mainCam;

    private static readonly int CenterPosID = Shader.PropertyToID("_CenterPos");
    private static readonly int DrawDistanceID = Shader.PropertyToID("_DrawDistance");
    private static readonly int MaxDrawDistanceID = Shader.PropertyToID("_MaxDrawDistance");
    private static readonly int TextureUpdateThresholdID = Shader.PropertyToID("_TextureUpdateThreshold");
    private static readonly int GrassPositionsID = Shader.PropertyToID("_GrassPositions");
    private static readonly int FlowerIndexID = Shader.PropertyToID("_FlowerIndex");
    private static readonly int FlowerCountID = Shader.PropertyToID("_FlowerCount");
    private static readonly int FlowerYOffsetID = Shader.PropertyToID("_FlowerYOffset");

    private void OnEnable() => instance = this;

    private void OnDisable()
    {
        instance = null;
        argsBuffer?.Release();
        tBuffer?.Release();
        argsBuffer = null;
        tBuffer = null;
        if (flowerArgsBuffers != null)
        {
            foreach (var buf in flowerArgsBuffers) buf?.Release();
            flowerArgsBuffers = null;
        }
    }

    public float GetMaxDrawDistance()
    {
        return (flowerSettings != null && flowerSettings.Length > 0) ? Mathf.Max(drawDistance, flowerDrawDistance) : drawDistance;
    }

    void LateUpdate()
    {
        if (spacing == 0) return;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        Bounds cameraBounds = CalculateCameraBounds(mainCam);
        Vector2 centerPos = new Vector2(Mathf.Floor(mainCam.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, Mathf.Floor(mainCam.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold);
        float maxDraw = GetMaxDrawDistance();

        if (tBuffer == null) tBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

        if (grassMaterial != null)
        {
            if (argsBuffer == null) argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            Mesh currentMesh = GetGrassMeshCache();
            args[0] = (uint)currentMesh.GetIndexCount(0);
            args[1] = (uint)(maxBufferCount * 1000000);
            args[2] = (uint)currentMesh.GetIndexStart(0);
            args[3] = (uint)currentMesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuffer.SetData(args);

            grassMaterial.SetVector(CenterPosID, centerPos);
            grassMaterial.SetFloat(DrawDistanceID, maxDraw);
            grassMaterial.SetFloat(MaxDrawDistanceID, maxDraw);
            grassMaterial.SetFloat(TextureUpdateThresholdID, textureUpdateThreshold);

            Graphics.DrawMeshInstancedIndirect(currentMesh, 0, grassMaterial, cameraBounds, argsBuffer);
        }

        if (flowerSettings != null && flowerSettings.Length > 0)
        {
            // 1. Calculate total submeshes across all flowers
            int totalSubmeshes = 0;
            for (int i = 0; i < flowerSettings.Length; i++)
            {
                if (flowerSettings[i].mesh != null) totalSubmeshes += flowerSettings[i].mesh.subMeshCount;
            }

            // 2. Reallocate list if necessary
            if (flowerArgsBuffers == null || flowerArgsBuffers.Count != totalSubmeshes)
            {
                if (flowerArgsBuffers != null) foreach (var buf in flowerArgsBuffers) buf?.Release();
                flowerArgsBuffers = new List<ComputeBuffer>();
                for (int i = 0; i < totalSubmeshes; i++)
                    flowerArgsBuffers.Add(new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments));
            }

            // 3. Draw each submesh
            int bufferIndex = 0;
            for (int i = 0; i < flowerSettings.Length; i++)
            {
                if (flowerSettings[i].mesh == null) continue;

                for (int submesh = 0; submesh < flowerSettings[i].mesh.subMeshCount; submesh++)
                {
                    args[0] = (uint)flowerSettings[i].mesh.GetIndexCount(submesh);
                    args[1] = (uint)(maxBufferCount * 1000000);
                    args[2] = (uint)flowerSettings[i].mesh.GetIndexStart(submesh);
                    args[3] = (uint)flowerSettings[i].mesh.GetBaseVertex(submesh);
                    args[4] = 0;

                    ComputeBuffer currentBuffer = flowerArgsBuffers[bufferIndex];
                    currentBuffer.SetData(args);

                    // Use assigned material, or fallback to global flowerMaterial
                    Material matToUse = flowerMaterial;
                    if (flowerSettings[i].materials != null && submesh < flowerSettings[i].materials.Length && flowerSettings[i].materials[submesh] != null)
                    {
                        matToUse = flowerSettings[i].materials[submesh];
                    }

                    if (matToUse != null)
                    {
                        matToUse.SetInt(FlowerIndexID, i);
                        matToUse.SetInt(FlowerCountID, flowerSettings.Length);
                        matToUse.SetFloat(FlowerYOffsetID, flowerSettings[i].yOffset);
                        matToUse.SetVector(CenterPosID, centerPos);
                        matToUse.SetFloat(DrawDistanceID, maxDraw);
                        matToUse.SetFloat(TextureUpdateThresholdID, textureUpdateThreshold);

                        Graphics.DrawMeshInstancedIndirect(flowerSettings[i].mesh, submesh, matToUse, cameraBounds, currentBuffer);
                    }
                    bufferIndex++;
                }
            }
        }
    }

    private void OnGUI()
    {
        if (previewVisibleGrassCount && tBuffer != null)
        {
            GUI.contentColor = Color.black;
            GUIStyle style = new GUIStyle(); style.fontSize = 25;
            uint[] count = new uint[1]; tBuffer.GetData(count);
            Bounds cameraBounds = CalculateCameraBounds(mainCam != null ? mainCam : Camera.main);
            Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));
            GUI.Label(new Rect(50, 50, 400, 200), "Dispatch Size : " + gridSize.x + "x" + gridSize.y + " = " + (gridSize.x * gridSize.y), style);
            GUI.Label(new Rect(50, 80, 400, 200), "Visible Instance Count : " + count[0], style);
        }
    }

    public Mesh GetGrassMeshCache()
    {
        if (!cachedGrassMesh || oldSubdivision != grassMeshSubdivision)
        {
            cachedGrassMesh = new Mesh();
            Vector3[] vertices = new Vector3[3 + 4 * grassMeshSubdivision];
            int[] triangles = new int[(1 + 2 * grassMeshSubdivision) * 3];

            for (int i = 0; i < grassMeshSubdivision; i++)
            {
                float y1 = (float)i / (grassMeshSubdivision + 1); float y2 = (float)(i + 1) / (grassMeshSubdivision + 1);
                int bL = i * 4; int bR = i * 4 + 1; int tL = i * 4 + 2; int tR = i * 4 + 3;
                vertices[bL] = new Vector3(-0.25f, y1); vertices[bR] = new Vector3(0.25f, y1);
                vertices[tL] = new Vector3(-0.25f, y2); vertices[tR] = new Vector3(0.25f, y2);
                triangles[i * 6] = bL; triangles[i * 6 + 1] = tR; triangles[i * 6 + 2] = bR;
                triangles[i * 6 + 3] = bL; triangles[i * 6 + 4] = tL; triangles[i * 6 + 5] = tR;
            }
            vertices[grassMeshSubdivision * 4] = new Vector3(-0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));
            vertices[grassMeshSubdivision * 4 + 1] = new Vector3(0, 1);
            vertices[grassMeshSubdivision * 4 + 2] = new Vector3(0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));
            triangles[grassMeshSubdivision * 6] = grassMeshSubdivision * 4;
            triangles[grassMeshSubdivision * 6 + 1] = grassMeshSubdivision * 4 + 1;
            triangles[grassMeshSubdivision * 6 + 2] = grassMeshSubdivision * 4 + 2;
            cachedGrassMesh.SetVertices(vertices); cachedGrassMesh.SetTriangles(triangles, 0); cachedGrassMesh.RecalculateNormals();
            oldSubdivision = grassMeshSubdivision;
        }
        return cachedGrassMesh;
    }

    Bounds CalculateCameraBounds(Camera camera)
    {
        float activeDrawDist = GetMaxDrawDistance();
        Vector3 ntopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));
        Vector3 ntopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
        Vector3 nbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
        Vector3 nbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));
        Vector3 ftopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, activeDrawDist));
        Vector3 ftopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, activeDrawDist));
        Vector3 fbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, activeDrawDist));
        Vector3 fbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, activeDrawDist));

        float startX = Mathf.Max(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);
        float endX = Mathf.Min(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);
        float startY = Mathf.Max(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);
        float endY = Mathf.Min(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);
        float startZ = Mathf.Max(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);
        float endZ = Mathf.Min(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);

        Bounds bounds = new Bounds(new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2), new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ)));
        bounds.Expand(1);
        return bounds;
    }
}