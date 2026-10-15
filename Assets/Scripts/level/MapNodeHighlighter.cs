using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spotlight + power-up controller for the chamber selection map.
///
/// When the player hovers a chamber:
///   - one shared spotlight glides over to it. The cone starts wide and tightens onto the chamber,
///   - the rest of the map dims a little (through MapPostFX, if it is on this object),
///   - the chamber's MapNodePowerUp pops up. The previous chamber's power-up shrinks away.
/// When a chamber is selected the light flashes and the power-up punches.
///
/// Put this on the SAME GameObject as MapController. The light is created from code.
/// </summary>
[RequireComponent(typeof(MapController))]
public class MapNodeHighlighter : MonoBehaviour
{
    [Header("Spotlight")]
    public Color color = new Color(1f, 0.88f, 0.66f);
    [Tooltip("Brightness on the chamber compared to normal. Raise for a stronger spotlight.")]
    public float intensity = 3f;
    [Tooltip("How high the light hangs above the chamber, as a fraction of the map size.")]
    [Range(0.05f, 0.6f)] public float heightFraction = 0.22f;
    [Tooltip("Radius of the lit circle, in chamber-icon widths.")]
    public float spotRadiusInNodes = 2.5f;
    [Tooltip("Extra cone angle at the moment of hovering. The cone tightens from this down to its final size.")]
    public float openAngle = 25f;
    public float tightenTime = 0.45f;
    [Tooltip("How softly the light glides from one chamber to the next (seconds).")]
    public float moveSmoothTime = 0.14f;
    public float fadeIn = 0.35f;
    public float fadeOut = 0.25f;
    [Tooltip("Shadows from the spotlight (nicer, but costs performance).")]
    public bool castShadows = false;

    [Header("Dim the rest of the map (needs MapPostFX)")]
    [Range(0f, 1f)] public float dimStrength = 1f;

    private Light spot;
    private MapPostFX fx;

    private bool shown;
    private MapNode current;
    private MapNodePowerUp currentPowerUp;
    private readonly Dictionary<MapNode, Renderer[]> rendererCache = new Dictionary<MapNode, Renderer[]>();

    private float lightHeight = 5f;
    private float level;          // 0..1 fade of the whole spotlight
    private float kick;           // 1 right after a hover change, decays to 0
    private float selectFlash;    // 1 right after a selection, decays to 0
    private float targetAngle = 50f;

    private bool placed;
    private Vector3 pos;
    private Vector3 vel;
    private Vector3 aim;

    // ------------------------------------------------------------------ public API

    /// <summary>The map just appeared.</summary>
    public void Show()
    {
        MapController mc = GetComponent<MapController>();
        if (mc == null || mc.mapBoardRoot == null) return;

        fx = GetComponent<MapPostFX>();
        EnsureLight();

        // the map camera culls lights by layer too, so the light must be on the map's layer
        spot.gameObject.layer = mc.mapBoardRoot.layer;

        // size of the map, so every distance scales with it
        float mapSize = 20f;
        bool any = false;
        Bounds b = new Bounds(mc.mapBoardRoot.transform.position, Vector3.zero);
        foreach (Renderer r in mc.mapBoardRoot.GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (any) mapSize = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        lightHeight = Mathf.Max(0.5f, mapSize * heightFraction);

        // everything starts clean
        foreach (MapNodePowerUp p in mc.mapBoardRoot.GetComponentsInChildren<MapNodePowerUp>(true))
            p.HideInstant();

        rendererCache.Clear();
        current = null;
        currentPowerUp = null;
        level = 0f;
        kick = 0f;
        selectFlash = 0f;
        placed = false;
        spot.enabled = false;
        shown = true;
    }

    /// <summary>The map is about to disappear (the level starts).</summary>
    public void Hide()
    {
        shown = false;
        current = null;
        currentPowerUp = null;
        level = 0f;
        if (spot != null) spot.enabled = false;
        if (fx != null)
        {
            fx.FocusAmount = 0f;
            fx.ClearFocusPoint();
        }

        MapController mc = GetComponent<MapController>();
        if (mc != null && mc.mapBoardRoot != null)
        {
            foreach (MapNodePowerUp p in mc.mapBoardRoot.GetComponentsInChildren<MapNodePowerUp>(true))
                p.HideInstant();
        }
    }

    /// <summary>The hovered chamber changed (null = nothing hovered).</summary>
    public void OnHover(MapNode node)
    {
        if (!shown || node == current) return;

        if (currentPowerUp != null) currentPowerUp.Hide();

        current = node;
        currentPowerUp = node != null ? node.GetComponent<MapNodePowerUp>() : null;

        if (node == null) return;

        kick = 1f;

        Bounds nb = GetBounds(node);
        float width = Mathf.Max(0.0001f, Mathf.Max(nb.size.x, nb.size.z));
        targetAngle = Mathf.Clamp(2f * Mathf.Atan(width * spotRadiusInNodes / lightHeight) * Mathf.Rad2Deg, 15f, 110f);

        if (level < 0.05f) placed = false;     // first hover: appear on the chamber instead of flying in

        if (currentPowerUp != null) currentPowerUp.Show();
    }

    /// <summary>The player confirmed this chamber.</summary>
    public void OnSelect(MapNode node)
    {
        if (!shown) return;
        if (node != current) OnHover(node);

        selectFlash = 1f;
        if (currentPowerUp != null) currentPowerUp.Punch();
    }

    // ------------------------------------------------------------------ light

    private void EnsureLight()
    {
        if (spot != null) return;

        GameObject go = new GameObject("MapNodeSpotlight");
        go.transform.SetParent(transform, false);

        spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = color;
        spot.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
        spot.enabled = false;
    }

    private void Update()
    {
        if (!shown || spot == null) return;

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        // fade the whole spotlight in / out
        float target = current != null ? 1f : 0f;
        float time = target > level ? fadeIn : fadeOut;
        level = Mathf.MoveTowards(level, target, dt / Mathf.Max(0.01f, time));

        kick = Mathf.MoveTowards(kick, 0f, dt / Mathf.Max(0.01f, tightenTime));
        selectFlash = Mathf.MoveTowards(selectFlash, 0f, dt / 0.6f);

        // dim the rest of the map while a chamber is lit
        if (fx != null)
        {
            fx.FocusAmount = dimStrength * EaseOutCubic(level);
            if (current == null) fx.ClearFocusPoint();
        }

        if (level <= 0.001f)
        {
            spot.enabled = false;
            placed = false;
            return;
        }

        // glide to the hovered chamber
        if (current != null)
        {
            Bounds nb = GetBounds(current);
            Vector3 want = nb.center + Vector3.up * lightHeight;
            aim = nb.center;

            // depth of field follows the lit chamber, so the spotlight area stays sharp
            if (fx != null) fx.SetFocusPoint(nb.center);

            if (!placed)
            {
                pos = want;
                vel = Vector3.zero;
                placed = true;
            }
            else
            {
                pos = Vector3.SmoothDamp(pos, want, ref vel, Mathf.Max(0.01f, moveSmoothTime), Mathf.Infinity, dt);
            }
        }

        spot.enabled = true;
        spot.color = color;
        spot.range = lightHeight * 3.5f;

        Vector3 dir = aim - pos;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.down;
        spot.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir.normalized));

        // cone starts wide and tightens
        float angle = targetAngle + openAngle * kick * kick;
        spot.spotAngle = angle;
        spot.innerSpotAngle = angle * 0.55f;

        // intensity scales with height squared (light falls off with distance squared),
        // so "intensity = 3" always means roughly "3x normal brightness on the chamber"
        float breath = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 2.2f);
        float flash = 1f + selectFlash * 1.5f;
        spot.intensity = intensity * lightHeight * lightHeight * EaseOutCubic(level) * breath * flash;
    }

    // ------------------------------------------------------------------ helpers

    private Bounds GetBounds(MapNode node)
    {
        if (!rendererCache.TryGetValue(node, out Renderer[] rs))
        {
            rs = node.GetComponentsInChildren<Renderer>();
            rendererCache[node] = rs;
        }

        bool any = false;
        Bounds b = new Bounds(node.transform.position, Vector3.zero);
        foreach (Renderer r in rs)
        {
            if (r == null) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        if (!any) b = new Bounds(node.transform.position, Vector3.one * Mathf.Max(0.01f, node.transform.lossyScale.x));
        return b;
    }

    private static float EaseOutCubic(float p)
    {
        p = 1f - p;
        return 1f - p * p * p;
    }

    private void OnDestroy()
    {
        if (spot != null) Destroy(spot.gameObject);
    }
}