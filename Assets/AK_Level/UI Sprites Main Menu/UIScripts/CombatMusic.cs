using UnityEngine;

/// <summary>
/// Put this on an empty GameObject in the level scene (next to the AmbientMusic object).
///
/// Fades an AmbientMusic extra layer (default name "Combat") IN while enemies are near the
/// player, and back OUT a few seconds after the last one is gone.
///
/// How it finds enemies: anything on the "Enemy Layers" within Detect Radius of the player.
/// You can also drive it by hand from any script or UnityEvent: SetCombat(true / false).
/// The AmbientMusic layer needs Start Active turned OFF.
/// </summary>
public class CombatMusic : MonoBehaviour
{
    [Header("Music")]
    [Tooltip("Name of the Extra Layer in AmbientMusic. Must match exactly.")]
    [SerializeField] private string layerName = "Combat";

    [Header("Detection")]
    [Tooltip("ON: enemies near the player start the combat music automatically.")]
    [SerializeField] private bool autoDetect = true;
    [Tooltip("The layer(s) your enemies are on. If empty, layers named 'Enemy' or 'Enemies' are used.")]
    [SerializeField] private LayerMask enemyLayers;
    [Tooltip("Enemies closer than this to the player count as combat.")]
    [SerializeField] private float detectRadius = 15f;
    [Tooltip("Seconds to keep the combat music after the last enemy is gone.")]
    [SerializeField] private float exitDelay = 4f;
    [Tooltip("How often to look for enemies. 0.25 is plenty.")]
    [SerializeField] private float checkInterval = 0.25f;

    [Header("Who is the player?")]
    [Tooltip("Leave empty to use the object tagged 'Player'.")]
    [SerializeField] private Transform player;

    [Header("Debug")]
    [Tooltip("Logs to the Console when combat starts/ends. Turn off when everything works.")]
    [SerializeField] private bool debugLog = false;

    private static readonly Collider[] hits = new Collider[32];

    private bool manualCombat;      // set by SetCombat()
    private bool threatNow;         // result of the last enemy check
    private bool inCombat;          // what we want the music to do
    private float calmTimer;
    private float checkTimer;
    private float findTimer;

    private AmbientMusic appliedMusic;
    private bool appliedState;
    private bool warnedNoMusic;
    private bool warnedNoLayer;
    private bool warnedNoPlayer;

    // While above 0, combat music is held off (used by ArenaMusic). It is a counter so
    // two overlapping arenas can't release each other's lock.
    private static int suppressCount;
    public static bool IsSuppressed => suppressCount > 0;

    /// <summary>Hold combat music off (true) or release it (false). Calls must be paired.</summary>
    public static void Suppress(bool on)
    {
        suppressCount = Mathf.Max(0, suppressCount + (on ? 1 : -1));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        suppressCount = 0;
    }

    /// <summary>Force combat music on/off from your own scripts (stays until you call it again).</summary>
    public void SetCombat(bool on)
    {
        manualCombat = on;
    }

    private void Start()
    {
        if (enemyLayers.value == 0)
            enemyLayers = LayerMask.GetMask("Enemy", "Enemies");

        if (autoDetect && enemyLayers.value == 0)
            Debug.LogWarning("CombatMusic: no Enemy Layers set. Put your enemies on a layer (for example \"Enemy\") " +
                             "and select it in Enemy Layers, or turn Auto Detect off and call SetCombat().", this);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // 0. Held off by an arena: no combat music at all, and no waiting for Exit Delay.
        if (IsSuppressed)
        {
            threatNow = false;
            checkTimer = 0f;   // look again straight away once released
            if (inCombat)
            {
                inCombat = false;
                if (debugLog) Debug.Log("CombatMusic: combat SUPPRESSED (arena)", this);
            }
            ApplyToMusic();
            return;
        }

        // 1. Look for enemies a few times per second (not every frame).
        checkTimer -= dt;
        if (checkTimer <= 0f)
        {
            checkTimer = Mathf.Max(0.05f, checkInterval);
            threatNow = manualCombat || (autoDetect && EnemiesNearPlayer());
        }

        // 2. Combat starts at once, and ends only after Exit Delay of calm.
        if (threatNow)
        {
            calmTimer = exitDelay;
            if (!inCombat)
            {
                inCombat = true;
                if (debugLog) Debug.Log("CombatMusic: combat STARTED", this);
            }
        }
        else if (inCombat)
        {
            calmTimer -= dt;
            if (calmTimer <= 0f)
            {
                inCombat = false;
                if (debugLog) Debug.Log("CombatMusic: combat ENDED", this);
            }
        }

        ApplyToMusic();
    }

    private bool EnemiesNearPlayer()
    {
        if (player == null)
        {
            findTimer -= checkInterval;
            if (findTimer <= 0f)
            {
                var found = GameObject.FindGameObjectWithTag("Player");
                if (found != null) player = found.transform;
                findTimer = 0.5f;
            }

            if (player == null)
            {
                if (!warnedNoPlayer)
                {
                    warnedNoPlayer = true;
                    Debug.LogWarning("CombatMusic: can't find the player. Tag it \"Player\" or assign the Player field.", this);
                }
                return false;
            }
        }

        if (enemyLayers.value == 0) return false;

        int count = Physics.OverlapSphereNonAlloc(player.position, detectRadius, hits, enemyLayers, QueryTriggerInteraction.Collide);
        return count > 0;
    }

    // Tells AmbientMusic about changes only (and again if the AmbientMusic object was replaced).
    private void ApplyToMusic()
    {
        var music = AmbientMusic.Current;

        if (music == null)
        {
            if (inCombat && !warnedNoMusic)
            {
                warnedNoMusic = true;
                Debug.LogWarning("CombatMusic: there is no AmbientMusic object in this scene.", this);
            }
            return;
        }

        if (music == appliedMusic && appliedState == inCombat) return;

        if (!warnedNoLayer && !music.HasLayer(layerName))
        {
            warnedNoLayer = true;
            Debug.LogWarning("CombatMusic: AmbientMusic has no extra layer named \"" + layerName +
                             "\" (check spelling and that the layer has a clip).", this);
        }

        music.SetLayerActive(layerName, inCombat);
        appliedMusic = music;
        appliedState = inCombat;
    }

    private void OnDisable()
    {
        if (appliedState && appliedMusic != null)
            appliedMusic.SetLayerActive(layerName, false);

        appliedMusic = null;
        appliedState = false;
        inCombat = false;
        threatNow = false;
    }

    // Select this object to see the detection range around the player (Play mode).
    private void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(player.position, detectRadius);
    }
}