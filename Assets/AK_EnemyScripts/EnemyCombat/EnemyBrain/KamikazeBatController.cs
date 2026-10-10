using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class KamikazeBatController : MonoBehaviour, IDamageable
{
    public enum BatState { Spawning, Chasing, Fusing, Dead }
    public enum MirrorAxis { X, Y, Z }

    [Header("Core References")]
    [Tooltip("Auto-caches statically. You do not need to assign this manually for every bat.")]
    public PlayerController player;
    private Transform cachedTransform;
    private Transform playerTransform;

    private static PlayerController globalCachedPlayer;

    [Header("Stats")]
    public float maxHealth = 30f;
    private float currentHealth;

    [Header("Movement")]
    public float chaseSpeed = 5.0f;
    public float rotationSpeed = 10f;

    [Header("Hovering")]
    [Tooltip("How high above the ground/player the bat tries to fly.")]
    public float hoverHeightOffset = 1.5f;
    [Tooltip("How fast the bat climbs to its hover height when it spawns.")]
    public float floatSpeed = 4.0f;
    [Tooltip("How far (in units) the bat drifts up and down from its hover height. Raise this if the bobbing is too subtle.")]
    public float floatAmplitude = 0.5f;
    [Tooltip("How fast the bat bobs up and down (radians per second). 2.5 is a slow, lazy float; 5+ is jittery.")]
    public float bobFrequency = 2.5f;
    [Tooltip("Seconds it takes the bobbing to fade in after spawning, so the bat never pops.")]
    public float hoverBlendTime = 0.6f;

    [Header("Polish: Visuals & Wings")]
    [Tooltip("The wing holder (WingOObject). If it is ONE mesh containing both mirrored wings, it is split into a left and a right wing at runtime so each spins around its own centre.")]
    public Transform wingObject;
    [Tooltip("OPTIONAL. If you already have the wings as separate objects, assign each one here and auto-detection is skipped. Each entry spins around its own centre.")]
    public Transform[] individualWings;
    public float normalWingSpinSpeed = 180f;
    public float fuseWingSpinSpeed = 1200f;
    [Tooltip("Spin axis in the bat's local space. (0,1,0) = flat, helicopter-style rotor.")]
    public Vector3 wingSpinAxis = Vector3.up;
    [Tooltip("Left and right wings spin in opposite directions.")]
    public bool counterRotateWings = false;
    [Tooltip("Which local axis separates the left wing from the right wing. X for a normal bat facing +Z.")]
    public MirrorAxis wingMirrorAxis = MirrorAxis.X;
    private float currentWingSpinSpeed;

    [Header("Kamikaze & Explosion Parameters")]
    [Tooltip("Distance to player required to trigger the explosion fuse.")]
    public float fuseTriggerDistance = 2.0f;
    [Tooltip("How long the bat waits before exploding once in range.")]
    public float fuseDuration = 1.5f;
    public float explosionRadius = 3.5f;
    public float explosionDamage = 40f;
    public float explosionKnockback = 4.0f;

    [Header("Player Reaction Hook")]
    public string playerReactionAnimName = "Hit_Heavy";
    public float stunDuration = 0.5f;

    [Header("Swarm Token Rules")]
    [Tooltip("If false, this enemy ignores token limits and swarms the player.")]
    public bool requiresTokenToFuse = false;
    public TokenType tokenType = TokenType.AgileFlanker;
    private bool holdsToken = false;

    [Header("VFX & Audio (Local)")]
    public ParticleSystem explosionVFXPrefab;
    public AudioClip explosionSound;
    public AudioClip fuseTickSound;
    private AudioSource audioSource;

    public Color fuseFlashColor = Color.red;

    private Renderer[] batRenderers;
    private MaterialPropertyBlock propBlock;

    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    private static readonly Collider[] explosionBuffer = new Collider[16];

    private BatState currentState = BatState.Spawning;
    private Coroutine activeFuseRoutine;
    private Coroutine activeKnockbackRoutine;

    // ---- Hover bookkeeping -------------------------------------------------
    // baseY is the bat's "logical" altitude: all gameplay (spawn climb, chase, fuse,
    // distance checks) reads and writes this value. The visible bobbing is added on top
    // of it at the very end of every frame, so it can never confuse the state machine.
    private float baseY;
    private float targetSpawnY;
    private float hoverWeight;
    private bool needsSpawnInit;

    private float randomizedSpeed;
    private float timeOffset;

    // ---- Wing rotor bookkeeping --------------------------------------------
    private class WingRotor
    {
        public Transform pivot;
        public Quaternion restRotation;
        public float direction = 1f;
        public float angle;
    }

    private struct WingPart
    {
        public Transform transform;
        public Bounds bounds;
    }

    private class SplitMeshPair
    {
        public Mesh negative;
        public Mesh positive;
    }

    private readonly List<WingRotor> wingRotors = new List<WingRotor>();
    private Vector3 spinAxis = Vector3.up;

    // Shared between every bat that uses the same mesh, so pooled bats don't re-split it.
    private static readonly Dictionary<(int, int, int), SplitMeshPair> splitMeshCache =
        new Dictionary<(int, int, int), SplitMeshPair>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        splitMeshCache.Clear();
        globalCachedPlayer = null;
    }

    // ========================================================================
    // Lifecycle
    // ========================================================================

    private void Awake()
    {
        cachedTransform = transform;
        audioSource = GetComponent<AudioSource>();

        // Wings must be set up BEFORE the renderers are cached, so any wing halves
        // created here also take part in the fuse colour flash.
        SetupWings();

        batRenderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();

        FindPlayerReference();
        timeOffset = Random.Range(0f, 100f);
    }

    private void OnEnable()
    {
        currentHealth = maxHealth;
        currentState = BatState.Spawning;
        holdsToken = false;
        activeFuseRoutine = null;
        activeKnockbackRoutine = null;

        randomizedSpeed = chaseSpeed * Random.Range(0.85f, 1.15f);
        currentWingSpinSpeed = normalWingSpinSpeed;

        // The spawn altitude is captured on the first Update instead of here, so it still works
        // if a pool/spawner positions the bat AFTER activating it.
        needsSpawnInit = true;
        hoverWeight = 0f;

        for (int i = 0; i < wingRotors.Count; i++)
        {
            WingRotor rotor = wingRotors[i];
            rotor.angle = 0f;
            if (rotor.pivot != null) rotor.pivot.localRotation = rotor.restRotation;
        }

        ResetColors();
        FindPlayerReference();
    }

    private void OnDisable()
    {
        if (holdsToken && GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseToken(cachedTransform, tokenType);
            holdsToken = false;
        }
    }

    private void FindPlayerReference()
    {
        if (playerTransform != null) return;

        if (globalCachedPlayer == null)
        {
            globalCachedPlayer = player != null ? player : FindObjectOfType<PlayerController>();
        }

        player = globalCachedPlayer;
        if (player != null) playerTransform = player.transform;
    }

    // ========================================================================
    // Main loop
    // ========================================================================

    private void Update()
    {
        if (currentState == BatState.Dead) return;

        if (needsSpawnInit)
        {
            needsSpawnInit = false;
            baseY = cachedTransform.position.y;
            targetSpawnY = baseY + hoverHeightOffset;
        }

        float dt = Time.deltaTime;

        SpinWings(dt);

        if (hoverBlendTime > 0f) hoverWeight = Mathf.MoveTowards(hoverWeight, 1f, dt / hoverBlendTime);
        else hoverWeight = 1f;

        if (playerTransform != null)
        {
            if (currentState == BatState.Spawning)
            {
                // Smooth vertical ascension to the target hover height (logical height, not the bobbing one)
                baseY = Mathf.MoveTowards(baseY, targetSpawnY, floatSpeed * dt);

                // Transition to chase only when it reaches the apex
                if (Mathf.Abs(baseY - targetSpawnY) < 0.01f)
                {
                    currentState = BatState.Chasing;
                }
            }
            else if (currentState == BatState.Chasing)
            {
                Vector3 playerPos = playerTransform.position;
                Vector3 currentPos = cachedTransform.position;
                // Use the bat's logical height so the bobbing never affects the fuse distance.
                currentPos.y = baseY;

                Vector3 dirToPlayer = playerPos - currentPos;
                float distSqr = dirToPlayer.sqrMagnitude;

                HandleFlightMovement(playerPos, dirToPlayer);
                CheckFuseTrigger(distSqr);
            }
        }

        ApplyHover();
    }

    // ========================================================================
    // Hovering
    // ========================================================================

    private float GetBobOffset()
    {
        if (hoverWeight <= 0f) return 0f;

        float t = (Time.time + timeOffset) * bobFrequency;

        // Two slow sine waves blended together feel organic instead of a metronome.
        // Divided by 1.35 so the peak never exceeds floatAmplitude.
        float wave = (Mathf.Sin(t) + 0.35f * Mathf.Sin(t * 0.53f + 1.3f)) / 1.35f;
        return wave * floatAmplitude * hoverWeight;
    }

    private void ApplyHover()
    {
        Vector3 pos = cachedTransform.position;
        pos.y = baseY + GetBobOffset();
        cachedTransform.position = pos;
    }

    // Moves on the XZ plane only. The height is owned by baseY / ApplyHover.
    private void MoveHorizontal(Vector3 targetPos, float speed)
    {
        Vector3 pos = cachedTransform.position;
        Vector3 flatTarget = new Vector3(targetPos.x, pos.y, targetPos.z);
        cachedTransform.position = Vector3.MoveTowards(pos, flatTarget, speed * Time.deltaTime);
    }

    private void HandleFlightMovement(Vector3 targetPos, Vector3 dirToPlayer)
    {
        if (activeKnockbackRoutine != null) return;

        MoveHorizontal(targetPos, randomizedSpeed);

        float idealY = targetPos.y + hoverHeightOffset;
        baseY = Mathf.MoveTowards(baseY, idealY, randomizedSpeed * 0.75f * Time.deltaTime);

        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToPlayer);
            cachedTransform.rotation = Quaternion.Slerp(cachedTransform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    // ========================================================================
    // Kamikaze logic
    // ========================================================================

    private void CheckFuseTrigger(float distSqr)
    {
        if (distSqr <= fuseTriggerDistance * fuseTriggerDistance)
        {
            if (requiresTokenToFuse && GlobalTokenManager.Instance != null)
            {
                if (!GlobalTokenManager.Instance.RequestToken(cachedTransform, tokenType))
                {
                    return;
                }
                holdsToken = true;
            }

            currentState = BatState.Fusing;
            activeFuseRoutine = StartCoroutine(FuseAndExplodeRoutine());
        }
    }

    private IEnumerator FuseAndExplodeRoutine()
    {
        float elapsed = 0f;
        float flashTimer = 0f;
        bool isFlashingRed = false;

        if (audioSource != null && fuseTickSound != null)
        {
            audioSource.clip = fuseTickSound;
            audioSource.loop = true;
            audioSource.Play();
        }

        while (elapsed < fuseDuration)
        {
            if (currentState == BatState.Dead) yield break;

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            flashTimer += deltaTime;

            currentWingSpinSpeed = Mathf.Lerp(normalWingSpinSpeed, fuseWingSpinSpeed, elapsed / fuseDuration);
            float currentFlashInterval = Mathf.Lerp(0.2f, 0.05f, elapsed / fuseDuration);

            if (flashTimer >= currentFlashInterval)
            {
                flashTimer = 0f;
                isFlashingRed = !isFlashingRed;

                if (isFlashingRed) SetFlashColor(fuseFlashColor);
                else ResetColors();
            }

            if (playerTransform != null)
            {
                Vector3 dragPos = playerTransform.position;

                // Don't fight the knockback while it is pushing the bat around.
                if (activeKnockbackRoutine == null)
                {
                    MoveHorizontal(dragPos, randomizedSpeed * 0.3f);
                }

                baseY = Mathf.MoveTowards(baseY, dragPos.y + hoverHeightOffset, randomizedSpeed * deltaTime);
                ApplyHover();
            }

            yield return null;
        }

        Explode();
    }

    private void Explode()
    {
        if (currentState == BatState.Dead) return;
        currentState = BatState.Dead;

        if (audioSource != null) audioSource.Stop();

        if (explosionVFXPrefab != null)
        {
            ParticleSystem vfx = Instantiate(explosionVFXPrefab, cachedTransform.position, Quaternion.identity);
            vfx.Play();
            Destroy(vfx.gameObject, 2.5f);
        }

        int hitCount = Physics.OverlapSphereNonAlloc(cachedTransform.position, explosionRadius, explosionBuffer);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = explosionBuffer[i];
            if (hit != null && hit.CompareTag("Player") && hit.TryGetComponent<IDamageable>(out var damageable))
            {
                Vector3 hitDir = (hit.transform.position - cachedTransform.position).normalized;
                hitDir.y = 0f;

                int animHash = Animator.StringToHash(playerReactionAnimName);

                if (damageable is PlayerHealth ph)
                {
                    ph.TakeDamage(explosionDamage, hit.transform.position, hitDir, explosionKnockback, explosionSound, animHash, true, stunDuration);
                }
                else
                {
                    damageable.TakeDamage(explosionDamage, hit.transform.position, hitDir, explosionKnockback, explosionSound, animHash, true);
                }
            }
        }

        ReturnToPool();
    }

    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, float force, AudioClip hitSound, int attackID, bool isAOE)
    {
        if (currentState == BatState.Dead) return;

        currentHealth -= damage;

        if (activeKnockbackRoutine != null) StopCoroutine(activeKnockbackRoutine);
        activeKnockbackRoutine = StartCoroutine(ApplyKnockback(hitDirection, force));

        if (currentHealth <= 0)
        {
            DieByPlayer();
        }
    }

    private void DieByPlayer()
    {
        currentState = BatState.Dead;

        if (activeFuseRoutine != null) StopCoroutine(activeFuseRoutine);
        if (audioSource != null) audioSource.Stop();

        ReturnToPool();
    }

    // Knockback only pushes on the XZ plane. Height belongs to the hover system, so the
    // bat keeps bobbing smoothly while it is being knocked back (and never snaps in Y).
    private IEnumerator ApplyKnockback(Vector3 hitDirection, float knockbackForce)
    {
        Vector3 pushDir = hitDirection;
        pushDir.y = 0f;
        if (pushDir == Vector3.zero) pushDir = -cachedTransform.forward;
        pushDir.y = 0f;
        pushDir.Normalize();

        float elapsed = 0f;
        float duration = 0.15f;
        Vector3 startPos = cachedTransform.position;
        Vector3 offset = pushDir * knockbackForce;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            Vector3 pos = cachedTransform.position;
            pos.x = startPos.x + offset.x * t;
            pos.z = startPos.z + offset.z * t;
            cachedTransform.position = pos;

            yield return null;
        }
        activeKnockbackRoutine = null;
    }

    private void ReturnToPool()
    {
        ResetColors();

        if (EnemyObjectPool.Instance != null)
        {
            EnemyObjectPool.Instance.ReturnToPool(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    private void SetFlashColor(Color targetColor)
    {
        propBlock.SetColor(ColorPropID, targetColor);
        propBlock.SetColor(BaseColorPropID, targetColor);

        for (int i = 0; i < batRenderers.Length; i++)
        {
            if (batRenderers[i] != null)
            {
                batRenderers[i].SetPropertyBlock(propBlock);
            }
        }
    }

    private void ResetColors()
    {
        for (int i = 0; i < batRenderers.Length; i++)
        {
            if (batRenderers[i] != null)
            {
                batRenderers[i].SetPropertyBlock(null);
            }
        }
    }

    // ========================================================================
    // Wings: every wing spins around its OWN centre, like a helicopter rotor
    // ========================================================================

    private void SpinWings(float dt)
    {
        float step = currentWingSpinSpeed * dt;

        for (int i = 0; i < wingRotors.Count; i++)
        {
            WingRotor rotor = wingRotors[i];
            if (rotor.pivot == null) continue;

            rotor.angle = Mathf.Repeat(rotor.angle + step * rotor.direction, 360f);
            rotor.pivot.localRotation = rotor.restRotation * Quaternion.AngleAxis(rotor.angle, spinAxis);
        }
    }

    // Spinning happens on an empty "pivot" object placed at the visual centre of each wing.
    // Because the pivot sits in the middle of the wing, rotating it spins the wing in place
    // instead of swinging it around the bat's body.
    private void SetupWings()
    {
        wingRotors.Clear();
        spinAxis = wingSpinAxis.sqrMagnitude > 0.0001f ? wingSpinAxis.normalized : Vector3.up;

        // --- Mode 1: wings were assigned one by one ---------------------------------------
        if (individualWings != null && individualWings.Length > 0)
        {
            int index = 0;
            for (int i = 0; i < individualWings.Length; i++)
            {
                Transform wing = individualWings[i];
                if (wing == null) continue;

                Bounds bounds;
                if (!TryGetRendererBounds(wing, out bounds)) bounds = new Bounds(wing.position, Vector3.zero);

                List<WingPart> single = new List<WingPart>(1);
                single.Add(new WingPart { transform = wing, bounds = bounds });

                float dir = (counterRotateWings && index % 2 == 1) ? -1f : 1f;
                CreateRotor(single, dir, wing.parent != null ? wing.parent : cachedTransform);
                index++;
            }
            return;
        }

        // --- Mode 2: auto-detect from the wing holder -------------------------------------
        if (wingObject == null) return;

        Renderer[] found = wingObject.GetComponentsInChildren<Renderer>();
        List<Renderer> meshRenderers = new List<Renderer>();
        Bounds all = new Bounds();
        bool hasBounds = false;

        for (int i = 0; i < found.Length; i++)
        {
            Renderer r = found[i];
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            meshRenderers.Add(r);
            if (!hasBounds) { all = r.bounds; hasBounds = true; }
            else all.Encapsulate(r.bounds);
        }

        if (!hasBounds)
        {
            Debug.LogWarning("[KamikazeBatController] No mesh renderers found under the wing object, so the wings will not spin.", this);
            return;
        }

        int axis = (int)wingMirrorAxis;
        // The plane that separates the left wing from the right wing, in the bat's local space.
        float plane = cachedTransform.InverseTransformPoint(all.center)[axis];

        List<WingPart> parts = new List<WingPart>();
        for (int i = 0; i < meshRenderers.Count; i++)
        {
            Renderer r = meshRenderers[i];

            float lo, hi;
            GetLocalRange(r.bounds, axis, out lo, out hi);
            bool straddlesPlane = Mathf.Min(plane - lo, hi - plane) > 0.15f * (hi - lo);

            MeshRenderer meshRenderer = r as MeshRenderer;
            if (straddlesPlane && meshRenderer != null && TrySplitMesh(meshRenderer, axis, plane, parts))
            {
                continue; // this one mesh became a left half and a right half
            }

            parts.Add(new WingPart { transform = r.transform, bounds = r.bounds });
        }

        List<WingPart> negativeSide = new List<WingPart>();
        List<WingPart> positiveSide = new List<WingPart>();
        for (int i = 0; i < parts.Count; i++)
        {
            float c = cachedTransform.InverseTransformPoint(parts[i].bounds.center)[axis];
            if (c < plane) negativeSide.Add(parts[i]);
            else positiveSide.Add(parts[i]);
        }

        CreateRotor(negativeSide, 1f, wingObject);
        CreateRotor(positiveSide, counterRotateWings ? -1f : 1f, wingObject);

        if (wingRotors.Count < 2)
        {
            Debug.LogWarning("[KamikazeBatController] Only one wing could be isolated, so it spins around the middle of the bat. " +
                             "Either tick Read/Write Enabled on the wing mesh's import settings (so it can be split automatically), " +
                             "or assign the left and right wings separately in 'Individual Wings'.", this);
        }
    }

    private void CreateRotor(List<WingPart> group, float direction, Transform pivotParent)
    {
        if (group.Count == 0) return;

        Bounds bounds = group[0].bounds;
        for (int i = 1; i < group.Count; i++) bounds.Encapsulate(group[i].bounds);

        // If the wing IS the holder itself, the pivot can't be created inside of it.
        if (pivotParent == group[0].transform)
        {
            pivotParent = pivotParent.parent != null ? pivotParent.parent : cachedTransform;
        }

        GameObject pivotObject = new GameObject("WingPivot_" + wingRotors.Count);
        pivotObject.layer = group[0].transform.gameObject.layer;

        Transform pivot = pivotObject.transform;
        pivot.SetParent(pivotParent, false);
        pivot.position = bounds.center;
        pivot.rotation = cachedTransform.rotation; // so the spin axis is the bat's own axis

        for (int i = 0; i < group.Count; i++)
        {
            group[i].transform.SetParent(pivot, true); // keeps the wing exactly where it was
        }

        WingRotor rotor = new WingRotor();
        rotor.pivot = pivot;
        rotor.restRotation = pivot.localRotation;
        rotor.direction = direction;
        wingRotors.Add(rotor);
    }

    private bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bool has = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (!(renderers[i] is MeshRenderer) && !(renderers[i] is SkinnedMeshRenderer)) continue;

            if (!has) { bounds = renderers[i].bounds; has = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return has;
    }

    // Min / max of a world-space bounds box along one of the bat's local axes.
    private void GetLocalRange(Bounds b, int axis, out float lo, out float hi)
    {
        Vector3 axisVector = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
        Vector3 axisWorld = cachedTransform.TransformDirection(axisVector);
        Vector3 e = b.extents;
        float half = Mathf.Abs(axisWorld.x) * e.x + Mathf.Abs(axisWorld.y) * e.y + Mathf.Abs(axisWorld.z) * e.z;

        float a = cachedTransform.InverseTransformPoint(b.center - axisWorld * half)[axis];
        float d = cachedTransform.InverseTransformPoint(b.center + axisWorld * half)[axis];
        lo = Mathf.Min(a, d);
        hi = Mathf.Max(a, d);
    }

    // ------------------------------------------------------------------------
    // Runtime mesh splitting (only used when both wings share a single mesh)
    // ------------------------------------------------------------------------

    private bool TrySplitMesh(MeshRenderer source, int axis, float plane, List<WingPart> outParts)
    {
        MeshFilter filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return false;

        Mesh sourceMesh = filter.sharedMesh;
        if (!sourceMesh.isReadable)
        {
            Debug.LogWarning("[KamikazeBatController] Wing mesh '" + sourceMesh.name + "' is not readable, so it can't be split " +
                             "into two wings. Select the model in the Project window and tick 'Read/Write' in its import settings.", this);
            return false;
        }

        var key = (sourceMesh.GetInstanceID(), axis, Mathf.RoundToInt(plane * 1000f));
        SplitMeshPair pair;
        if (!splitMeshCache.TryGetValue(key, out pair) || pair == null || pair.negative == null || pair.positive == null)
        {
            pair = BuildSplitMeshes(sourceMesh, filter.transform, axis, plane);
            if (pair == null) return false;
            splitMeshCache[key] = pair;
        }

        outParts.Add(CreateHalf(source, pair.negative, "_WingA"));
        outParts.Add(CreateHalf(source, pair.positive, "_WingB"));

        source.enabled = false; // the combined mesh is replaced by its two halves
        return true;
    }

    private SplitMeshPair BuildSplitMeshes(Mesh source, Transform meshTransform, int axis, float plane)
    {
        Matrix4x4 toBatSpace = cachedTransform.worldToLocalMatrix * meshTransform.localToWorldMatrix;

        Vector3[] vertices = source.vertices;
        int subMeshCount = source.subMeshCount;

        List<int>[] negativeTris = new List<int>[subMeshCount];
        List<int>[] positiveTris = new List<int>[subMeshCount];

        for (int s = 0; s < subMeshCount; s++)
        {
            negativeTris[s] = new List<int>();
            positiveTris[s] = new List<int>();

            int[] tris = source.GetTriangles(s);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 centroid = (vertices[tris[i]] + vertices[tris[i + 1]] + vertices[tris[i + 2]]) / 3f;
                List<int> target = toBatSpace.MultiplyPoint3x4(centroid)[axis] < plane ? negativeTris[s] : positiveTris[s];

                target.Add(tris[i]);
                target.Add(tris[i + 1]);
                target.Add(tris[i + 2]);
            }
        }

        Mesh negative = BuildHalfMesh(source, vertices, negativeTris, source.name + "_WingA");
        Mesh positive = BuildHalfMesh(source, vertices, positiveTris, source.name + "_WingB");

        if (negative == null || positive == null)
        {
            if (negative != null) Destroy(negative);
            if (positive != null) Destroy(positive);
            return null;
        }

        return new SplitMeshPair { negative = negative, positive = positive };
    }

    private static Mesh BuildHalfMesh(Mesh source, Vector3[] sourceVertices, List<int>[] subMeshTris, string meshName)
    {
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Vector2[] uvs = source.uv;
        Color[] colors = source.colors;

        bool hasNormals = normals != null && normals.Length == sourceVertices.Length;
        bool hasTangents = tangents != null && tangents.Length == sourceVertices.Length;
        bool hasUVs = uvs != null && uvs.Length == sourceVertices.Length;
        bool hasColors = colors != null && colors.Length == sourceVertices.Length;

        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector4> newTangents = new List<Vector4>();
        List<Vector2> newUVs = new List<Vector2>();
        List<Color> newColors = new List<Color>();
        List<int>[] newTris = new List<int>[subMeshTris.Length];

        for (int s = 0; s < subMeshTris.Length; s++)
        {
            newTris[s] = new List<int>(subMeshTris[s].Count);

            for (int i = 0; i < subMeshTris[s].Count; i++)
            {
                int oldIndex = subMeshTris[s][i];
                int newIndex;

                if (!remap.TryGetValue(oldIndex, out newIndex))
                {
                    newIndex = newVertices.Count;
                    remap[oldIndex] = newIndex;

                    newVertices.Add(sourceVertices[oldIndex]);
                    if (hasNormals) newNormals.Add(normals[oldIndex]);
                    if (hasTangents) newTangents.Add(tangents[oldIndex]);
                    if (hasUVs) newUVs.Add(uvs[oldIndex]);
                    if (hasColors) newColors.Add(colors[oldIndex]);
                }

                newTris[s].Add(newIndex);
            }
        }

        if (newVertices.Count == 0) return null;

        Mesh mesh = new Mesh();
        mesh.name = meshName;
        mesh.indexFormat = source.indexFormat;
        mesh.SetVertices(newVertices);
        if (hasNormals) mesh.SetNormals(newNormals);
        if (hasTangents) mesh.SetTangents(newTangents);
        if (hasUVs) mesh.SetUVs(0, newUVs);
        if (hasColors) mesh.SetColors(newColors);

        mesh.subMeshCount = subMeshTris.Length;
        for (int s = 0; s < newTris.Length; s++) mesh.SetTriangles(newTris[s], s);

        if (!hasNormals) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static WingPart CreateHalf(MeshRenderer source, Mesh mesh, string suffix)
    {
        GameObject half = new GameObject(source.gameObject.name + suffix);
        half.layer = source.gameObject.layer;
        half.transform.SetParent(source.transform, false);

        half.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = half.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;

        return new WingPart
        {
            transform = half.transform,
            bounds = TransformBounds(half.transform, mesh.bounds)
        };
    }

    private static Bounds TransformBounds(Transform t, Bounds local)
    {
        Vector3 c = local.center;
        Vector3 e = local.extents;
        Bounds result = new Bounds();

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = c + new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);

            Vector3 world = t.TransformPoint(corner);
            if (i == 0) result = new Bounds(world, Vector3.zero);
            else result.Encapsulate(world);
        }
        return result;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, fuseTriggerDistance);

        Gizmos.color = new Color(1, 0, 0, 0.3f);
        Gizmos.DrawSphere(transform.position, explosionRadius);
    }
}