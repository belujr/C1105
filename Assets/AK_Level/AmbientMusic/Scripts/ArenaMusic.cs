using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on an empty GameObject with a Collider set to "Is Trigger" (the arena entrance).
///
/// When the player walks into the trigger:
///   - the AmbientMusic extra layer (default name "Arena") fades IN and the normal music fades OUT
///     (the Environment layer keeps playing),
///   - Combat music is silenced for the whole fight, so arena enemies never start it.
///
/// When every arena enemy is dead (after a short End Delay):
///   - the Arena layer fades slowly OUT and the normal music fades back IN,
///   - Combat music is allowed again after Combat Release Delay.
///
/// The AmbientMusic layer needs: Start Active OFF, Replaces Music ON, Priority 2 (higher than Combat).
/// </summary>
public class ArenaMusic : MonoBehaviour
{
    [Header("Music")]
    [Tooltip("Name of the Extra Layer in AmbientMusic. Must match exactly.")]
    [SerializeField] private string layerName = "Arena";

    [Header("Start / Restart")]
    [Tooltip("ON: walking into a trigger Collider on this object starts the arena. Turn OFF when a beacon or spawner drives it instead.")]
    [SerializeField] private bool startOnPlayerTrigger = true;
    [Tooltip("ON: the first enemy passed to RegisterEnemy() starts the music by itself (for ships and spawners).")]
    [SerializeField] private bool startWhenEnemiesRegister = false;
    [Tooltip("ON: after the music has ended it can start again later (next ship, next wave). OFF: it plays once.")]
    [SerializeField] private bool canStartAgain = false;

    [Header("Arena Enemies")]
    [Tooltip("Optional. Every direct CHILD of this object counts as an arena enemy.")]
    [SerializeField] private Transform enemiesRoot;
    [Tooltip("Optional. Extra enemies (anywhere in the scene) that belong to this arena.")]
    [SerializeField] private List<GameObject> enemies = new List<GameObject>();
    [Tooltip("Seconds with zero enemies left before the music ends. Also covers waves: if a spawner adds new enemies in this time, the arena keeps going.")]
    [SerializeField] private float endDelay = 2f;
    [Tooltip("How often to count the remaining enemies. 0.25 is plenty.")]
    [SerializeField] private float checkInterval = 0.25f;

    [Header("Combat Music")]
    [Tooltip("ON: the CombatMusic script is silenced while the arena is active, so arena enemies don't trigger it.")]
    [SerializeField] private bool silenceCombatMusic = true;
    [Tooltip("Seconds after the arena ends before CombatMusic may react again (lets dead enemies leave the detect radius).")]
    [SerializeField] private float combatReleaseDelay = 6f;

    [Header("Events")]
    public UnityEvent OnArenaStarted;
    public UnityEvent OnArenaCleared;

    [Header("Debug")]
    [Tooltip("Logs to the Console when the arena starts/ends. Turn off when everything works.")]
    [SerializeField] private bool debugLog = false;

    private readonly List<GameObject> tracked = new List<GameObject>();
    private readonly HashSet<GameObject> seenActive = new HashSet<GameObject>();

    private AmbientMusic activeMusic;
    private bool isActive;
    private bool finished;
    private bool hadEnemies;
    private int alive;
    private float checkTimer;
    private float clearTimer;

    private bool combatLocked;
    private float releaseTimer;

    public bool IsActive => isActive;
    public bool IsFinished => finished;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning("ArenaMusic: the Collider is not a trigger. Turning \"Is Trigger\" on.", this);
            col.isTrigger = true;
        }
    }

    // ------------------------------------------------------------------
    // Trigger
    // ------------------------------------------------------------------

    private bool CanStart => !isActive && (!finished || canStartAgain);

    private void OnTriggerEnter(Collider other)
    {
        if (!startOnPlayerTrigger || !CanStart) return;

        if (other.CompareTag("Player") || other.GetComponentInParent<PlayerController>() != null)
            StartArena();
    }

    // ------------------------------------------------------------------
    // Public API (call these from your own scripts or UnityEvents)
    // ------------------------------------------------------------------

    /// <summary>Start the arena music. Called automatically by the trigger.</summary>
    public void StartArena()
    {
        if (!CanStart) return;

        // Collect the enemies that belong to this arena.
        tracked.Clear();
        seenActive.Clear();

        if (enemiesRoot != null)
        {
            foreach (Transform child in enemiesRoot)
                AddTracked(child.gameObject);
        }
        for (int i = 0; i < enemies.Count; i++)
            AddTracked(enemies[i]);

        isActive = true;
        clearTimer = 0f;
        checkTimer = 0f;
        releaseTimer = 0f;

        alive = CountAlive();
        hadEnemies = alive > 0;

        activeMusic = AmbientMusic.Current;
        if (activeMusic == null)
        {
            Debug.LogWarning("ArenaMusic: there is no AmbientMusic object in this scene.", this);
        }
        else if (!activeMusic.HasLayer(layerName))
        {
            Debug.LogWarning("ArenaMusic: AmbientMusic has no extra layer named \"" + layerName +
                             "\" (check spelling and that the layer has a clip).", this);
        }
        else
        {
            activeMusic.SetLayerActive(layerName, true);
        }

        if (silenceCombatMusic && !combatLocked)
        {
            CombatMusic.Suppress(true);
            combatLocked = true;
        }

        if (debugLog) Debug.Log("ArenaMusic: arena STARTED with " + alive + " enemies", this);
        OnArenaStarted?.Invoke();
    }

    /// <summary>
    /// Tell the arena an enemy died. Use this if your enemies stay active after dying
    /// (corpses, death animations) so they can't be told apart from living ones.
    /// </summary>
    public void OnEnemyKilled(GameObject enemy)
    {
        if (enemy == null) return;
        tracked.Remove(enemy);
        seenActive.Remove(enemy);
        if (isActive) alive = CountAlive();
    }

    /// <summary>Add an enemy to the arena while it is running (for spawners and waves).</summary>
    public void RegisterEnemy(GameObject enemy)
    {
        if (enemy == null) return;

        // Idle: the first registered enemy can start the music (if allowed).
        if (!isActive)
        {
            if (!startWhenEnemiesRegister || !CanStart) return;
            StartArena();
        }

        if (tracked.Contains(enemy)) return;
        tracked.Add(enemy);
        hadEnemies = true;
        clearTimer = 0f;
        alive = CountAlive();
    }

    /// <summary>End the arena music right now, whatever the enemies are doing.</summary>
    public void ForceEnd()
    {
        if (isActive) EndArena();
    }

    /// <summary>Allow the trigger to start the arena again (the enemies must be back in place first).</summary>
    public void ResetArena()
    {
        if (isActive) return;
        finished = false;
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------

    private void Update()
    {
        float dt = Time.deltaTime;

        if (isActive)
        {
            checkTimer -= dt;
            if (checkTimer <= 0f)
            {
                checkTimer = Mathf.Max(0.05f, checkInterval);
                alive = CountAlive();
                if (alive > 0) hadEnemies = true;
            }

            // Only end once enemies existed and are now all gone (never end an empty arena by accident).
            if (hadEnemies && alive == 0)
            {
                clearTimer += dt;
                if (clearTimer >= endDelay) EndArena();
            }
            else
            {
                clearTimer = 0f;
            }
        }

        // Give Combat music back a little after the arena has ended.
        if (!isActive && combatLocked)
        {
            releaseTimer -= dt;
            if (releaseTimer <= 0f) ReleaseCombat();
        }
    }

    private void EndArena()
    {
        isActive = false;
        finished = true;

        var music = activeMusic != null ? activeMusic : AmbientMusic.Current;
        if (music != null && music.HasLayer(layerName))
            music.SetLayerActive(layerName, false);   // fades out, main music fades back in
        activeMusic = null;

        releaseTimer = combatReleaseDelay;
        if (debugLog) Debug.Log("ArenaMusic: arena CLEARED", this);
        OnArenaCleared?.Invoke();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void AddTracked(GameObject go)
    {
        if (go != null && go != gameObject && !tracked.Contains(go))
            tracked.Add(go);
    }

    // Counts enemies still alive. An enemy is gone when it was destroyed, or when it was
    // active at some point and has since been switched off. An enemy that has never been
    // active yet (waiting to be spawned in) still counts as alive.
    private int CountAlive()
    {
        int count = 0;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var go = tracked[i];

            if (go == null)
            {
                tracked.RemoveAt(i);
                continue;
            }

            if (go.activeInHierarchy)
            {
                seenActive.Add(go);
                count++;
            }
            else if (seenActive.Contains(go))
            {
                tracked.RemoveAt(i);
                seenActive.Remove(go);
            }
            else
            {
                count++;
            }
        }
        return count;
    }

    private void ReleaseCombat()
    {
        if (!combatLocked) return;
        CombatMusic.Suppress(false);
        combatLocked = false;
    }

    private void OnDisable()
    {
        // Never leave Combat music locked if this object goes away mid-fight.
        if (isActive)
        {
            var music = activeMusic != null ? activeMusic : AmbientMusic.Current;
            if (music != null && music.HasLayer(layerName))
                music.SetLayerActive(layerName, false);

            isActive = false;
            activeMusic = null;
        }
        ReleaseCombat();
    }
}