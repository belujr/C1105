#pragma warning disable 0618, 0672
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class GrassDataRendererFeature : ScriptableRendererFeature
{
	[SerializeField] private LayerMask heightMapLayer;
	[SerializeField] private Material heightMapMat;
	[SerializeField] private ComputeShader computeShader;

	GrassDataPass grassDataPass;

	public override void Create()
	{
		grassDataPass = new GrassDataPass(heightMapLayer, heightMapMat, computeShader);
		grassDataPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
	}

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		renderer.EnqueuePass(grassDataPass);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			grassDataPass?.Dispose();
		}
	}

	private class GrassDataPass : ScriptableRenderPass
	{
		private List<ShaderTagId> shaderTagsList = new List<ShaderTagId>();

		private RTHandle heightRT;
		private RTHandle heightDepthRT;
		private RTHandle maskRT;
		private RTHandle colorRT;
		private RTHandle slopeRT;

		private LayerMask heightMapLayer;
		private Material heightMapMat;
		private ComputeShader computeShader;
		private ComputeBuffer grassPositionsBuffer;

		private const int TextureSize = 2048;

		// OPTIMIZATION: Pre-hash Shader Property IDs
		private static readonly int BoundsYMinMaxID = Shader.PropertyToID("_BoundsYMinMax");
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
		private static readonly int GrassPositionsID = Shader.PropertyToID("_GrassPositions");
		private static readonly int GrassHeightMapRT_ID = Shader.PropertyToID("_GrassHeightMapRT");
		private static readonly int GrassMaskMapRT_ID = Shader.PropertyToID("_GrassMaskMapRT");

		public GrassDataPass(LayerMask heightMapLayer, Material heightMapMat, ComputeShader computeShader)
		{
			this.heightMapLayer = heightMapLayer;
			this.computeShader = computeShader;
			this.heightMapMat = heightMapMat;

			shaderTagsList.Add(new ShaderTagId("SRPDefaultUnlit"));
			shaderTagsList.Add(new ShaderTagId("UniversalForward"));
			shaderTagsList.Add(new ShaderTagId("UniversalForwardOnly"));
		}

		private void AllocateRTs()
		{
			RenderingUtils.ReAllocateIfNeeded(ref heightRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.RGFloat, 0), FilterMode.Bilinear, name: "GrassHeightRT");
			RenderingUtils.ReAllocateIfNeeded(ref heightDepthRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.Depth, 32), FilterMode.Point, name: "GrassHeightDepthRT");
			RenderingUtils.ReAllocateIfNeeded(ref maskRT, new RenderTextureDescriptor(TextureSize, TextureSize, RenderTextureFormat.RFloat, 0), FilterMode.Bilinear, name: "GrassMaskRT");
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
		}

		public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
		{
			AllocateRTs();
			ConfigureTarget(heightRT, heightDepthRT);
			ConfigureClear(ClearFlag.All, Color.black);
		}

		private class PassData
		{
			public RendererListHandle heightRLHandle;
			public RendererListHandle maskRLHandle;
			public RendererListHandle colorRLHandle;
			public RendererListHandle slopeRLHandle;
			public RTHandle heightRT, heightDepthRT, maskRT, colorRT, slopeRT;
			public ComputeShader computeShader;
			public ComputeBuffer grassPositionsBuffer;
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

			Bounds cameraBounds = CalculateCameraBounds(cameraData.camera, InfiniteGrassRenderer.instance.drawDistance);

			SortingSettings heightSorting = new SortingSettings(cameraData.camera) { criteria = cameraData.defaultOpaqueSortFlags };
			DrawingSettings heightDraw = new DrawingSettings(shaderTagsList[0], heightSorting);
			for (int i = 1; i < shaderTagsList.Count; i++)
			{
				heightDraw.SetShaderPassName(i, shaderTagsList[i]);
			}
			heightMapMat.SetVector(BoundsYMinMaxID, new Vector2(cameraBounds.min.y, cameraBounds.max.y));
			heightDraw.overrideMaterial = heightMapMat;

			FilteringSettings heightFilter = new FilteringSettings(RenderQueueRange.all, heightMapLayer);
			RendererListHandle heightRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, heightDraw, heightFilter));

			SortingSettings transparentSorting = new SortingSettings(cameraData.camera) { criteria = SortingCriteria.CommonTransparent };
			FilteringSettings allFilter = new FilteringSettings(RenderQueueRange.all);

			DrawingSettings maskDraw = new DrawingSettings(new ShaderTagId("GrassMask"), transparentSorting);
			RendererListHandle maskRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, maskDraw, allFilter));

			DrawingSettings colorDraw = new DrawingSettings(new ShaderTagId("GrassColor"), transparentSorting);
			RendererListHandle colorRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, colorDraw, allFilter));

			DrawingSettings slopeDraw = new DrawingSettings(new ShaderTagId("GrassSlope"), transparentSorting);
			RendererListHandle slopeRL = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, slopeDraw, allFilter));

			using (var builder = renderGraph.AddUnsafePass<PassData>("Grass Data Compute Pass", out var passData))
			{
				builder.AllowPassCulling(false);

				passData.heightRLHandle = heightRL;
				passData.maskRLHandle = maskRL;
				passData.colorRLHandle = colorRL;
				passData.slopeRLHandle = slopeRL;

				passData.heightRT = heightRT;
				passData.heightDepthRT = heightDepthRT;
				passData.maskRT = maskRT;
				passData.colorRT = colorRT;
				passData.slopeRT = slopeRT;

				passData.computeShader = computeShader;
				passData.grassPositionsBuffer = grassPositionsBuffer;
				passData.camera = cameraData.camera;
				passData.cameraBounds = cameraBounds;

				builder.UseRendererList(heightRL);
				builder.UseRendererList(maskRL);
				builder.UseRendererList(colorRL);
				builder.UseRendererList(slopeRL);

				builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
				{
					CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

					float spacing = InfiniteGrassRenderer.instance.spacing;
					float drawDist = InfiniteGrassRenderer.instance.drawDistance;
					float texThresh = InfiniteGrassRenderer.instance.textureUpdateThreshold;
					float fullDensityDist = InfiniteGrassRenderer.instance.fullDensityDistance;

					Vector2 centerPos = new Vector2(Mathf.Floor(data.camera.transform.position.x / texThresh) * texThresh, Mathf.Floor(data.camera.transform.position.z / texThresh) * texThresh);
					Matrix4x4 viewMatrix = Matrix4x4.TRS(new Vector3(centerPos.x, data.cameraBounds.max.y, centerPos.y), Quaternion.LookRotation(-Vector3.up), new Vector3(1, 1, -1)).inverse;
					Matrix4x4 projectionMatrix = Matrix4x4.Ortho(-(drawDist + texThresh), drawDist + texThresh, -(drawDist + texThresh), drawDist + texThresh, 0, data.cameraBounds.size.y);

					Rect viewportRect = new Rect(0, 0, TextureSize, TextureSize);

					cmd.SetRenderTarget(data.heightRT, data.heightDepthRT);
					cmd.SetViewport(viewportRect);
					cmd.ClearRenderTarget(true, true, Color.black);
					cmd.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
					cmd.DrawRendererList(data.heightRLHandle);

					cmd.SetRenderTarget(data.maskRT);
					cmd.SetViewport(viewportRect);
					cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
					cmd.DrawRendererList(data.maskRLHandle);

					cmd.SetRenderTarget(data.colorRT);
					cmd.SetViewport(viewportRect);
					cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
					cmd.DrawRendererList(data.colorRLHandle);

					cmd.SetRenderTarget(data.slopeRT);
					cmd.SetViewport(viewportRect);
					cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
					cmd.DrawRendererList(data.slopeRLHandle);

					cmd.SetGlobalTexture(GrassColorRT_ID, data.colorRT);
					cmd.SetGlobalTexture(GrassSlopeRT_ID, data.slopeRT);

					cmd.SetViewProjectionMatrices(data.camera.worldToCameraMatrix, data.camera.projectionMatrix);

					Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(data.cameraBounds.size.x / spacing), Mathf.CeilToInt(data.cameraBounds.size.z / spacing));
					Vector2Int gridStartIndex = new Vector2Int(Mathf.FloorToInt(data.cameraBounds.min.x / spacing), Mathf.FloorToInt(data.cameraBounds.min.z / spacing));

					data.grassPositionsBuffer.SetCounterValue(0);

					// OPTIMIZATION: Property IDs applied here
					cmd.SetComputeMatrixParam(data.computeShader, VPMatrixID, data.camera.projectionMatrix * data.camera.worldToCameraMatrix);
					cmd.SetComputeFloatParam(data.computeShader, FullDensityDistanceID, fullDensityDist);
					cmd.SetComputeVectorParam(data.computeShader, BoundsMinID, data.cameraBounds.min);
					cmd.SetComputeVectorParam(data.computeShader, BoundsMaxID, data.cameraBounds.max);
					cmd.SetComputeVectorParam(data.computeShader, CameraPositionID, data.camera.transform.position);
					cmd.SetComputeVectorParam(data.computeShader, CenterPosID, centerPos);
					cmd.SetComputeFloatParam(data.computeShader, DrawDistanceID, drawDist);
					cmd.SetComputeFloatParam(data.computeShader, TextureUpdateThresholdID, texThresh);
					cmd.SetComputeFloatParam(data.computeShader, SpacingID, spacing);
					cmd.SetComputeVectorParam(data.computeShader, GridStartIndexID, (Vector2)gridStartIndex);
					cmd.SetComputeVectorParam(data.computeShader, GridSizeID, (Vector2)gridSize);

					cmd.SetComputeBufferParam(data.computeShader, 0, GrassPositionsID, data.grassPositionsBuffer);
					cmd.SetComputeTextureParam(data.computeShader, 0, GrassHeightMapRT_ID, data.heightRT);
					cmd.SetComputeTextureParam(data.computeShader, 0, GrassMaskMapRT_ID, data.maskRT);

					cmd.DispatchCompute(data.computeShader, 0, Mathf.CeilToInt((float)gridSize.x / 8), Mathf.CeilToInt((float)gridSize.y / 8), 1);

					cmd.SetGlobalBuffer(GrassPositionsID, data.grassPositionsBuffer);
					if (InfiniteGrassRenderer.instance.grassMaterial != null)
					{
						InfiniteGrassRenderer.instance.grassMaterial.SetBuffer(GrassPositionsID, data.grassPositionsBuffer);
					}

					cmd.CopyCounterValue(data.grassPositionsBuffer, InfiniteGrassRenderer.instance.argsBuffer, 4);

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
			Vector3 ntopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));
			Vector3 ntopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
			Vector3 nbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
			Vector3 nbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));

			Vector3 ftopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, drawDistance));
			Vector3 ftopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, drawDistance));
			Vector3 fbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, drawDistance));
			Vector3 fbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, drawDistance));

			float startX = Mathf.Max(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);
			float endX = Mathf.Min(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);

			float startY = Mathf.Max(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);
			float endY = Mathf.Min(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);

			float startZ = Mathf.Max(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);
			float endZ = Mathf.Min(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);

			Vector3 center = new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2);
			Vector3 size = new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ));

			Bounds bounds = new Bounds(center, size);
			bounds.Expand(1);
			return bounds;
		}

		public void Dispose()
		{
			heightRT?.Release();
			heightDepthRT?.Release();
			maskRT?.Release();
			colorRT?.Release();
			slopeRT?.Release();
			grassPositionsBuffer?.Release();
		}
	}
}