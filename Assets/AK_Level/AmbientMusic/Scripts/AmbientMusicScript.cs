using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Put this on an empty GameObject in each scene (forest, hub, loading screen, ...).
/// Plays looping layers: Music, Environment on top of it (wind, forest night, ...),
/// plus any number of Extra Layers (combat, build-up, portal glow, ...).
///
/// MUSIC REPLACEMENT: an Extra Layer with "Replaces Music" ticked takes over from the main
/// Music layer while it is active. The main Music fades out as the layer fades in (smooth
/// equal-power crossfade), and comes back when the layer ends. The Environment layer is never
/// touched, so the replacing layer plays over the Environment only.
/// If several replacing layers are active, the one with the highest Priority plays
/// (suggested: Combat = 1, Buildup = 2).
///
/// All layers fade in when the scene starts. When the scene is left (for example when the
/// loading screen takes over) this object survives the scene change just long enough to
/// fade every layer out, then removes itself. You will see it move under
/// "DontDestroyOnLoad" in the Hierarchy while playing; that is expected.
///
/// The layers link themselves to the Audio Mixer of the Pause Menu Manager, so the pause
/// menu sliders control them. The Output fields are optional.
///
/// Extra layers can be switched from gameplay code:
///     if (AmbientMusic.Current != null) AmbientMusic.Current.SetLayerActive("Buildup", true);
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AmbientMusic : MonoBehaviour
{
    /// <summary>The AmbientMusic of the scene we are in right now (null if none).</summary>
    public static AmbientMusic Current { get; private set; }

    /// <summary>One extra looping layer on top of Music + Environment.</summary>
    [System.Serializable]
    public class ExtraLayer
    {
        [Tooltip("Name used from code and by triggers: SetLayerActive(\"Combat\", true)")]
        public string name = "Layer";
        public AudioClip clip;
        [Tooltip("Level of this layer. Can be changed live in Play mode.")]
        [Range(0f, 1f)] public float volume = 0.25f;
        [Tooltip("Tip: give layers that must stay in sync the SAME start delay as the main music.")]
        public float startDelay = 0.5f;
        public float fadeInDuration = 4f;
        [Tooltip("On: the layer is active from the start of the scene. Off: it stays silent until it is switched on (SetLayerActive / a trigger).")]
        public bool startActive = true;
        [Tooltip("Seconds it takes to fade in and out when switched on/off. For a Replaces Music layer this is also the crossfade time.")]
        public float switchDuration = 2f;

        [Tooltip("ON: while this layer is active, the main Music fades out and this plays over the Environment only (Combat, Buildup...).")]
        public bool replacesMusic = false;
        [Tooltip("Only used with Replaces Music. If several are active, the highest number plays. Start at 1 (Combat = 1, Buildup = 2).")]
        public int priority = 1;
        [Tooltip("ON: the layer starts from its beginning every time it is switched on (use for build-ups). OFF: it joins mid-loop, which keeps it in sync with the music.")]
        public bool restartOnActivate = false;

        [Tooltip("Mixer group to link to automatically (when Output is empty). Use the exact group name from your mixer.")]
        public string mixerGroupName = "Music";
        [Tooltip("Optional. Overrides the group name above.")]
        public AudioMixerGroup output;

        // runtime state (not saved in the scene)
        [System.NonSerialized] public AudioSource source;
        [System.NonSerialized] public bool started;
        [System.NonSerialized] public float gate;         // 0..1 current on/off level (normal layers)
        [System.NonSerialized] public float gateTarget;   // where the gate is heading; > 0 means "active"
        [System.NonSerialized] public float bedGain;      // 0..1 crossfade level (Replaces Music layers)
        [System.NonSerialized] public bool wasActive;
        [System.NonSerialized] public float pendingLevel; // highest level requested this frame
        [System.NonSerialized] public bool hasPending;
    }

    [Header("Music Layer")]
    [SerializeField] private AudioClip clip;
    [Tooltip("Music level. Keep it low so it sits under the environment: try 0.15 - 0.3. Can be changed live in Play mode.")]
    [Range(0f, 1f)] [SerializeField] private float baseVolume = 0.25f;
    [SerializeField] private float startDelay = 0.5f;
    [SerializeField] private float fadeInDuration = 4f;
    [Tooltip("Seconds the main Music takes to fade out when a Replaces Music layer takes over, and to fade back in afterwards.")]
    [SerializeField] private float musicCrossfadeDuration = 1.5f;
    [Tooltip("Optional. Leave empty to link automatically to the 'Music' group of the Pause Menu Manager's mixer.")]
    [SerializeField] private AudioMixerGroup musicOutput;

    [Header("Environment Layer (plays over the music)")]
    [Tooltip("Wind, forest night, hub atmosphere... Leave empty for no environment layer in this scene.")]
    [SerializeField] private AudioClip environmentClip;
    [Tooltip("Environment level. Can be changed live in Play mode.")]
    [Range(0f, 1f)] [SerializeField] private float environmentVolume = 0.5f;
    [SerializeField] private float environmentStartDelay = 0f;
    [SerializeField] private float environmentFadeInDuration = 4f;
    [Tooltip("Optional. Leave empty to link automatically to the 'Ambience' group of the Pause Menu Manager's mixer.")]
    [SerializeField] private AudioMixerGroup environmentOutput;

    [Header("Extra Layers")]
    [Tooltip("Any number of additional looping layers (combat, build-up...). Press + to add one.")]
    [SerializeField] private ExtraLayer[] extraLayers;

    [Header("Scene Change")]
    [Tooltip("When the scene is left, all layers fade out over this many seconds, even while the loading screen is showing.")]
    [SerializeField] private float fadeOutDuration = 2f;

    private AudioSource musicSource;
    private AudioSource environmentSource;
    private bool musicStarted;
    private bool environmentStarted;
    private float mainBedGain = 1f;   // 1 = main Music audible, 0 = replaced by another layer

    private float sceneTime;          // seconds since this scene started (drives delays and fade-ins)
    private Scene originScene;
    private bool leaving;
    private float outProgress = 1f;   // 1 -> 0 while fading out

    private void Awake()
    {
        originScene = gameObject.scene;
        Current = this;

        // DontDestroyOnLoad only works on root objects.
        if (transform.parent != null) transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneUnloaded += OnSceneUnloaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        musicSource = GetComponent<AudioSource>();
        SetupSource(musicSource, clip, musicOutput);

        if (environmentClip != null)
        {
            var go = new GameObject("EnvironmentLayer");
            go.transform.SetParent(transform, false);
            environmentSource = go.AddComponent<AudioSource>();
            SetupSource(environmentSource, environmentClip, environmentOutput);
        }

        if (extraLayers != null)
        {
            for (int i = 0; i < extraLayers.Length; i++)
            {
                var layer = extraLayers[i];
                if (layer == null || layer.clip == null) continue;

                var go = new GameObject("Layer_" + layer.name);
                go.transform.SetParent(transform, false);
                layer.source = go.AddComponent<AudioSource>();
                SetupSource(layer.source, layer.clip, layer.output);

                layer.started = false;
                layer.gate = layer.startActive ? 1f : 0f;
                layer.gateTarget = layer.gate;
                layer.bedGain = layer.startActive ? 1f : 0f;
                layer.wasActive = layer.startActive;
                layer.pendingLevel = 0f;
                layer.hasPending = false;
            }
        }

        // If a replacing layer starts active, the main Music starts out replaced.
        mainBedGain = GetWinnerPriority() == 0 ? 1f : 0f;
    }

    private void Start()
    {
        // By now every Awake has run, so the Pause Menu Manager is ready.
        // Link to its mixer unless an Output was set by hand.
        if (musicSource.outputAudioMixerGroup == null)
            musicSource.outputAudioMixerGroup = PauseMenuManager.FindMixerGroup(PauseMenuManager.MusicGroupName);

        if (environmentSource != null && environmentSource.outputAudioMixerGroup == null)
            environmentSource.outputAudioMixerGroup = PauseMenuManager.FindMixerGroup(PauseMenuManager.AmbienceGroupName);

        if (clip != null && musicSource.outputAudioMixerGroup == null)
            Debug.LogWarning("AmbientMusic: couldn't link the music to the pause menu's mixer. Check that a Pause Menu Manager with an Audio Mixer exists and has a group named \"" +
                             PauseMenuManager.MusicGroupName + "\".", this);

        if (environmentSource != null && environmentSource.outputAudioMixerGroup == null)
            Debug.LogWarning("AmbientMusic: couldn't link the environment to the pause menu's mixer. Check that a Pause Menu Manager with an Audio Mixer exists and has a group named \"" +
                             PauseMenuManager.AmbienceGroupName + "\".", this);

        if (extraLayers != null)
        {
            for (int i = 0; i < extraLayers.Length; i++)
            {
                var layer = extraLayers[i];
                if (layer == null || layer.source == null) continue;

                if (layer.source.outputAudioMixerGroup == null)
                    layer.source.outputAudioMixerGroup = PauseMenuManager.FindMixerGroup(layer.mixerGroupName);

                if (layer.source.outputAudioMixerGroup == null)
                    Debug.LogWarning("AmbientMusic: couldn't link extra layer \"" + layer.name +
                                     "\" to the pause menu's mixer. Check that a group named \"" + layer.mixerGroupName + "\" exists.", this);
            }
        }
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private static void SetupSource(AudioSource source, AudioClip audioClip, AudioMixerGroup group)
    {
        source.clip = audioClip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D: same volume everywhere in the scene
        source.volume = 0f;
        if (group != null) source.outputAudioMixerGroup = group;
    }

    // ------------------------------------------------------------------
    // Public API for gameplay code
    // ------------------------------------------------------------------

    /// <summary>Fade an extra layer in (true) or out (false). Uses the layer's Switch Duration.</summary>
    public void SetLayerActive(string layerName, bool active)
    {
        var layer = FindLayer(layerName);
        if (layer == null)
        {
            Debug.LogWarning("AmbientMusic: no extra layer named \"" + layerName + "\".", this);
            return;
        }
        layer.gateTarget = active ? 1f : 0f;
    }

    /// <summary>Change the level of an extra layer from code.</summary>
    public void SetLayerVolume(string layerName, float volume)
    {
        var layer = FindLayer(layerName);
        if (layer != null) layer.volume = Mathf.Clamp01(volume);
    }

    /// <summary>True if an extra layer with this name exists.</summary>
    public bool HasLayer(string layerName)
    {
        return FindLayer(layerName) != null;
    }

    /// <summary>
    /// Drive an extra layer with a continuous 0..1 level (for example, proximity to a portal).
    /// If several callers ask for the same layer in one frame, the highest level wins, so two
    /// portals can share one layer without fighting. Call it every frame while the level is
    /// above 0, and once more with 0 when it ends. The layer's own Volume is the maximum.
    /// (Layers with Replaces Music are on/off: any level above 0 counts as on.)
    /// </summary>
    public void RequestLayerLevel(string layerName, float level)
    {
        var layer = FindLayer(layerName);
        if (layer == null) return;

        layer.pendingLevel = Mathf.Max(layer.pendingLevel, Mathf.Clamp01(level));
        layer.hasPending = true;
    }

    private void LateUpdate()
    {
        // Apply the highest level requested this frame (see RequestLayerLevel).
        if (extraLayers == null) return;

        for (int i = 0; i < extraLayers.Length; i++)
        {
            var layer = extraLayers[i];
            if (layer == null || !layer.hasPending) continue;

            layer.gateTarget = layer.pendingLevel;
            layer.pendingLevel = 0f;
            layer.hasPending = false;
        }
    }

    private ExtraLayer FindLayer(string layerName)
    {
        if (extraLayers == null) return null;
        for (int i = 0; i < extraLayers.Length; i++)
            if (extraLayers[i] != null && extraLayers[i].name == layerName) return extraLayers[i];
        return null;
    }

    // ------------------------------------------------------------------
    // Scene change
    // ------------------------------------------------------------------

    // Our scene is going away (or another scene took over): start fading out.
    private void OnSceneUnloaded(Scene scene)
    {
        if (scene == originScene) BeginFadeOut();
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (previous == originScene) BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        leaving = true;
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------

    private void Update()
    {
        // Unscaled time so fades also work while the game is paused. Capped so a loading
        // hitch can't make a fade jump.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        sceneTime += dt;

        if (leaving)
        {
            outProgress = fadeOutDuration <= 0f ? 0f : Mathf.MoveTowards(outProgress, 0f, dt / fadeOutDuration);
            if (outProgress <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }

        float outMultiplier = Mathf.SmoothStep(0f, 1f, outProgress);

        // Which "music bed" is playing: the main Music (0) or the highest-priority active
        // Replaces Music layer? The main Music crossfades with it.
        int winner = GetWinnerPriority();
        float mainStep = musicCrossfadeDuration <= 0f ? 1f : dt / musicCrossfadeDuration;
        mainBedGain = Mathf.MoveTowards(mainBedGain, winner == 0 ? 1f : 0f, mainStep);

        UpdateLayer(musicSource, clip, baseVolume * EqualPower(mainBedGain), startDelay, fadeInDuration, ref musicStarted, outMultiplier);
        UpdateLayer(environmentSource, environmentClip, environmentVolume, environmentStartDelay, environmentFadeInDuration, ref environmentStarted, outMultiplier);

        if (extraLayers == null) return;

        for (int i = 0; i < extraLayers.Length; i++)
        {
            var layer = extraLayers[i];
            if (layer == null || layer.source == null || layer.clip == null) continue;

            bool active = layer.gateTarget > 0.001f;
            float stepRate = layer.switchDuration <= 0f ? 1f : dt / layer.switchDuration;

            // Build-ups etc.: start from the beginning when switched on, but only while the
            // layer is still silent, so the jump can never be heard.
            float levelBefore = layer.replacesMusic ? layer.bedGain : layer.gate;
            if (active && !layer.wasActive && layer.restartOnActivate && layer.started && levelBefore < 0.02f)
                layer.source.time = 0f;
            layer.wasActive = active;

            float shown;   // 0..1: how much of this layer is audible right now
            if (layer.replacesMusic)
            {
                bool isWinner = active && Mathf.Max(1, layer.priority) == winner;
                layer.bedGain = Mathf.MoveTowards(layer.bedGain, isWinner ? 1f : 0f, stepRate);
                shown = EqualPower(layer.bedGain);
            }
            else
            {
                layer.gate = Mathf.MoveTowards(layer.gate, layer.gateTarget, stepRate);
                shown = Mathf.SmoothStep(0f, 1f, layer.gate);
            }

            // A silent layer keeps playing (volume 0) so it stays in sync with the others.
            UpdateLayer(layer.source, layer.clip, layer.volume * shown, layer.startDelay, layer.fadeInDuration, ref layer.started, outMultiplier);
        }
    }

    // 0 = the main Music plays. Otherwise the highest priority among active Replaces Music layers.
    private int GetWinnerPriority()
    {
        int winner = 0;
        if (extraLayers == null) return winner;

        for (int i = 0; i < extraLayers.Length; i++)
        {
            var layer = extraLayers[i];
            if (layer == null || layer.source == null || !layer.replacesMusic) continue;
            if (layer.gateTarget <= 0.001f) continue;

            winner = Mathf.Max(winner, Mathf.Max(1, layer.priority));
        }
        return winner;
    }

    // Equal-power fade: when one track fades out while another fades in, the total loudness
    // stays constant (no dip in the middle of a crossfade).
    private static float EqualPower(float x)
    {
        return Mathf.Sqrt(Mathf.SmoothStep(0f, 1f, x));
    }

    private void UpdateLayer(AudioSource source, AudioClip audioClip, float volume, float delay,
                             float fadeIn, ref bool started, float outMultiplier)
    {
        if (source == null || audioClip == null) return;

        if (!started)
        {
            if (leaving || sceneTime < delay) return;
            source.Play();
            started = true;
        }

        float t = fadeIn <= 0f ? 1f : Mathf.Clamp01((sceneTime - delay) / fadeIn);
        source.volume = volume * Mathf.SmoothStep(0f, 1f, t) * outMultiplier;
    }
}