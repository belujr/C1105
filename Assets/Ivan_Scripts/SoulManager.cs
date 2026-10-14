using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Persistent soul-currency service. Owns the data and broadcasts changes.
/// It knows nothing about UI: any number of listeners (see SoulCounterUI) can subscribe
/// to <see cref="OnSoulsChanged"/> and stay in sync across scenes.
/// </summary>
[DefaultExecutionOrder(-100)] // Awake before UI/gameplay scripts so Instance is ready for their OnEnable
[DisallowMultipleComponent]
public sealed class SoulManager : MonoBehaviour
{
    public static SoulManager Instance { get; private set; }

    /// <summary>Fired with the new total whenever the balance changes (and once after load).</summary>
    public static event Action<int> OnSoulsChanged;

    [Header("Debug & Testing")]
    [Tooltip("Wipes saved souls when you hit Play. Only has an effect inside the Unity Editor.")]
    public bool resetSaveDataOnStart = false;

    [Header("Persistence")]
    [Tooltip("Disk writes are batched: many changes inside this window cost a single PlayerPrefs.Save().")]
    [SerializeField, Min(0f)] private float saveDelaySeconds = 1.5f;

    [Header("Currency (runtime)")]
    [SerializeField] private int totalSouls = 0;
    public int TotalSouls => totalSouls;

    private const string SAVE_SOULS_KEY = "Player_TotalSouls";

    private WaitForSecondsRealtime saveWait;
    private Coroutine saveRoutine;
    private bool isDirty;

    // Keeps static state clean when "Enter Play Mode > Reload Domain" is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        OnSoulsChanged = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // DontDestroyOnLoad silently does nothing for objects nested under another object.
        // The manager would then be destroyed when scene_hub unloads and scene_level would
        // have no SoulManager at all (counter stuck at 0, kills never add souls).
        if (transform.parent != null) transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        saveWait = new WaitForSecondsRealtime(saveDelaySeconds);

        if (resetSaveDataOnStart && Application.isEditor)
            ResetSoulData();
        else
            totalSouls = PlayerPrefs.GetInt(SAVE_SOULS_KEY, 0);

        // Any UI that enabled before us gets the loaded value; later UI reads TotalSouls itself.
        OnSoulsChanged?.Invoke(totalSouls);
    }

    // ---------------------------------------------------------------- Public API

    public bool CanAfford(int amount) => amount >= 0 && totalSouls >= amount;

    public void AddSouls(int amount)
    {
        if (amount <= 0) return;
        totalSouls = (int)Math.Min((long)totalSouls + amount, int.MaxValue); // overflow-safe
        Commit();
    }

    public bool SpendSouls(int amount)
    {
        if (amount <= 0 || totalSouls < amount) return false;
        totalSouls -= amount;
        Commit();
        return true;
    }

    public void ResetSoulData()
    {
        totalSouls = 0;
        isDirty = false;
        PlayerPrefs.DeleteKey(SAVE_SOULS_KEY);
        PlayerPrefs.Save();
        OnSoulsChanged?.Invoke(totalSouls);
    }

    // ---------------------------------------------------------------- Persistence

    private void Commit()
    {
        MarkDirty();
        OnSoulsChanged?.Invoke(totalSouls);
    }

    private void MarkDirty()
    {
        isDirty = true;
        if (saveRoutine == null)
            saveRoutine = StartCoroutine(SaveAfterDelay());
    }

    private IEnumerator SaveAfterDelay()
    {
        yield return saveWait; // one shared yield object, no per-save allocation
        saveRoutine = null;
        Flush();
    }

    /// <summary>Writes pending changes to disk right now (no-op if nothing changed).</summary>
    public void Flush()
    {
        if (!isDirty) return;
        isDirty = false;
        PlayerPrefs.SetInt(SAVE_SOULS_KEY, totalSouls);
        PlayerPrefs.Save();
    }

    // Make sure a pending batch is never lost.
    private void OnApplicationPause(bool paused) { if (paused) Flush(); }
    private void OnApplicationQuit() => Flush();

    private void OnDisable()
    {
        Flush();
        saveRoutine = null; // coroutines stop when the object is disabled
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}