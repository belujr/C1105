using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the SLAB in the buff room.
///
///   1. The buff the player picked on the chamber map (the model that popped over the chamber icon)
///      appears floating on top of the slab.
///   2. The player walks up and picks it up (A / Cross on a gamepad, E on the keyboard).
///   3. The chamber is completed, and your fire portal (PortalVFX) opens in the air behind the slab.
///   4. Walking through the portal sends the player back to the chamber selection map.
///
/// If the player dies before picking the buff up, nothing is completed: the map offers the same chamber again.
///
/// The portal is made from your PortalVFX script and PortalWindow / PortalParticle / PortalDistort shaders.
/// </summary>
public class BuffRoomPedestal : MonoBehaviour
{
    /// <summary>Fired when the buff is picked up. Hook your real buff effect to this (the node is the chamber it came from).</summary>
    public static event System.Action<MapNode> BuffCollected;

    [Header("The buff on the slab")]
    [Tooltip("Optional. An empty object where the buff floats. If empty, it floats above the top of the slab.")]
    public Transform buffAnchor;
    [Tooltip("Size of the buff in the room, in meters (its longest side, orbiting shards included). Raise it if the buff looks small.")]
    [Min(0.1f)] public float buffSize = 1.8f;
    [Tooltip("Gap between the top of the slab and the bottom of the buff, in buff sizes.")]
    public float floatGap = 0.35f;
    [Tooltip("Degrees per second.")]
    public float spinSpeed = 50f;
    [Tooltip("Up-and-down bobbing, in buff sizes.")]
    public float bobHeight = 0.06f;
    public float bobSpeed = 1.6f;
    [Tooltip("Only used when no chamber is pending (for example when you test this room on its own).")]
    public GameObject fallbackBuff;

    [Header("Glow light on the buff")]
    [Tooltip("A light hanging just above the buff that makes it, and the slab around it, glow.")]
    public bool buffLight = true;
    [Tooltip("Brightness. Scales with Buff Size, so 4 always means about the same look. Raise it for a stronger glow.")]
    public float glowIntensity = 4f;
    [Tooltip("How far the glow reaches, in buff sizes.")]
    public float glowRange = 3f;
    [Tooltip("How high above the buff the light hangs, in buff sizes. Higher lights the top of the buff and the slab more.")]
    public float glowLift = 0.9f;
    [Tooltip("How far the light is pulled toward the camera, in buff sizes, so the buff itself is lit from the front.")]
    public float glowTowardCamera = 0.5f;
    [Tooltip("Slow breathing of the glow. 0 = steady.")]
    [Range(0f, 0.5f)] public float glowPulse = 0.15f;
    [Tooltip("Take the glow color from the buff model's own glow color. Untick to use Glow Color below.")]
    public bool autoGlowColor = true;
    public Color glowColor = new Color(0.75f, 1f, 0.85f);

    [Header("Player gains the buff (effect when picked up)")]
    [Tooltip("The buff breaks into little copies of itself that stream into the player, with a glow flash on the player. Untick for no built-in effect.")]
    public bool absorbEffect = true;
    [Range(0, 30)] public int absorbShardCount = 12;
    [Tooltip("Size of each little copy compared to the buff.")]
    [Range(0.05f, 0.6f)] public float absorbShardScale = 0.2f;
    [Tooltip("Seconds the streams take to reach the player.")]
    public float absorbDuration = 0.75f;
    [Tooltip("Brightness of the flash on the player when the buff lands.")]
    public float gainLightIntensity = 6f;
    public float gainLightRange = 5f;
    [Tooltip("Optional. Your own VFX prefab, spawned on the player when the first stream lands (it follows the player). Use this to add a custom 'gained something' look on top.")]
    public GameObject playerGainEffectPrefab;
    [Tooltip("Seconds before your VFX prefab is removed again.")]
    public float playerGainEffectLifetime = 4f;

    [Header("Player aura (swirling energy around the player)")]
    [Tooltip("When the buff lands, rings of energy pull into the player's body (your SoulRing shader), a shockwave and rising rings burst out, and a soft glow lingers (your PortalParticle shader).")]
    public BuffAuraSettings aura = new BuffAuraSettings();

    [Header("Pick up")]
    [Tooltip("How close the player has to be (meters, sideways).")]
    public float pickupRadius = 2.2f;
    [Tooltip("Pick up just by walking close, no button.")]
    public bool autoPickup = false;
    [Tooltip("Optional object of your own (for example a world-space button icon) shown while the player is in range. Leave empty for no prompt.")]
    public GameObject promptObject;
    public float pickupDuration = 0.7f;
    [Tooltip("Optional particle effect played where the buff disappears.")]
    public GameObject pickupEffectPrefab;

    [Header("Portal (opens behind the slab when the buff is taken)")]
    [Tooltip("Optional. A prefab with your PortalVFX on it (set Start Open to OFF on it). If empty, a portal is built in code.")]
    public GameObject portalPrefab;
    [Tooltip("Optional. Fixed spot for the portal; its blue arrow (+Z) must point toward where the player stands. If empty, the portal opens behind the slab, as seen from the player.")]
    public Transform portalAnchor;
    [Tooltip("How far behind the slab the portal opens (meters).")]
    public float portalDistance = 3f;
    [Tooltip("Height of the portal's center above the floor (meters).")]
    public float portalHeight = 2f;
    public float portalRadius = 1.5f;
    [Tooltip("Scene with the chamber selection map.")]
    public string returnScene = "Scene_Level";

    [Header("Portal shaders (only needed for built games)")]
    [Tooltip("Assign your portal shaders so they are included in builds (the Particle shader is also used by the player aura). In the editor they are found automatically.")]
    public Shader windowShader;
    public Shader particleShader;
    public Shader distortShader;
    [Tooltip("Your VFX/SoulRing shader, used for the rings of energy around the player. Assign it so it is included in builds.")]
    public Shader soulRingShader;

    [Header("Events")]
    public UnityEvent onPickedUp;

    private Bounds slab;
    private Transform buff;
    private Light lightComp;
    private Color activeGlow = Color.white;
    private Vector3 buffCenterNow;
    private bool absorbStarted;
    private bool auraSpawned;
    private Transform player;
    private float findTimer;

    private Vector3 buffOrigScale = Vector3.one;
    private Quaternion buffBaseRot = Quaternion.identity;
    private Vector3 buffLocalCenter;
    private float buffExtent = 1f;   // measured size of the model; the fit factor is worked out live from Buff Size

    private float appear;          // 0..1 pop-in when the room starts
    private float yaw;
    private float phase;
    private bool collected;
    private bool portalOpened;

    // ------------------------------------------------------------------ setup

    private void Start()
    {
        phase = Random.value * 6.28f;
        slab = ComputeSlabBounds();

        MapController mc = MapController.Instance;
        bool hasPending = mc != null && mc.PendingNode != null;

        GameObject copy = null;
        if (hasPending) copy = mc.CreatePendingBuff();
        if (copy == null && !hasPending && fallbackBuff != null) copy = Instantiate(fallbackBuff);

        if (copy != null)
        {
            SetupBuff(copy);
        }
        else if (hasPending)
        {
            // the chamber has no buff model: never leave the player stuck, complete it and open the exit
            Collect();
        }

        if (promptObject != null) promptObject.SetActive(false);
    }

    private Bounds ComputeSlabBounds()
    {
        bool any = false;
        Bounds b = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        if (!any) b = new Bounds(transform.position, new Vector3(1f, 0.2f, 1f));
        return b;
    }

    private void SetupBuff(GameObject copy)
    {
        copy.SetActive(true);
        buff = copy.transform;
        buff.SetParent(null, true);

        // the room's camera may only render certain layers
        SetLayerRecursive(copy, gameObject.layer);

        // a buff must never block the player
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        buffOrigScale = buff.localScale;
        buffBaseRot = buff.rotation;

        // measure it: size and visual center (so it spins around its middle, whatever its pivot)
        bool any = false;
        Bounds b = new Bounds(buff.position, Vector3.zero);
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;   // hidden parts must not count
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }

        float extent = 1f;
        buffLocalCenter = Vector3.zero;
        if (any)
        {
            extent = Mathf.Max(0.0001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            buffLocalCenter = Quaternion.Inverse(buffBaseRot) * (b.center - buff.position);
        }
        buffExtent = extent;

        if (buffLight)
        {
            GameObject lg = new GameObject("BuffLight");
            lg.transform.SetParent(transform, false);
            lightComp = lg.AddComponent<Light>();
            lightComp.type = LightType.Point;
            lightComp.intensity = 0f;
            lightComp.shadows = LightShadows.None;
            lg.layer = gameObject.layer;
        }

        activeGlow = ResolveGlowColor(copy);

        if (lightComp != null)
        {
            lightComp.color = activeGlow;
        }

        PlaceBuff(0f, 0f, 0f);
    }

    // ------------------------------------------------------------------ idle + pick up

    private void Update()
    {
        if (collected || buff == null) return;

        float dt = Time.deltaTime;
        appear = Mathf.MoveTowards(appear, 1f, dt / 0.7f);
        yaw += spinSpeed * dt;

        float pop = EaseOutBack(appear);
        float popSpin = 360f * (1f - EaseOutCubic(appear));
        PlaceBuff(pop, 0f, popSpin);

        if (lightComp != null) lightComp.intensity = GlowIntensity(appear);

        // is the player close enough?
        Transform p = GetPlayer(dt);
        bool near = false;
        if (p != null)
        {
            Vector3 c = CenterPoint();
            Vector2 d = new Vector2(p.position.x - c.x, p.position.z - c.z);
            near = d.magnitude <= pickupRadius;
        }

        bool canPick = near && appear > 0.9f;
        UpdatePrompt(canPick);

        if (canPick && (autoPickup || InteractPressed()))
            Collect();
    }

    private void UpdatePrompt(bool visible)
    {
        if (promptObject != null && promptObject.activeSelf != visible)
            promptObject.SetActive(visible);
    }

    private static bool InteractPressed()
    {
        // A on Xbox / Cross on PlayStation, or E on the keyboard
        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
        return false;
    }

    private Transform GetPlayer(float dt)
    {
        if (player == null)
        {
            findTimer -= dt;
            if (findTimer <= 0f)
            {
                findTimer = 0.5f;
                GameObject go = GameObject.FindWithTag("Player");
                if (go != null) player = go.transform;
            }
        }
        return player;
    }

    private Vector3 CenterPoint()
    {
        return buffAnchor != null ? buffAnchor.position : new Vector3(slab.center.x, slab.max.y, slab.center.z);
    }

    // puts the buff where it belongs. scaleMul 0..1(+), extraRise in meters, extraYaw in degrees
    private void PlaceBuff(float scaleMul, float extraRise, float extraYaw)
    {
        if (buff == null) return;

        float bob = Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight * buffSize;

        Vector3 center;
        if (buffAnchor != null) center = buffAnchor.position + Vector3.up * (bob + extraRise);
        else center = new Vector3(slab.center.x, slab.max.y + buffSize * (floatGap + 0.5f) + bob + extraRise, slab.center.z);

        Quaternion rot = Quaternion.AngleAxis(yaw + extraYaw, Vector3.up) * buffBaseRot;
        float fit = buffSize / Mathf.Max(0.0001f, buffExtent);   // live: Buff Size can be changed while playing
        float ratio = fit * Mathf.Max(0.0001f, scaleMul);

        buff.position = center - rot * (buffLocalCenter * ratio);
        buff.rotation = rot;
        buff.localScale = buffOrigScale * ratio;

        buffCenterNow = center;

        if (lightComp != null)
        {
            // hung above the buff and pulled toward the camera, so the buff itself is lit from the front
            Vector3 pos = center + Vector3.up * (buffSize * glowLift);
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - center;
                if (toCam.sqrMagnitude > 0.0001f) pos += toCam.normalized * (buffSize * glowTowardCamera);
            }
            lightComp.transform.position = pos;
            lightComp.range = Mathf.Max(0.1f, glowRange * buffSize);
            lightComp.color = activeGlow;
        }
    }

    // glow brightness; scaled by buff size squared because light fades with distance squared
    private float GlowIntensity(float multiplier)
    {
        float pulse = 1f + glowPulse * Mathf.Sin(Time.time * 2.3f + phase);
        return glowIntensity * buffSize * buffSize * pulse * multiplier;
    }

    // the color of the buff's own glow (its HDR / emission color), so the light matches the model
    private Color ResolveGlowColor(GameObject go)
    {
        if (!autoGlowColor) return glowColor;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            foreach (Material mat in r.sharedMaterials)
            {
                if (mat == null || mat.shader == null) continue;
                Shader sh = mat.shader;

                int count = sh.GetPropertyCount();
                for (int i = 0; i < count; i++)
                {
                    if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                    if ((sh.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.HDR) == 0) continue;

                    string propName = sh.GetPropertyName(i);
                    if (!mat.HasProperty(propName)) continue;

                    Color c = mat.GetColor(propName);
                    float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (m < 0.05f) continue;                         // black = unused glow color

                    return new Color(c.r / m, c.g / m, c.b / m, 1f); // keep the hue at full brightness
                }
            }
        }

        return glowColor;
    }

    // ------------------------------------------------------------------ collect

    private void Collect()
    {
        if (collected) return;
        collected = true;
        UpdatePrompt(false);

        MapController mc = MapController.Instance;
        MapNode node = mc != null ? mc.PendingNode : null;
        if (mc != null) mc.CompleteCurrentChamber();      // the chamber is done: greys out, next ones unlock

        BuffCollected?.Invoke(node);
        onPickedUp?.Invoke();

        OpenPortal();

        if (buff != null) StartCoroutine(PickupRoutine());
        else if (lightComp != null) Destroy(lightComp.gameObject);
    }

    private IEnumerator PickupRoutine()
    {
        float t = 0f;
        while (t < pickupDuration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / Mathf.Max(0.05f, pickupDuration));

            // swell for a moment, then shrink away while rising and spinning faster
            float scale = p < 0.3f
                ? Mathf.Lerp(1f, 1.3f, p / 0.3f)
                : Mathf.Lerp(1.3f, 0f, EaseInCubic((p - 0.3f) / 0.7f));

            yaw += spinSpeed * 6f * Time.deltaTime;
            PlaceBuff(scale, p * p * buffSize * 1.2f, 0f);

            if (lightComp != null) lightComp.intensity = GlowIntensity(1f + 2f * (1f - p));

            // at the peak of the swell the buff bursts into streams that fly into the player
            if (!absorbStarted && p >= 0.3f)
            {
                absorbStarted = true;
                if (absorbEffect)
                {
                    StartCoroutine(AbsorbRoutine());
                }
                else
                {
                    Transform pl = player;
                    if (pl == null)
                    {
                        GameObject go = GameObject.FindWithTag("Player");
                        if (go != null) pl = go.transform;
                    }
                    StartAura(pl);
                }
            }

            yield return null;
        }

        if (pickupEffectPrefab != null)
        {
            Vector3 at = buff != null ? buff.position : CenterPoint();
            GameObject fx = Instantiate(pickupEffectPrefab, at, Quaternion.identity);
            SetLayerRecursive(fx, gameObject.layer);
            Destroy(fx, 4f);
        }

        if (buff != null) Destroy(buff.gameObject);
        if (lightComp != null) Destroy(lightComp.gameObject);
    }

    // ------------------------------------------------------------------ player gains the buff

    private class Shard
    {
        public Transform tf;
        public Vector3 start;
        public Vector3 control;
        public Vector3 baseScale;
        public Vector3 spinAxis;
        public float spinSpeed;
        public float delay;
        public float duration;
        public bool done;
    }

    // The buff breaks into little copies of itself (so they look exactly like the buff) that curve through
    // the air into the player. Every landing flashes a light on the player, in the buff's glow color.
    private IEnumerator AbsorbRoutine()
    {
        Transform p = player;
        if (p == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) p = go.transform;
        }
        if (p == null || buff == null) yield break;

        Vector3 origin = buffCenterNow;
        Vector3 chest = ChestPoint(p);
        int count = Mathf.Max(0, absorbShardCount);

        List<Shard> shards = new List<Shard>();
        for (int i = 0; i < count; i++)
        {
            GameObject g = Instantiate(buff.gameObject);
            g.name = "BuffShard";
            g.transform.SetParent(null, true);
            SetLayerRecursive(g, gameObject.layer);

            Vector3 start = origin + Random.insideUnitSphere * (buffSize * 0.25f);
            Vector3 side = Vector3.Cross((chest - start).normalized, Vector3.up);
            if (side.sqrMagnitude < 0.01f) side = Vector3.right;
            side.Normalize();

            Shard s = new Shard();
            s.tf = g.transform;
            s.start = start;
            s.control = Vector3.Lerp(start, chest, 0.5f)
                      + Vector3.up * Random.Range(0.2f, 1.1f)
                      + side * Random.Range(-1.2f, 1.2f);
            s.baseScale = buff.localScale * absorbShardScale;
            s.spinAxis = Random.onUnitSphere;
            s.spinSpeed = Random.Range(180f, 540f);
            s.delay = Random.Range(0f, 0.35f);
            s.duration = Mathf.Max(0.1f, absorbDuration) * Random.Range(0.8f, 1.15f);

            s.tf.position = start;
            s.tf.rotation = Random.rotation;
            s.tf.localScale = Vector3.one * 0.0001f;      // invisible until its turn
            shards.Add(s);
        }

        if (shards.Count == 0) StartAura(p);     // no streams to wait for

        // flash on the player
        Light flash = null;
        if (gainLightIntensity > 0f)
        {
            GameObject fo = new GameObject("BuffGainLight");
            fo.layer = p.gameObject.layer;
            flash = fo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = activeGlow;
            flash.range = Mathf.Max(0.1f, gainLightRange);
            flash.shadows = LightShadows.None;
            flash.intensity = 0f;
        }

        float flashAmount = 0f;
        bool effectSpawned = false;
        int remaining = shards.Count;
        float t = 0f;

        while ((remaining > 0 || flashAmount > 0.01f) && p != null)
        {
            float dt = Time.deltaTime;
            t += dt;
            Vector3 target = ChestPoint(p);          // follows the player if they move

            foreach (Shard s in shards)
            {
                if (s.done) continue;
                if (s.tf == null) { s.done = true; remaining--; continue; }

                float k = (t - s.delay) / s.duration;
                if (k < 0f) continue;

                if (k >= 1f)
                {
                    // landed
                    s.done = true;
                    remaining--;
                    Destroy(s.tf.gameObject);
                    flashAmount = 1f;
                    if (!effectSpawned)
                    {
                        effectSpawned = true;
                        SpawnPlayerEffect(p, target);
                    }
                    continue;
                }

                float e = k * k * 0.75f + k * 0.25f;   // gentle start, speeds up toward the player
                Vector3 a = Vector3.Lerp(s.start, s.control, e);
                Vector3 b = Vector3.Lerp(s.control, target, e);
                s.tf.position = Vector3.Lerp(a, b, e);
                s.tf.Rotate(s.spinAxis, s.spinSpeed * dt, Space.World);

                float grow = Mathf.Clamp01(k * 6f);
                float shrink = 1f - 0.75f * k;
                s.tf.localScale = s.baseScale * Mathf.Max(0.0001f, grow * shrink);
            }

            flashAmount = Mathf.Max(0f, flashAmount - dt * 1.4f);

            if (flash != null)
            {
                // in front of the player and a little above, so the flash lights the player's front
                Vector3 pos = target + Vector3.up * 0.3f;
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - target;
                    if (toCam.sqrMagnitude > 0.0001f) pos += toCam.normalized * 0.9f;
                }
                flash.transform.position = pos;
                flash.intensity = gainLightIntensity * flashAmount;
            }

            yield return null;
        }

        foreach (Shard s in shards)
            if (s.tf != null) Destroy(s.tf.gameObject);
        if (flash != null) Destroy(flash.gameObject);
    }

    // the swirling aura around the player, in the buff's glow color (or the aura's own color)
    private void StartAura(Transform p)
    {
        if (auraSpawned || p == null || aura == null || !aura.enabled) return;
        auraSpawned = true;

        Color c = aura.useBuffColor ? activeGlow : aura.color;
        PlayerBuffAura.Spawn(p, c, particleShader, soulRingShader, aura);
    }

    private void SpawnPlayerEffect(Transform p, Vector3 at)
    {
        StartAura(p);

        if (playerGainEffectPrefab == null) return;

        GameObject fx = Instantiate(playerGainEffectPrefab, at, Quaternion.identity, p);
        Destroy(fx, Mathf.Max(0.1f, playerGainEffectLifetime));
    }

    private static Vector3 ChestPoint(Transform p)
    {
        CharacterController cc = p.GetComponentInChildren<CharacterController>();
        if (cc != null) return cc.bounds.center;
        return p.position + Vector3.up * 1.1f;
    }

    // ------------------------------------------------------------------ portal

    private void OpenPortal()
    {
        if (portalOpened) return;
        portalOpened = true;

        Transform p = player != null ? player : (GameObject.FindWithTag("Player") != null ? GameObject.FindWithTag("Player").transform : null);

        Vector3 pos;
        Quaternion rot;

        if (portalAnchor != null)
        {
            pos = portalAnchor.position;
            rot = portalAnchor.rotation;
        }
        else
        {
            // "behind the slab" as seen from the player, floating in the air, facing the player
            Vector3 center = new Vector3(slab.center.x, slab.min.y, slab.center.z);
            Vector3 dir = p != null ? center - p.position : transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.Normalize();

            pos = center + dir * portalDistance + Vector3.up * portalHeight;
            rot = Quaternion.LookRotation(-dir, Vector3.up);   // +Z (blue arrow) points at the player
        }

        GameObject go;
        PortalVFX vfx;

        if (portalPrefab != null)
        {
            go = Instantiate(portalPrefab, pos, rot);
            vfx = go.GetComponentInChildren<PortalVFX>(true);
        }
        else
        {
            // built in code from your PortalVFX. Inactive while we set it up, so its Awake sees our values.
            go = new GameObject("BuffRoomPortal");
            go.SetActive(false);
            go.transform.SetPositionAndRotation(pos, rot);

            vfx = go.AddComponent<PortalVFX>();
            vfx.startOpen = false;
            vfx.radius = portalRadius;
            if (windowShader != null) vfx.windowShader = windowShader;
            if (particleShader != null) vfx.particleShader = particleShader;
            if (distortShader != null) vfx.distortShader = distortShader;

            go.SetActive(true);
        }

        float radius = vfx != null ? vfx.radius : portalRadius;
        if (vfx != null)
        {
            if (p != null) vfx.reactTo = p;
            vfx.Open();                           // plays its opening animation
            StartCoroutine(SurgeWhenOpen(vfx));
        }

        // walk-through trigger
        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(radius * 1.7f, radius * 2f + 0.6f, 1.4f);
            box.center = new Vector3(0f, -0.3f, 0f);
            col = box;
        }
        col.isTrigger = true;

        BuffRoomPortalExit exit = go.GetComponent<BuffRoomPortalExit>();
        if (exit == null) exit = go.AddComponent<BuffRoomPortalExit>();
        exit.returnScene = returnScene;
        exit.armDelay = vfx != null ? vfx.openTime * 0.6f : 0.5f;
    }

    private IEnumerator SurgeWhenOpen(PortalVFX vfx)
    {
        yield return new WaitForSeconds(vfx.openTime + 0.1f);
        if (vfx != null) vfx.TriggerSurge();      // one big flare once it is fully open
    }

    // ------------------------------------------------------------------ helpers

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    private static float EaseOutCubic(float p)
    {
        p = 1f - p;
        return 1f - p * p * p;
    }

    private static float EaseInCubic(float p)
    {
        return p * p * p;
    }

    private static float EaseOutBack(float p)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float x = p - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }
}