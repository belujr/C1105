using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class AOEConeVisualizer : MonoBehaviour
{
	[Tooltip("Link your Player Controller here so it can read the equipped stats.")]
	public PlayerController player;

	[Tooltip("Check this to visualize the RT Heavy Attack. Uncheck it for the Y-Button Special Attack.")]
	public bool visualizeHeavyAttack = false;

	[Tooltip("How smooth the curve is. Higher = smoother.")]
	public int segments = 30;

	private Mesh mesh;

	private void Awake()
	{
		mesh = new Mesh();
		GetComponent<MeshFilter>().mesh = mesh;

		// If not assigned manually, try to find it on the parent
		if (player == null)
		{
			player = GetComponentInParent<PlayerController>();
		}
	}

	private void Update()
	{
		if (player != null)
		{
			AttackData activeData = null;

			// Figure out which attack we should be looking at
			if (visualizeHeavyAttack && player.equippedStyle != null)
			{
				activeData = player.equippedStyle.GetActiveChargeAttack();
			}
			else
			{
				activeData = player.specialAttackY;
			}

			// If we have data and it IS an AOE attack, draw the mesh!
			if (activeData != null && activeData.isAOE)
			{
				GetComponent<MeshRenderer>().enabled = true;
				DrawCone(activeData.aoeRadius, activeData.coneAngle);
			}
			else
			{
				// Hide the mesh entirely if the equipped attack isn't an AOE
				GetComponent<MeshRenderer>().enabled = false;
			}
		}
	}

	private void DrawCone(float radius, float angle)
	{
		int numVertices = segments + 2;
		Vector3[] vertices = new Vector3[numVertices];
		int[] triangles = new int[segments * 3];

		// The center point of the cone is always at local 0,0,0
		vertices[0] = Vector3.zero;

		float currentAngle = -angle / 2f;
		float angleStep = angle / segments;

		for (int i = 0; i <= segments; i++)
		{
			// Convert degrees to radians for math
			float rad = currentAngle * Mathf.Deg2Rad;

			// Calculate the X and Z position (laying flat on the ground)
			float x = Mathf.Sin(rad) * radius;
			float z = Mathf.Cos(rad) * radius;

			vertices[i + 1] = new Vector3(x, 0f, z);

			// Build the triangles to fill the mesh
			if (i < segments)
			{
				triangles[i * 3] = 0;
				triangles[i * 3 + 1] = i + 1;
				triangles[i * 3 + 2] = i + 2;
			}

			currentAngle += angleStep;
		}

		// Apply the math to the actual 3D mesh
		mesh.Clear();
		mesh.vertices = vertices;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
	}
}