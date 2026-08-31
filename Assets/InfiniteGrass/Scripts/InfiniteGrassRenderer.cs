#pragma warning disable 0618, 0672
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class InfiniteGrassRenderer : MonoBehaviour
{
	[HideInInspector] public static InfiniteGrassRenderer instance;

	[Header("Internal")]
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

	[Header("Debug")]
	public bool previewVisibleGrassCount = false;

	private Mesh cachedGrassMesh;
	private int oldSubdivision = -1;
	private uint[] args = new uint[5];
	private Camera mainCam;

	// OPTIMIZATION: Pre-hash Shader Property IDs
	private static readonly int CenterPosID = Shader.PropertyToID("_CenterPos");
	private static readonly int DrawDistanceID = Shader.PropertyToID("_DrawDistance");
	private static readonly int TextureUpdateThresholdID = Shader.PropertyToID("_TextureUpdateThreshold");
	private static readonly int GrassPositionsID = Shader.PropertyToID("_GrassPositions");

	private void OnEnable()
	{
		instance = this;
	}

	private void OnDisable()
	{
		instance = null;
		argsBuffer?.Release();
		tBuffer?.Release();
		argsBuffer = null;
		tBuffer = null;
	}

	void LateUpdate()
	{
		if (spacing == 0 || grassMaterial == null) return;

		// OPTIMIZATION: Cache Camera.main
		if (mainCam == null) mainCam = Camera.main;
		if (mainCam == null) return;

		Bounds cameraBounds = CalculateCameraBounds(mainCam);
		Vector2 centerPos = new Vector2(Mathf.Floor(mainCam.transform.position.x / textureUpdateThreshold) * textureUpdateThreshold, Mathf.Floor(mainCam.transform.position.z / textureUpdateThreshold) * textureUpdateThreshold);

		if (argsBuffer == null)
			argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

		if (tBuffer == null)
			tBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);

		// OPTIMIZATION: Call GetGrassMeshCache only once per frame
		Mesh currentMesh = GetGrassMeshCache();

		args[0] = (uint)currentMesh.GetIndexCount(0);
		args[1] = (uint)(maxBufferCount * 1000000);
		args[2] = (uint)currentMesh.GetIndexStart(0);
		args[3] = (uint)currentMesh.GetBaseVertex(0);
		args[4] = 0;
		argsBuffer.SetData(args);

		// OPTIMIZATION: Use Property IDs instead of strings
		grassMaterial.SetVector(CenterPosID, centerPos);
		grassMaterial.SetFloat(DrawDistanceID, drawDistance);
		grassMaterial.SetFloat(TextureUpdateThresholdID, textureUpdateThreshold);

		Graphics.DrawMeshInstancedIndirect(currentMesh, 0, grassMaterial, cameraBounds, argsBuffer);
	}

	private void OnGUI()
	{
		if (previewVisibleGrassCount && tBuffer != null)
		{
			GUI.contentColor = Color.black;
			GUIStyle style = new GUIStyle();
			style.fontSize = 25;

			uint[] count = new uint[1];
			tBuffer.GetData(count);

			Bounds cameraBounds = CalculateCameraBounds(mainCam != null ? mainCam : Camera.main);
			Vector2Int gridSize = new Vector2Int(Mathf.CeilToInt(cameraBounds.size.x / spacing), Mathf.CeilToInt(cameraBounds.size.z / spacing));

			GUI.Label(new Rect(50, 50, 400, 200), "Dispatch Size : " + gridSize.x + "x" + gridSize.y + " = " + (gridSize.x * gridSize.y), style);
			GUI.Label(new Rect(50, 80, 400, 200), "Visible Grass Count : " + count[0], style);
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
				float y1 = (float)i / (grassMeshSubdivision + 1);
				float y2 = (float)(i + 1) / (grassMeshSubdivision + 1);

				Vector3 bottomLeft = new Vector3(-0.25f, y1);
				Vector3 bottomRight = new Vector3(0.25f, y1);
				Vector3 topLeft = new Vector3(-0.25f, y2);
				Vector3 topRight = new Vector3(0.25f, y2);

				int bottomLeftIndex = i * 4;
				int bottomRightIndex = i * 4 + 1;
				int topLeftIndex = i * 4 + 2;
				int topRightIndex = i * 4 + 3;

				vertices[bottomLeftIndex] = bottomLeft;
				vertices[bottomRightIndex] = bottomRight;
				vertices[topLeftIndex] = topLeft;
				vertices[topRightIndex] = topRight;

				triangles[i * 6] = bottomLeftIndex;
				triangles[i * 6 + 1] = topRightIndex;
				triangles[i * 6 + 2] = bottomRightIndex;
				triangles[i * 6 + 3] = bottomLeftIndex;
				triangles[i * 6 + 4] = topLeftIndex;
				triangles[i * 6 + 5] = topRightIndex;
			}

			vertices[grassMeshSubdivision * 4] = new Vector3(-0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));
			vertices[grassMeshSubdivision * 4 + 1] = new Vector3(0, 1);
			vertices[grassMeshSubdivision * 4 + 2] = new Vector3(0.25f, (float)grassMeshSubdivision / (grassMeshSubdivision + 1));

			triangles[grassMeshSubdivision * 6] = grassMeshSubdivision * 4;
			triangles[grassMeshSubdivision * 6 + 1] = grassMeshSubdivision * 4 + 1;
			triangles[grassMeshSubdivision * 6 + 2] = grassMeshSubdivision * 4 + 2;

			cachedGrassMesh.SetVertices(vertices);
			cachedGrassMesh.SetTriangles(triangles, 0);
			cachedGrassMesh.RecalculateNormals();

			oldSubdivision = grassMeshSubdivision;
		}

		return cachedGrassMesh;
	}

	float GetMax(float a, float b, float c, float d, float e, float f, float g, float h) => Mathf.Max(a, Mathf.Max(b, Mathf.Max(c, Mathf.Max(d, Mathf.Max(e, Mathf.Max(f, Mathf.Max(g, h)))))));
	float GetMin(float a, float b, float c, float d, float e, float f, float g, float h) => Mathf.Min(a, Mathf.Min(b, Mathf.Min(c, Mathf.Min(d, Mathf.Min(e, Mathf.Min(f, Mathf.Min(g, h)))))));

	Bounds CalculateCameraBounds(Camera camera)
	{
		Vector3 ntopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, camera.nearClipPlane));
		Vector3 ntopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, camera.nearClipPlane));
		Vector3 nbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, camera.nearClipPlane));
		Vector3 nbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, camera.nearClipPlane));

		Vector3 ftopLeft = camera.ViewportToWorldPoint(new Vector3(0, 1, drawDistance));
		Vector3 ftopRight = camera.ViewportToWorldPoint(new Vector3(1, 1, drawDistance));
		Vector3 fbottomLeft = camera.ViewportToWorldPoint(new Vector3(0, 0, drawDistance));
		Vector3 fbottomRight = camera.ViewportToWorldPoint(new Vector3(1, 0, drawDistance));

		float startX = GetMax(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);
		float endX = GetMin(ftopLeft.x, ftopRight.x, ntopLeft.x, ntopRight.x, fbottomLeft.x, fbottomRight.x, nbottomLeft.x, nbottomRight.x);

		float startY = GetMax(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);
		float endY = GetMin(ftopLeft.y, ftopRight.y, ntopLeft.y, ntopRight.y, fbottomLeft.y, fbottomRight.y, nbottomLeft.y, nbottomRight.y);

		float startZ = GetMax(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);
		float endZ = GetMin(ftopLeft.z, ftopRight.z, ntopLeft.z, ntopRight.z, fbottomLeft.z, fbottomRight.z, nbottomLeft.z, nbottomRight.z);

		Vector3 center = new Vector3((startX + endX) / 2, (startY + endY) / 2, (startZ + endZ) / 2);
		Vector3 size = new Vector3(Mathf.Abs(startX - endX), Mathf.Abs(startY - endY), Mathf.Abs(startZ - endZ));

		Bounds bounds = new Bounds(center, size);
		bounds.Expand(1);
		return bounds;
	}
}