// Put this file in a folder named "Editor" (e.g. Assets/Editor/ShieldPanelBaker.cs)
//
// Finds every separate panel (hex / pentagon) in the shield mesh and stores, per vertex:
//   UV1 (mesh channel 1) = (panelCenter.x, panelCenter.y, panelCenter.z, panelRandom01)
// Shader Graph reads it with a UV node set to "UV1".
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ShieldPanelBaker
{
    const int Seed = 1234; // change for a different random pattern

    [MenuItem("Tools/Shield/Bake Panel Data")]
    static void Bake()
    {
        Mesh src = null;
        MeshFilter filter = null;

        if (Selection.activeGameObject != null)
        {
            filter = Selection.activeGameObject.GetComponent<MeshFilter>();
            if (filter != null) src = filter.sharedMesh;
        }
        if (src == null) src = Selection.activeObject as Mesh;

        if (src == null)
        {
            EditorUtility.DisplayDialog("Shield Panel Baker",
                "Select the shield object in the Hierarchy (it needs a MeshFilter), or select the Mesh asset.", "OK");
            return;
        }
        if (!src.isReadable)
        {
            EditorUtility.DisplayDialog("Shield Panel Baker",
                "Mesh is not readable. Select the model file, Inspector > Model tab > tick 'Read/Write', Apply.", "OK");
            return;
        }

        Vector3[] verts = src.vertices;
        int[] tris = src.triangles;

        // Union-Find: vertices that share a triangle belong to the same panel.
        int[] parent = new int[verts.Length];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (int i = 0; i < tris.Length; i += 3)
        {
            Union(tris[i], tris[i + 1]);
            Union(tris[i + 1], tris[i + 2]);
        }

        // Centroid + random value per panel
        var sum = new Dictionary<int, Vector3>();
        var count = new Dictionary<int, int>();
        var rand = new Dictionary<int, float>();
        var rng = new System.Random(Seed);

        for (int i = 0; i < verts.Length; i++)
        {
            int r = Find(i);
            if (!sum.ContainsKey(r))
            {
                sum[r] = Vector3.zero;
                count[r] = 0;
                rand[r] = (float)rng.NextDouble();
            }
            sum[r] += verts[i];
            count[r]++;
        }

        var data = new List<Vector4>(verts.Length);
        for (int i = 0; i < verts.Length; i++)
        {
            int r = Find(i);
            Vector3 c = sum[r] / count[r];
            data.Add(new Vector4(c.x, c.y, c.z, rand[r]));
        }

        Mesh baked = Object.Instantiate(src);
        baked.name = src.name + "_Panels";
        baked.SetUVs(1, data);

        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/" + baked.name + ".asset");
        AssetDatabase.CreateAsset(baked, path);
        AssetDatabase.SaveAssets();

        if (filter != null)
        {
            Undo.RecordObject(filter, "Assign baked shield mesh");
            filter.sharedMesh = baked;
            EditorUtility.SetDirty(filter);
        }

        Debug.Log($"[ShieldPanelBaker] Found {sum.Count} panels. Saved {path}", baked);
    }
}