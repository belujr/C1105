#pragma warning disable 0618, 0672
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class GrassDataRendererFeature : ScriptableRendererFeature
{
    [Header("Spawning Layers")]
    [SerializeField] private LayerMask mixedLayer;
    [SerializeField] private LayerMask grassOnlyLayer;
    [SerializeField] private LayerMask flowerOnlyLayer;

    [Header("Dependencies")]
    [SerializeField] private Material heightMapMat;
    [SerializeField] private ComputeShader computeShader;

    GrassDataPass grassDataPass;

    public override void Create()
    {
        grassDataPass = new GrassDataPass(mixedLayer, grassOnlyLayer, flowerOnlyLayer, heightMapMat, computeShader);
        grassDataPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(grassDataPass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) grassDataPass?.Dispose();
    }

    private class GrassDataPass : ScriptableRenderPass
    {
        private List<ShaderTagId> shaderTagsList = new List<ShaderTagId>();

        private RTHandle heightRT, heightDepthRT, maskRT, flowerMaskRT, colorRT, slopeRT;

        private LayerMask mixedLayer, grassLayer, flowerLayer;
        private Material heightMapMat;
        private ComputeShader computeShader;

        private ComputeBuffer grassPositionsBuffer, flowerPositionsBuffer;
        private const int TextureSize = 2048;

        private static readonly int MaxDrawDistanceID = Shader.PropertyToID("_MaxDrawDistance");
        private static readonly int BoundsYMinMaxID = Shader.PropertyToID("_BoundsYMinMax");
        private static readonly int SpawnAllowedID = Shader.PropertyToID("_SpawnAllowed");
        private static readonly int GrassColorRT_ID = Shader.PropertyToID("_GrassColorRT");
        private static readonly int GrassSlopeRT_ID = Shader.PropertyToID("_GrassSlopeRT");
        private static readonly int VPMatrixID = Shader.PropertyToID("_VPMatrix");
        private static readonly int FullDensityDistanceID = Shader.PropertyToID("_FullDensityDistance");
        private static readonly int BoundsMinID = Shader.PropertyToID("_BoundsMin");
        private static readonly int BoundsMaxID = Shader.PropertyToID("_BoundsMax");
        private static readonly int CameraPositionID = Shader.PropertyToID("_CameraPosition");
        private static readonly int CenterPosID = Shader.PropertyToID("_CenterPos");
        private static readonly int DrawDistanceID = Shader.PropertyToID("_DrawDistance");
        private static readonly int TextureUpdateThresholdID = Shader.PropertyToID("_TextureUpdateThreshold");
        private static readonly int SpacingID = Shader.PropertyToID("_Spacing");
        private static readonly int GridStartIndexID = Shader.PropertyToID("_GridStartIndex");
        private static readonly int GridSizeID = Shader.PropertyToID("_GridSize");
        private static readonly int CameraForwardID = Shader.PropertyToID("_CameraForward");

        private static readonly int GrassPositionsID = Shader.PropertyToID("_GrassPositions");
        private static readonly int FlowerPositionsID = Shader.PropertyToID("_FlowerPositions");
        private static readonly int GrassHeightMapRT_ID = Shader.PropertyToID("_GrassHeightMapRT");
        private static readonly int GrassMaskMapRT_ID = Shader.PropertyToID("_GrassMaskMapRT");
        private static readonly int FlowerMaskMapRT_ID = Shader.PropertyToID("_FlowerMaskMapRT");

        public GrassDataPass(LayerMask mixed, LayerMask grass, LayerMask flower, Material heightMapMat, ComputeShader computeShader)
        {
            this.mixedLayer = mixed;
            this.grassLayer = grass;
            this.flowerLayer = flower;
            this.computeShader = computeShader;
            this.heightMapMat = heightMapMat;

            shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
            shaderTagsList.Add(new ShaderTagId("UniversalForward"));
            shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));
        }

        private void AllocateRTs()
        {
            // Height RT is now ARGBFloat to hold both Grass and Flower mapping
            RenderingUtils.ReAllocateIfNeeded(ref heightRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Point, name: "GrassHeightRT");
            RenderingUtils.ReAllocateIfNeeded(ref heightDepthRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.Depth, 32), FilterMode.Point, name: "GrassHeightDepthRT");
            RenderingUtils.ReAllocateIfNeeded(ref maskRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.RFloat, 0), FilterMode.Bilinear, name: "GrassMaskRT");
            RenderingUtils.ReAllocateIfNeeded(ref flowerMaskRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.RFloat, 0), FilterMode.Bilinear, name: "FlowerMaskRT");
            RenderingUtils.ReAllocateIfNeeded(ref colorRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Bilinear, name: "GrassColorRT");
            RenderingUtils.ReAllocateIfNeeded(ref slopeRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.ARGBFloat, 0), FilterMode.Bilinear, name: "GrassSlopeRT");
        }

        private void AllocateComputeBuffer()
        {
            if (InfiniteGrassRenderer.instance == null) return;
            int bufferSize = (int)(1000000 * InfiniteGrassRenderer.instance.maxBufferCount);

            if (grassPositionsBuffer == null || grassPositionsBuffer.count != bufferSize)
            {
                grassPositionsBuffer?.Release();
                grassPositionsBuffer = new ComputeBuffer(bufferSize, sizeof(float) * 3, ComputeBufferType.Append);
            }

            if (flowerPositionsBuffer == null || flowerPositionsBuffer.count != bufferSize)
            {
                flowerPositionsBuffer?.Release();
                flowerPositionsBuffer = new ComputeBuffer(bufferSize, sizeof(float) * 3, ComputeBufferType.Append);
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            AllocateRTs();
            ConfigureTarget(heightRT, heightDepthRT);
            ConfigureClear(ClearFlag.All, Color.clear);
        }

        private class PassData
        {
            public RendererListHandle mixedRL, grassRL, flowerRL, maskRL, flowerMaskRL, colorRL, slopeRL;
            public RTHandle heightRT, heightDepthRT, maskRT, flowerMaskRT, colorRT, slopeRT;
            public ComputeShader computeShader;
            public ComputeBuffer grassPositionsBuffer, flowerPositionsBuffer;
            public Camera camera;
            public Bounds cameraBounds;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (InfiniteGrassRenderer.instance == null || heightMapMat == null || computeShader == null) return;

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            AllocateRTs();
            AllocateComputeBuffer();

            float drawDist = InfiniteGrassRenderer.instance.GetMaxDrawDistance();

            Bounds cameraBounds = CalculateCameraBounds(cameraData.camera, drawDist);

            SortingSettings heightSorting = new SortingSettings(cameraData.camera) { criteria = cameraData.defaultOpaqueSortFlags };
            DrawingSettings heightDraw = new DrawingSettings(shaderTagsList[0], heightSorting);
            for (int i = 1; i < shaderTagsList.Count; i++) heightDraw.SetShaderPassName(i, shaderTagsList[i]);

            heightMapMat.SetVector(BoundsYMinMaxID, new Vector2(cameraBounds.min.y, cameraBounds.max.y));
            heightDraw.overrideMaterial = heightMapMat;

            // Create separate drawing lists for the 3 ground layers
            FilteringSettings mixedFilter = new FilteringSettings(RenderQueueRange.all, mixedLayer);
            RendererListHandle mixedRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, heightDraw, mixedFilter));

            FilteringSettings grassFilter = new FilteringSettings(RenderQueueRange.all, grassLayer);
            RendererListHandle grassRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, heightDraw, grassFilter));

            FilteringSettings flowerFilter = new FilteringSettings(RenderQueueRange.all, flowerLayer);
            RendererListHandle flowerRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, heightDraw, flowerFilter));

            SortingSettings transparentSorting = new SortingSettings(cameraData.camera) { criteria = SortingCriteria.CommonTransparent };
            FilteringSettings allFilter = new FilteringSettings(RenderQueueRange.all);

            DrawingSettings maskDraw = new DrawingSettings(new ShaderTagId("GrassMask"), transparentSorting);
            RendererListHandle maskRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, maskDraw, allFilter));

            DrawingSettings flowerMaskDraw = new DrawingSettings(new ShaderTagId("FlowerMask"), transparentSorting);
            RendererListHandle flowerMaskRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, flowerMaskDraw, allFilter));

            DrawingSettings colorDraw = new DrawingSettings(new ShaderTagId("GrassColor"), transparentSorting);
            RendererListHandle colorRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, colorDraw, allFilter));

            DrawingSettings slopeDraw = new DrawingSettings(new ShaderTagId("GrassSlope"), transparentSorting);
            RendererListHandle slopeRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, slopeDraw, allFilter));

            using (var builder = renderGraph.AddUnsafePass<PassData>("Grass Data Compute Pass", out var passData))
            {
                builder.AllowPassCulling(false);

                passData.mixedRL = mixedRL;
                passData.grassRL = grassRL;
                passData.flowerRL = flowerRL;
                passData.maskRL = maskRL;
                passData.flowerMaskRL = flowerMaskRL;
                passData.colorRL = colorRL;
                passData.slopeRL = slopeRL;

                passData.heightRT = heightRT;
                passData.heightDepthRT = heightDepthRT;
                passData.maskRT = maskRT;
                passData.flowerMaskRT = flowerMaskRT;
                passData.colorRT = colorRT;
                passData.slopeRT = slopeRT;

                passData.computeShader = computeShader;
                passData.grassPositionsBuffer = grassPositionsBuffer;
                passData.flowerPositionsBuffer = flowerPositionsBuffer;
                passData.camera = cameraData.camera;
                passData.cameraBounds = cameraBounds;

                builder.UseRendererList(mixedRL);
                builder.UseRendererList(grassRL);
                builder.UseRendererList(flowerRL);
                builder.UseRendererList(maskRL);
                builder.UseRendererList(flowerMaskRL);
                builder.UseRendererList(colorRL);
                builder.UseRendererList(slopeRL);

                builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

                    float spacing = InfiniteGrassRenderer.instance.spacing;
                    float maxDraw = InfiniteGrassRenderer.instance.GetMaxDrawDistance();
                    float texThresh = InfiniteGrassRenderer.instance.textureUpdateThreshold;

                    Vector2 centerPos = new Vector2(Mathf.Floor(data.camera.transform.position.x / texThresh) * texThresh, Mathf.Floor(data.camera.transform.position.z / texThresh) * texThresh);
                    Matrix4x4 viewMatrix = Matrix4x4.TRS(new Vector3(centerPos.x, data.cameraBounds.max.y, centerPos.y), Quaternion.LookRotation(-Vector3.up), new Vector3(1, 1, -1)).inverse;

                    // RTs must always project using maxDraw
                    Matrix4x4 projectionMatrix = Matrix4x4.Ortho(-(maxDraw + texThresh), maxDraw + texThresh, -(maxDraw + texThresh), maxDraw + texThresh, 0, data.cameraBounds.size.y);
                    Rect viewportRect = new Rect(0, 0, TextureSize, TextureSize);

                    cmd.SetRenderTarget(data.heightRT, data.heightDepthRT);
                    cmd.SetViewport(viewportRect);
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                    cmd.SetGlobalVector(SpawnAllowedID, new Vector4(1, 1, 0, 0));
                    cmd.DrawRendererList(data.mixedRL);
                    cmd.SetGlobalVector(SpawnAllowedID, new Vector4(1, 0, 0, 0));
                    cmd.DrawRendererList(data.grassRL);
                    cmd.SetGlobalVector(SpawnAllowedID, new Vector4(0, 1, 0, 0));
                    cmd.DrawRendererList(data.flowerRL);

                    cmd.SetRenderTarget(data.maskRT);
                    cmd.SetViewport(viewportRect);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    cmd.DrawRendererList(data.maskRL);
                    cmd.SetRenderTarget(data.flowerMaskRT);
                    cmd.SetViewport(viewportRect);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    cmd.DrawRendererList(data.flowerMaskRL);
                    cmd.SetRenderTarget(data.colorRT);
                    cmd.SetViewport(viewportRect);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    cmd.DrawRendererList(data.colorRL);
                    cmd.SetRenderTarget(data.slopeRT);
                    cmd.SetViewport(viewportRect);
                    cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
                    cmd.DrawRendererList(data.slopeRL);

                    cmd.SetGlobalTexture(GrassColorRT_ID, data.colorRT);
                    cmd.SetGlobalTexture(GrassSlopeRT_ID, data.slopeRT);
                    cmd.SetViewProjectionMatrices(data.camera.worldToCameraMatrix, data.camera.projectionMatrix);

                    data.grassPositionsBuffer.SetCounterValue(0);
                    data.flowerPositionsBuffer.SetCounterValue(0);

                    // Set Global Compute Variables
                    cmd.SetComputeMatrixParam(data.computeShader, VPMatrixID, data.camera.projectionMatrix * data.camera.worldToCameraMatrix);
                    cmd.SetComputeVectorParam(data.computeShader, BoundsMinID, data.cameraBounds.min);
                    cmd.SetComputeVectorParam(data.computeShader, BoundsMaxID, data.cameraBounds.max);
                    cmd.SetComputeVectorParam(data.computeShader, CameraPositionID, data.camera.transform.position);
                    cmd.SetComputeVectorParam(data.computeShader, CameraForwardID, data.camera.transform.forward);
                    cmd.SetComputeVectorParam(data.computeShader, CenterPosID, centerPos);
                    cmd.SetComputeFloatParam(data.computeShader, TextureUpdateThresholdID, texThresh);
                    cmd.SetComputeFloatParam(data.computeShader, MaxDrawDistanceID, maxDraw);

                    int kernelGrass = data.computeShader.FindKernel("CSGrass");
                    int kernelFlower = data.computeShader.FindKernel("CSFlower");

                    // --- GRASS DISPATCH ---
                    float gSpacing = Mathf.Max(0.01f, InfiniteGrassRenderer.instance.spacing);
                    Vector2Int gGridSize = new Vector2Int(Mathf.CeilToInt(data.cameraBounds.size.x / gSpacing), Mathf.CeilToInt(data.cameraBounds.size.z / gSpacing));
                    Vector2Int gGridStart = new Vector2Int(Mathf.FloorToInt(data.cameraBounds.min.x / gSpacing), Mathf.FloorToInt(data.cameraBounds.min.z / gSpacing));

                    cmd.SetComputeFloatParam(data.computeShader, SpacingID, gSpacing);
                    cmd.SetComputeFloatParam(data.computeShader, DrawDistanceID, InfiniteGrassRenderer.instance.drawDistance);
                    cmd.SetComputeFloatParam(data.computeShader, FullDensityDistanceID, InfiniteGrassRenderer.instance.fullDensityDistance);
                    cmd.SetComputeVectorParam(data.computeShader, GridStartIndexID, (Vector2)gGridStart);
                    cmd.SetComputeTextureParam(data.computeShader, kernelGrass, GrassHeightMapRT_ID, data.heightRT);
                    cmd.SetComputeTextureParam(data.computeShader, kernelGrass, GrassMaskMapRT_ID, data.maskRT);
                    cmd.SetComputeBufferParam(data.computeShader, kernelGrass, GrassPositionsID, data.grassPositionsBuffer);

                    cmd.DispatchCompute(data.computeShader, kernelGrass, Mathf.CeilToInt((float)gGridSize.x / 8), Mathf.CeilToInt((float)gGridSize.y / 8), 1);

                    // --- FLOWER DISPATCH ---
                    float fSpacing = Mathf.Max(0.01f, InfiniteGrassRenderer.instance.flowerSpacing);
                    Vector2Int fGridSize = new Vector2Int(Mathf.CeilToInt(data.cameraBounds.size.x / fSpacing), Mathf.CeilToInt(data.cameraBounds.size.z / fSpacing));
                    Vector2Int fGridStart = new Vector2Int(Mathf.FloorToInt(data.cameraBounds.min.x / fSpacing), Mathf.FloorToInt(data.cameraBounds.min.z / fSpacing));

                    cmd.SetComputeFloatParam(data.computeShader, SpacingID, fSpacing);
                    cmd.SetComputeFloatParam(data.computeShader, DrawDistanceID, InfiniteGrassRenderer.instance.flowerDrawDistance);
                    cmd.SetComputeFloatParam(data.computeShader, FullDensityDistanceID, InfiniteGrassRenderer.instance.flowerFullDensityDistance);
                    cmd.SetComputeVectorParam(data.computeShader, GridStartIndexID, (Vector2)fGridStart);
                    cmd.SetComputeTextureParam(data.computeShader, kernelFlower, GrassHeightMapRT_ID, data.heightRT);
                    cmd.SetComputeTextureParam(data.computeShader, kernelFlower, FlowerMaskMapRT_ID, data.flowerMaskRT);
                    cmd.SetComputeBufferParam(data.computeShader, kernelFlower, FlowerPositionsID, data.flowerPositionsBuffer);

                    cmd.DispatchCompute(data.computeShader, kernelFlower, Mathf.CeilToInt((float)fGridSize.x / 8), Mathf.CeilToInt((float)fGridSize.y / 8), 1);

                    cmd.SetGlobalBuffer(GrassPositionsID, data.grassPositionsBuffer);

                    if (InfiniteGrassRenderer.instance.grassMaterial != null)
                    {
                        InfiniteGrassRenderer.instance.grassMaterial.SetBuffer(GrassPositionsID, data.grassPositionsBuffer);
                        cmd.CopyCounterValue(data.grassPositionsBuffer, InfiniteGrassRenderer.instance.argsBuffer, 4);
                    }

                    if (InfiniteGrassRenderer.instance.flowerMaterial != null)
                    {
                        InfiniteGrassRenderer.instance.flowerMaterial.SetBuffer(GrassPositionsID, data.flowerPositionsBuffer);
                        if (InfiniteGrassRenderer.instance.flowerArgsBuffers != null)
                        {
                            foreach (var buffer in InfiniteGrassRenderer.instance.flowerArgsBuffers)
                            {
                                if (buffer != null) cmd.CopyCounterValue(data.flowerPositionsBuffer, buffer, 4);
                            }
                        }
                    }
                    if (InfiniteGrassRenderer.instance.flowerSettings != null)
                    {
                        foreach (var setting in InfiniteGrassRenderer.instance.flowerSettings)
                        {
                            if (setting.materials != null)
                            {
                                foreach (var mat in setting.materials)
                                {
                                    if (mat != null) mat.SetBuffer(GrassPositionsID, data.flowerPositionsBuffer);
                                }
                            }
                        }
                    }
                    if (InfiniteGrassRenderer.instance.previewVisibleGrassCount)
                    {
                        cmd.CopyCounterValue(data.grassPositionsBuffer, InfiniteGrassRenderer.instance.tBuffer, 0);
                    }
                });
            }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) { }

        Bounds CalculateCameraBounds(Camera camera, float drawDistance)
        {
            // Create a perfectly stable bounding volume centered on the camera.
            // This prevents the grid from resizing or shifting when the FOV or rotation changes.
            Vector3 center = camera.transform.position;
            float size = drawDistance * 2f;

            Bounds bounds = new Bounds(center, new Vector3(size, size, size));
            return bounds;
        }

        public void Dispose()
        {
            heightRT?.Release();
            heightDepthRT?.Release();
            maskRT?.Release();
            flowerMaskRT?.Release();
            colorRT?.Release();
            slopeRT?.Release();
            grassPositionsBuffer?.Release();
            flowerPositionsBuffer?.Release();
        }
    }
}