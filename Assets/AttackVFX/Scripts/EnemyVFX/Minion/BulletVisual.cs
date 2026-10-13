using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of an enemy bullet, made to look like the crystal lances of a magic spell: a slim SHARD that points the way it flies,
/// a BUNDLE of thin glowing streaks behind it that wave a little and fade out, and tiny twinkling STAR sparkles in its wake.
/// Put it on every BULLET PATTERN PREFAB (the prefab that has the BulletPattern component, for example WaveBullet;
/// the BulletManager clones it for every bullet).
///
/// Works with pooled bullets: everything resets when a bullet is (re)spawned, so there are no streaks from the old pool position.
/// The original sprite/mesh of the bullet is switched off (Hide Original Look).
/// For a bullet-hell wave with hundreds of bullets, 'Max Bullets With Streaks' limits how many bullets get streaks and sparkles at once.
///
/// EDITING IN PLAY MODE: the bullets are clones of the prefab, made when they were first fired. Changing the PREFAB while the game runs
/// does not change clones that already exist. To tune the look live, select one of the bullets in the Hierarchy (they are named like
/// the prefab with "(Clone)"); the bullet rebuilds itself when you change a value. To change all bullets, stop Play mode and edit the prefab.
/// </summary>
public class BulletVisual : MonoBehaviour
{
    public enum Shape { Shard, Comet, Orb, Star, None }

    [Header("Shape")]
    [Tooltip("Shard = a slim crystal that points the way it flies. Comet = a bright head with a soft tail. Orb / Star = round shapes. None = no shape: only streaks, sparkles and the pop are added, and your own sprite stays.")]
    public Shape shape = Shape.Shard;
    [Tooltip("The size follows the Hit Radius and Scale of the BulletPattern on this prefab, so what the player sees matches what hurts.")]
    public bool matchHitbox = true;
    [Tooltip("Hit diameter in world units. Only used when 'Match Hitbox' is off.")]
    public float hitDiameter = 1f;
    [Tooltip("Shard: length compared to the hit diameter.")]
    public float shardLength = 1.2f;
    [Tooltip("Shard: width compared to its length (slim = small).")]
    [Range(0.1f, 0.6f)] public float shardThickness = 0.32f;
    [Tooltip("Comet: length compared to the hit diameter.")]
    public float cometLength = 1.6f;
    [Tooltip("Orb and Star: size compared to the hit diameter.")]
    public float roundSize = 1.2f;
    [Tooltip("Star only: how fast it spins (degrees per second). The shard uses it to roll around its length, so its facets flash.")]
    public float spinSpeed = 120f;
    [Tooltip("How much it pulses in size (0 = steady).")]
    [Range(0f, 0.3f)] public float pulse = 0.06f;
    [Tooltip("Shard and Comet: the long side follows the direction the bullet moves ON THE SCREEN, and the shape always faces the camera. This keeps it looking the same, and lined up with its streaks, whichever way the bullet flies (including toward the camera).")]
    public bool alignOnScreen = true;

    [Header("Colors (violet-blue crystal with white glints)")]
    public Color bodyColor = new Color(0.62f, 0.58f, 1f, 1f);
    public Color streakColor = new Color(0.35f, 0.6f, 1f, 1f);
    public Color glintColor = new Color(0.92f, 0.96f, 1f, 1f);
    [Tooltip("HDR brightness of the shard. Keep it low (about 1 to 1.5), or it burns out to white.")]
    public float bodyBrightness = 1.3f;
    [Tooltip("HDR brightness of the streaks. With Bloom on, higher = a stronger glow.")]
    public float streakBrightness = 2f;
    [Tooltip("Bullets get a color from what they do, so the player can read them: homing bullets and piercing bullets look different.")]
    public bool colorByModifier = true;
    public Color homingColor = new Color(1f, 0.45f, 0.85f, 1f);
    public Color pierceColor = new Color(1f, 0.95f, 0.6f, 1f);

    [Header("Streaks (the bundle of thin lines behind the bullet)")]
    public bool showStreaks = true;
    [Range(1, 5)] public int streakCount = 3;
    [Tooltip("How long the longest streak lasts, in seconds. At 12 meters per second, 0.5 seconds is a 6 meter streak.")]
    public float streakTime = 0.5f;
    [Tooltip("Width of the main streak at the start, in world units. The others are thinner.")]
    public float streakWidth = 0.07f;
    [Tooltip("How far apart the streaks end up (compared to the hit diameter). They start together at the muzzle and fan out as the bullet flies, like the lines of a spell.")]
    public float streakSpread = 0.4f;
    [Tooltip("How far the streaks wave from side to side (compared to the hit diameter). 0 = straight streaks.")]
    public float wobble = 0f;
    [Tooltip("Seconds after the shot until the streaks have fanned out completely.")]
    public float fanOutTime = 0.45f;
    public float wobbleSpeed = 7f;
    [Tooltip("At most this many bullets have streaks and sparkles at the same time (performance for big bullet waves).")]
    public int maxBulletsWithStreaks = 120;

    [Header("Sparkles (tiny stars in the air behind it)")]
    public bool sparkles = true;
    public float sparklesPerSecond = 20f;
    [Tooltip("A bigger flare of light now and then at the tip.")]
    public bool tipFlares = true;

    [Header("End of the bullet")]
    public bool popOnEnd = true;
    public float popSize = 1f;
    [Tooltip("Bullets that live shorter than this do not pop (so a bullet that is just being recycled stays quiet).")]
    public float minLifeForPop = 0.15f;

    [Header("Original look")]
    [Tooltip("Switches off the meshes, sprites and trails the bullet prefab already has, so only this look is shown.")]
    public bool hideOriginalLook = true;

    private static Mesh quadMesh;
    private static Material streakMat;
    private static readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
    private static Camera cam;
    private static int camFrame = -1000;
    private static int activeStreakBullets;

    private Transform head;
    private TrailRenderer[] streaks;
    private float[] streakBase;      // sideways start offset of each streak
    private bool built, hasStreaks, originalHidden, rebuildPending;
    private float alive, phase, angle, sparkleAcc, flareTimer, patternLifetime, diameter, length;
    private int startDelay;
    private Transform holder;
    private Vector2 screenDir = Vector2.up;
    private Vector3 spawnPos;
    private bool spawnSet;
    private static Transform container;
    private Vector3 prevPos, moveDir = Vector3.forward;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        mats.Clear();
        streakMat = null;
        quadMesh = null;
        cam = null;
        activeStreakBullets = 0;
        container = null;
    }

    private void Awake()
    {
        EnsureBuilt();
    }

    private void OnEnable()
    {
        EnsureBuilt();
        if (holder != null) holder.gameObject.SetActive(true);
        BulletImpactFX.EnsureCreated();
        BeginLife();
    }

    private void BeginLife()
    {
        alive = 0f;
        sparkleAcc = 0f;
        prevPos = transform.position;
        moveDir = transform.forward;
        spawnSet = false;
        flareTimer = Random.Range(0.3f, 0.55f);   // no flares at the muzzle: ten of them on one spot are a white starburst
        phase = Random.value * 6.2832f;
        angle = Random.value * 360f;

        hasStreaks = showStreaks && activeStreakBullets < maxBulletsWithStreaks;
        if (hasStreaks) activeStreakBullets++;
        ClearStreaks();

        // a pool may move the bullet AFTER enabling it, so the streaks only start a couple of frames later
        startDelay = 2;
        if (head != null) head.gameObject.SetActive(false);   // the shape appears once the bullet is in place
    }

    private void OnDisable()
    {
        if (!built) return;

        if (hasStreaks)
        {
            activeStreakBullets = Mathf.Max(0, activeStreakBullets - 1);
            hasStreaks = false;
        }
        ClearStreaks();
        if (holder != null) holder.gameObject.SetActive(false);

        if (popOnEnd && alive >= minLifeForPop && gameObject.scene.isLoaded)
        {
            // a bullet that simply ran out of time fizzles quietly; a bullet that hit the player or a wall pops fully
            bool expired = patternLifetime > 0f && alive >= patternLifetime - 0.1f;
            BulletImpactFX.Pop(transform.position, streakColor, glintColor, popSize * diameter * (expired ? 0.5f : 1f), expired ? 0.3f : 1f);
        }
    }

    private void ClearStreaks()
    {
        if (streaks == null) return;
        for (int i = 0; i < streaks.Length; i++)
        {
            streaks[i].Clear();
            streaks[i].emitting = false;
        }
    }

    private void OnDestroy()
    {
        if (holder != null) Destroy(holder.gameObject);
    }

    // editing a value in Play mode rebuilds this bullet, so every setting works live
    private void OnValidate()
    {
        if (Application.isPlaying && built) rebuildPending = true;
    }

    private void Rebuild()
    {
        if (hasStreaks)
        {
            activeStreakBullets = Mathf.Max(0, activeStreakBullets - 1);
            hasStreaks = false;
        }
        if (holder != null) Destroy(holder.gameObject);
        holder = null;
        head = null;
        streaks = null;
        built = false;
        EnsureBuilt();
        BeginLife();
    }

    private void LateUpdate()
    {
        if (!built) return;
        if (rebuildPending)
        {
            rebuildPending = false;
            Rebuild();
            return;
        }

        float dt = Time.deltaTime;
        alive += dt;

        if (startDelay > 0)
        {
            startDelay--;
            if (startDelay == 0)
            {
                if (head != null) head.gameObject.SetActive(true);
                if (streaks != null)
                {
                    for (int i = 0; i < streaks.Length; i++)
                    {
                        streaks[i].Clear();
                        streaks[i].emitting = hasStreaks;
                    }
                }
            }
        }

        // The direction the bullet REALLY moves, worked out from its movement. (The bullet's own rotation can differ from its path,
        // for example while it glides down to the player's height, and then the shape and the streaks would not line up.)
        Vector3 pos = transform.position;
        if (startDelay == 0)
        {
            Vector3 delta = pos - prevPos;
            if (delta.sqrMagnitude > 0.000001f) moveDir = Vector3.Slerp(moveDir, delta.normalized, 0.6f);
        }
        else
        {
            moveDir = transform.forward;
        }
        prevPos = pos;
        Vector3 fwd = moveDir;
        if (holder != null) holder.position = pos;
        if (startDelay == 0 && !spawnSet)
        {
            spawnPos = pos;   // where the bullet really started (the pool may have moved it after enabling it)
            spawnSet = true;
        }

        if (hasStreaks && startDelay == 0)
        {
            // the streaks start a little apart and wave, so over time they curve and cross like the lines of a spell
            // They start together at the muzzle and fan out with the age of the bullet, so the bundle converges where the shot began.
            float fan = Mathf.Clamp01(alive / Mathf.Max(0.05f, fanOutTime));
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            if (right.sqrMagnitude < 0.0001f) right = transform.right;
            right.Normalize();
            Vector3 upv = Vector3.Cross(fwd, right);
            for (int i = 0; i < streaks.Length; i++)
            {
                float side = (streakBase[i] + Mathf.Sin(Time.time * wobbleSpeed + phase + i * 1.7f) * wobble * diameter) * fan;
                float up = Mathf.Cos(Time.time * wobbleSpeed * 0.8f + phase + i * 2.3f) * wobble * 0.5f * diameter * fan;
                // the first streak starts inside the shard, so head and streaks are one thing
                float back = i == 0 ? 0.2f : 0.4f;
                streaks[i].transform.position = pos - fwd * length * back + right * side + upv * up;
            }

            if (sparkles && alive > 0.08f)
            {
                // tiny stars in the air behind the bullet, along its streaks
                sparkleAcc += sparklesPerSecond * dt;
                while (sparkleAcc >= 1f)
                {
                    sparkleAcc -= 1f;
                    Vector3 p = pos - fwd * Random.Range(0f, length * 2.2f)
                              + Random.insideUnitSphere * diameter * 0.2f;
                    BulletImpactFX.Sparkle(p, Random.value < 0.55f ? glintColor : streakColor);
                }
            }

            if (tipFlares)
            {
                flareTimer -= dt;
                if (flareTimer <= 0f)
                {
                    flareTimer = Random.Range(0.12f, 0.3f);
                    BulletImpactFX.Sparkle(pos + fwd * length * 0.5f, glintColor, 2.2f);
                }
            }
        }

        if (head == null) return;   // Shape None

        if (cam == null || Time.frameCount - camFrame > 60)
        {
            cam = Camera.main;
            camFrame = Time.frameCount;
        }
        Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
        Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.forward;

        // the shape grows in over the first moments, so a whole fan of bullets that starts on one spot never flashes as one big star
        float g = Mathf.Clamp01(alive / 0.12f);
        g = g * g * (3f - 2f * g);
        float p2 = (1f + pulse * Mathf.Sin(Time.time * 12f + phase)) * (0.35f + 0.65f * g);
        angle += spinSpeed * dt;

        if (shape == Shape.Shard || shape == Shape.Comet)
        {
            float stretch = 1f;
            if (alignOnScreen && cam != null)
            {
                // Aligned on the SCREEN: the long side follows the direction the bullet moves on the screen, and the face always
                // looks at the camera. (A shape lying in 3D space turns thin or short when the bullet flies toward the camera,
                // and then it no longer matches its streaks.)
                Vector3 sa = cam.WorldToScreenPoint(pos);
                Vector3 sb = cam.WorldToScreenPoint(pos + fwd);
                Vector2 sd = new Vector2(sb.x - sa.x, sb.y - sa.y);
                if (sa.z > 0f && sd.sqrMagnitude > 0.0001f) screenDir = sd.normalized;

                float sideways = (cam.WorldToScreenPoint(pos + cam.transform.right) - sa).magnitude;
                float ratio = Mathf.Clamp01(sd.magnitude / Mathf.Max(0.0001f, sideways));   // 1 = across the screen, 0 = straight at the camera
                stretch = Mathf.Min(1.6f, Mathf.Lerp(1f, 1f / Mathf.Max(0.35f, ratio), 0.7f));   // flying toward the camera, the shape would look short

                head.rotation = camRot * Quaternion.Euler(0f, 0f, Mathf.Atan2(screenDir.y, screenDir.x) * Mathf.Rad2Deg - 90f);
            }
            else
            {
                Vector3 z = Vector3.ProjectOnPlane(camFwd, fwd);
                head.rotation = z.sqrMagnitude > 0.01f ? Quaternion.LookRotation(z.normalized, fwd) : camRot;
            }

            // The shard EMERGES from the muzzle: at the start it lies entirely AHEAD of the bullet (nothing sticks out behind the
            // muzzle), and it settles around the bullet as it flies. A whole fan of bullets that starts on one spot then looks
            // like a burst of lances, not a star that points in every direction.
            float emerge = spawnSet ? Mathf.Clamp01(Vector3.Distance(pos, spawnPos) / Mathf.Max(0.1f, length * 0.8f)) : 0f;
            emerge = emerge * emerge * (3f - 2f * emerge);
            head.position = pos + fwd * (length * stretch * 0.5f * (1f - emerge));

            if (shape == Shape.Shard)
            {
                // it rolls around its length, so the facets flash
                float roll = 0.7f + 0.3f * Mathf.Abs(Mathf.Cos(angle * 0.0174533f * 0.5f));
                head.localScale = new Vector3(length * shardThickness * roll * p2, length * stretch * p2, 1f);
            }
            else
            {
                head.localScale = new Vector3(length * 0.33f * p2, length * stretch * p2, 1f);
            }
        }
        else
        {
            Quaternion face = camRot;
            if (shape == Shape.Star) face *= Quaternion.AngleAxis(angle, Vector3.forward);
            head.rotation = face;
            head.localScale = new Vector3(length * p2, length * p2, length * p2);
        }
    }

    // ------------------------------------------------------------------ build
    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        if (hideOriginalLook && shape != Shape.None && !originalHidden)
        {
            originalHidden = true;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // learn from the BulletPattern on this prefab (the manager removes it from its clones only after Awake has run)
        BulletPattern pat = GetComponent<BulletPattern>();
        if (pat != null)
        {
            patternLifetime = pat.lifetime;
            if (matchHitbox) hitDiameter = Mathf.Max(0.3f, pat.hitRadius * 2f * Mathf.Max(0.01f, pat.scale.x));
            if (colorByModifier)
            {
                if (pat.homingStrength > 0f) bodyColor = streakColor = homingColor;
                else if (pat.pierce > 0) bodyColor = streakColor = pierceColor;
            }
        }
        diameter = hitDiameter;
        switch (shape)
        {
            case Shape.Shard: length = diameter * shardLength; break;
            case Shape.Comet: length = diameter * cometLength; break;
            default: length = diameter * roundSize; break;
        }

        if (quadMesh == null) quadMesh = MakeQuad();
        if (streakMat == null) streakMat = HealVFXAssets.CreateMaterial(HealVFXAssets.Ribbon, streakBrightness);

        // The shape and the streaks live on their own object, NOT under the bullet: the scale or rotation of the bullet prefab
        // can then never squash or skew them.
        if (container == null) container = new GameObject("BulletVisuals").transform;
        holder = new GameObject("BulletVisual").transform;
        holder.SetParent(container, false);
        holder.position = transform.position;

        if (shape != Shape.None)
        {
            GameObject go = new GameObject("BulletHead");
            head = go.transform;
            head.SetParent(holder, false);
            AddQuad(go, HeadMaterial(false));

            // the bright part of a shard (a ridge and a glint at the tip), drawn on top in white
            if (shape == Shape.Shard)
            {
                GameObject cg = new GameObject("BulletCore");
                cg.transform.SetParent(head, false);
                cg.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                AddQuad(cg, HeadMaterial(true));
            }
        }

        // the streaks: the first one is the main one (white at the head, blue behind), the others are thinner and bluer
        int n = Mathf.Clamp(streakCount, 1, 5);
        streaks = new TrailRenderer[n];
        streakBase = new float[n];
        for (int i = 0; i < n; i++)
        {
            bool main = i == 0;
            float time = streakTime * (main ? 1f : Mathf.Lerp(0.85f, 0.55f, i / (float)n));
            float width = streakWidth * (main ? 1f : 0.65f);
            streaks[i] = MakeStreak("BulletStreak" + i, main ? Color.Lerp(glintColor, streakColor, 0.5f) : streakColor, main ? streakColor : bodyColor, width, time);
            int step = (i + 1) / 2;                            // 0, 1, 1, 2, 2
            streakBase[i] = (i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f) * step) * streakSpread * diameter * 0.5f;
        }
    }

    private void AddQuad(GameObject go, Material material)
    {
        go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private TrailRenderer MakeStreak(string name, Color headColor, Color tailColor, float width, float time)
    {
        GameObject tg = new GameObject(name);
        tg.transform.SetParent(holder, false);
        TrailRenderer t = tg.AddComponent<TrailRenderer>();
        t.sharedMaterial = streakMat;
        t.time = time;
        t.minVertexDistance = 0.08f;
        t.numCapVertices = 2;
        t.alignment = LineAlignment.View;
        t.textureMode = LineTextureMode.Stretch;
        t.widthMultiplier = width;
        t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.25f, 0.6f), new Keyframe(1f, 0f));
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(headColor, 0f), new GradientColorKey(tailColor, 0.5f), new GradientColorKey(tailColor, 1f) },
            new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.5f, 0.45f), new GradientAlphaKey(0f, 1f) });
        t.colorGradient = g;
        t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        t.receiveShadows = false;
        t.emitting = false;
        return t;
    }

    // one shared material per shape and color, so thousands of bullets still batch
    private Material HeadMaterial(bool core)
    {
        Color32 c = core ? glintColor : bodyColor;
        int key = ((int)shape << 26) ^ (c.r << 16) ^ (c.g << 8) ^ c.b ^ ((int)(bodyBrightness * 10f) << 20) ^ (core ? 0x2000000 : 0);
        Material m;
        if (mats.TryGetValue(key, out m) && m != null) return m;

        Texture2D tex;
        if (core) tex = HealVFXAssets.BulletShardCore;
        else
        {
            switch (shape)
            {
                case Shape.Comet: tex = HealVFXAssets.BulletComet; break;
                case Shape.Orb: tex = HealVFXAssets.BulletOrb; break;
                case Shape.Star: tex = HealVFXAssets.BulletStar; break;
                default: tex = HealVFXAssets.BulletShard; break;
            }
        }

        float b = core ? bodyBrightness * 1.4f : bodyBrightness;
        Color col = core ? glintColor : bodyColor;
        m = HealVFXAssets.CreateMaterial(tex, b);
        Color tint = new Color(col.r * b, col.g * b, col.b * b, 1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint * 0.5f);
        mats[key] = m;
        return m;
    }

    // a quad that can be seen from both sides
    private static Mesh MakeQuad()
    {
        Mesh m = new Mesh();
        m.name = "BulletQuad";
        m.hideFlags = HideFlags.HideAndDontSave;
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }
}