using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Switches the whole world off (every renderer, the terrain, the procedural grass) and makes the camera clear to pure black,
/// so only one object (the dying player, with its effects) and the screen effects on the camera stay visible.
/// Show() puts everything back exactly as it was. Used by PlayerDeathFX.
/// </summary>
public class DeathWorldHider
{
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Terrain> terrains = new List<Terrain>();
    private readonly List<bool> terrainHeightmap = new List<bool>();
    private readonly List<bool> terrainTrees = new List<bool>();
    private InfiniteGrassRenderer grass;
    private Camera cam;
    private CameraClearFlags savedClear;
    private Color savedBackground;

    public bool IsHidden { get; private set; }

    /// <param name="keepRoot">Everything below this transform stays visible (the player).</param>
    public void Hide(Camera camera, Transform keepRoot)
    {
        if (IsHidden) return;
        IsHidden = true;

        // every renderer except the kept object's, the camera's (screen effects) and the ones that are already off
#if UNITY_2023_1_OR_NEWER
        Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
#else
        Renderer[] all = Object.FindObjectsOfType<Renderer>();
#endif
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled) continue;
            if (r.transform.IsChildOf(keepRoot)) continue;
            if (camera != null && r.transform.IsChildOf(camera.transform)) continue;
            r.enabled = false;
            renderers.Add(r);
        }

        // terrain and the procedural grass are not renderers
        foreach (Terrain t in Terrain.activeTerrains)
        {
            terrains.Add(t);
            terrainHeightmap.Add(t.drawHeightmap);
            terrainTrees.Add(t.drawTreesAndFoliage);
            t.drawHeightmap = false;
            t.drawTreesAndFoliage = false;
        }
        if (InfiniteGrassRenderer.instance != null)
        {
            grass = InfiniteGrassRenderer.instance;
            grass.enabled = false;
        }

        // and the camera clears to pure black instead of showing the sky
        if (camera != null)
        {
            cam = camera;
            savedClear = camera.clearFlags;
            savedBackground = camera.backgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
    }

    public void Show()
    {
        if (!IsHidden) return;
        IsHidden = false;

        for (int i = 0; i < renderers.Count; i++)
            if (renderers[i] != null) renderers[i].enabled = true;
        renderers.Clear();

        for (int i = 0; i < terrains.Count; i++)
        {
            if (terrains[i] == null) continue;
            terrains[i].drawHeightmap = terrainHeightmap[i];
            terrains[i].drawTreesAndFoliage = terrainTrees[i];
        }
        terrains.Clear();
        terrainHeightmap.Clear();
        terrainTrees.Clear();

        if (grass != null) grass.enabled = true;
        grass = null;

        if (cam != null)
        {
            cam.clearFlags = savedClear;
            cam.backgroundColor = savedBackground;
        }
        cam = null;
    }
}
