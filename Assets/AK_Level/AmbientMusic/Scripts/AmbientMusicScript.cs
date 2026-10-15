using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

/// <summary>
/// Put this on an empty GameObject in each scene (forest, hub, loading screen, ...).
/// Plays two looping layers: Music, and Environment on top of it (wind, forest night, ...).
///
/// Both layers fade in when the scene starts. When the scene is left (for example when the
/// loading screen takes over) this object survives the scene change just long enough to
/// fade both layers out, then removes itself. You will see it move under
/// "DontDestroyOnLoad" in the Hierarchy while playing; that is expected.
///
/// The layers link themselves to the Audio Mixer of the Pause Menu Manager (groups "Music"
/// and "Ambience"), so the pause menu sliders control them. The Output fields are optional.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class AmbientMusic : MonoBehaviour
{
    [Header("Music Layer")]
    [SerializeField] private AudioClip clip;
    [Tooltip("Music level. Keep it low so it sits under the environment: try 0.15 - 0.3. Can be changed live in Play mode.")]
    [Range(0f, 1f)] [SerializeField] private float baseVolume = 0.25f;
    [SerializeField] private float startDelay = 0.5f;
    [SerializeField] private float fadeInDuration = 4f;
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

    [Header("Scene Change")]
    [Tooltip("When the scene is left, both layers fade out over this many seconds, even while the loading screen is showing.")]
    [SerializeField] private float fadeOutDuration = 2f;

    private AudioSource musicSource;
    private AudioSource environmentSource;
    private bool musicStarted;
    private bool environmentStarted;

    private float sceneTime;          // seconds since this scene started (drives delays and fade-ins)
    private Scene originScene;
    private bool leaving;
    private float outProgress = 1f;   // 1 -> 0 while fading out

    private void Awake()
    {
        originScene = gameObject.scene;

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
    }

    private void OnDestroy()
    {
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

        UpdateLayer(musicSource, clip, baseVolume, startDelay, fadeInDuration, ref musicStarted, outMultiplier);
        UpdateLayer(environmentSource, environmentClip, environmentVolume, environmentStartDelay, environmentFadeInDuration, ref environmentStarted, outMultiplier);
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