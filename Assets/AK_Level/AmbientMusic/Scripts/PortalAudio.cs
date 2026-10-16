using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Put this on a Portal GameObject. The closer the player is, the louder the portal gets.
///
/// Two things are driven by the player's distance to the portal (smoothly, no jumps):
///   1. The portal's own looping sound (Loop Clip), played from the portal.
///   2. An optional music layer in AmbientMusic (Music Layer Name), which can have a wider range.
///
/// Distance is measured from the PLAYER (not from the camera), so it behaves the same
/// whatever the camera does. Directional Panning adds a left/right feel on top.
///
/// Optional one-shots:
///   - Approach clip: plays once each time you enter range.
///   - Pass Through clip: call PlayPassThrough() from your teleport script.
///
/// Everything is editable live in Play mode. From code: SetEnabled(bool), SetVolume(float).
/// The loop links itself to the mixer group named "SFX" (change the name below if needed).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PortalAudio : MonoBehaviour
{
    [Header("Master")]
    [Tooltip("Switch the whole portal sound on/off. Also available from code: SetEnabled(bool).")]
    [SerializeField] private bool soundEnabled = true;

    [Header("Loop (plays while the player is near)")]
    [SerializeField] private AudioClip loopClip;
    [Range(0f, 1f)] [SerializeField] private float volume = 0.6f;
    [Tooltip("Inside this distance the loop is at full volume.")]
    [SerializeField] private float minDistance = 2f;
    [Tooltip("Beyond this distance the loop is silent (and stops playing).")]
    [SerializeField] private float maxDistance = 15f;
    [Tooltip("How volume follows distance. Left = at Max Distance (silent), right = at Min Distance (full). Drag the curve to taste.")]
    [SerializeField] private AnimationCurve proximityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Seconds of smoothing on volume changes. Higher = softer, lower = snappier (try 0.2 - 0.5).")]
    [SerializeField] private float smoothTime = 0.3f;
    [Tooltip("0 = same in both ears. 1 = fully positional (sound comes from the portal's side). Distance volume is NOT affected by this.")]
    [Range(0f, 1f)] [SerializeField] private float directionalPanning = 0.5f;

    [Header("One-shots (optional)")]
    [Tooltip("Plays once each time the player enters range (a whoosh, a shimmer...). Leave empty for none.")]
    [SerializeField] private AudioClip approachClip;
    [Range(0f, 1f)] [SerializeField] private float approachVolume = 0.8f;
    [Tooltip("Minimum seconds between two approach sounds.")]
    [SerializeField] private float approachCooldown = 3f;
    [Tooltip("Played when you call PlayPassThrough() (e.g. when the player is teleported). Leave empty for none.")]
    [SerializeField] private AudioClip passThroughClip;
    [Range(0f, 1f)] [SerializeField] private float passThroughVolume = 1f;

    [Header("Music Layer Link (optional)")]
    [Tooltip("Name of an Extra Layer in AmbientMusic (e.g. \"Portal\"). Its volume follows your distance. Set that layer's Start Active to OFF. Leave empty for none.")]
    [SerializeField] private string musicLayerName = "";
    [Tooltip("Inside this distance the music layer is at full volume.")]
    [SerializeField] private float musicMinDistance = 5f;
    [Tooltip("Beyond this distance the music layer is silent. Usually larger than the loop's Max Distance.")]
    [SerializeField] private float musicMaxDistance = 30f;

    [Header("Who counts as 'near'?")]
    [Tooltip("Leave empty to use the object tagged 'Player' (or the main camera if there is none).")]
    [SerializeField] private Transform target;

    [Header("Mixer")]
    [Tooltip("Optional. Leave empty to link automatically to the group named below.")]
    [SerializeField] private AudioMixerGroup output;
    [SerializeField] private string sfxGroupName = "SFX";

    [Header("Debug")]
    [Tooltip("Logs to the Console when the player enters/leaves range and when the music layer starts/stops. Turn off when everything works.")]
    [SerializeField] private bool debugLog = false;

    private const float SilenceThreshold = 0.0005f;

    private AudioSource loopSource;
    private AudioSource oneShotSource;
    private bool loopActive;

    private float presence, presenceVel;            // loop: 0..1 smoothed proximity
    private float musicPresence, musicVel;          // music layer: 0..1 smoothed proximity
    private float lastSentLevel;
    private AmbientMusic checkedMusic;
    private bool warnedNoMusic;

    private bool wasNear;
    private bool passedThrough;   // set by PlayPassThrough(): the player is leaving through this portal
    private float lastApproachTime = -999f;
    private float findTimer;

    public bool IsEnabled => soundEnabled;

    private void Awake()
    {
        loopSource = GetComponent<AudioSource>();
        loopSource.clip = loopClip;
        loopSource.loop = true;
        loopSource.playOnAwake = false;
        loopSource.volume = 0f;
        ApplySpatialSettings(loopSource);

        // A separate source for one-shots, so they aren't affected by the loop's changing volume.
        var go = new GameObject("PortalOneShots");
        go.transform.SetParent(transform, false);
        oneShotSource = go.AddComponent<AudioSource>();
        oneShotSource.playOnAwake = false;
        ApplySpatialSettings(oneShotSource);
    }

    private void Start()
    {
        // By now the Pause Menu Manager has awakened.
        if (output == null)
            output = PauseMenuManager.FindMixerGroup(sfxGroupName);

        if (output != null)
        {
            loopSource.outputAudioMixerGroup = output;
            oneShotSource.outputAudioMixerGroup = output;
        }
        else
        {
            Debug.LogWarning("PortalAudio: couldn't link to the pause menu's mixer. Make sure it has a group named \"" +
                             sfxGroupName + "\" (or assign Output by hand).", this);
        }
    }

    // We do the distance volume ourselves (from the player), so Unity's own distance
    // attenuation is switched off with a flat rolloff. Panning still works through Spatial Blend.
    private void ApplySpatialSettings(AudioSource source)
    {
        source.spatialBlend = directionalPanning;
        source.dopplerLevel = 0f;                 // no pitch wobble when the camera moves
        source.minDistance = 1f;
        source.maxDistance = 1000f;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
    }

    // Lets you tweak panning live while playing.
    private void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (loopSource != null) ApplySpatialSettings(loopSource);
        if (oneShotSource != null) ApplySpatialSettings(oneShotSource);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void SetEnabled(bool on) { soundEnabled = on; }

    public void SetVolume(float newVolume) { volume = Mathf.Clamp01(newVolume); }

    /// <summary>
    /// Call this when the player goes through the portal (RunPortal does it for you).
    /// The sound plays from a temporary object that survives the scene change, so it isn't
    /// cut off when this portal's scene unloads. The portal's loop fades out quickly.
    /// </summary>
    public void PlayPassThrough()
    {
        passedThrough = true;   // loop fades out so it never gets chopped by the scene unload

        if (!soundEnabled || passThroughClip == null) return;

        var go = new GameObject("PortalPassThroughSound");
        DontDestroyOnLoad(go);

        var src = go.AddComponent<AudioSource>();
        src.clip = passThroughClip;
        src.volume = passThroughVolume;
        src.spatialBlend = 0f;                  // 2D: the player is leaving, so no position needed
        src.playOnAwake = false;
        if (output != null) src.outputAudioMixerGroup = output;
        src.Play();

        Destroy(go, passThroughClip.length + 0.25f);
    }

    // ------------------------------------------------------------------
    // Per-frame
    // ------------------------------------------------------------------

    private void Update()
    {
        // Unscaled time so smoothing also works while the game is paused.
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        if (target == null)
        {
            findTimer -= dt;
            if (findTimer <= 0f)
            {
                target = FindTarget();
                findTimer = 0.5f;   // don't search every frame
            }
        }

        float dist = target != null ? Vector3.Distance(target.position, transform.position) : float.MaxValue;
        bool near = dist <= maxDistance;

        if (debugLog && near != wasNear)
            Debug.Log("PortalAudio: player " + (near ? "ENTERED" : "LEFT") + " range of " + name +
                      " (target: " + (target != null ? target.name : "none") +
                      ", distance " + (target != null ? dist.ToString("F1") : "?") +
                      ", max " + maxDistance + ")", this);

        if (near && !wasNear) OnPlayerEntered();
        wasNear = near;

        UpdateLoop(dt, dist);
        UpdateMusicLayer(dt, dist);
    }

    private void UpdateLoop(float dt, float dist)
    {
        if (loopClip == null) return;

        // After PlayPassThrough() the loop fades out fast, so it is silent before the scene unloads.
        float goal = (soundEnabled && !passedThrough) ? Proximity(dist, minDistance, maxDistance) : 0f;
        float time = passedThrough ? Mathf.Min(smoothTime, 0.12f) : smoothTime;
        presence = Smooth(presence, goal, ref presenceVel, time, dt);

        if (presence > SilenceThreshold)
        {
            loopSource.volume = volume * presence;      // set before Play so it never starts loud
            if (!loopActive)
            {
                loopSource.Play();
                loopActive = true;
            }
        }
        else if (loopActive)
        {
            loopSource.Stop();                          // already silent, so no click
            loopSource.volume = 0f;
            loopActive = false;
        }
    }

    private void UpdateMusicLayer(float dt, float dist)
    {
        if (string.IsNullOrEmpty(musicLayerName)) return;

        float goal = soundEnabled ? Proximity(dist, musicMinDistance, musicMaxDistance) : 0f;
        musicPresence = Smooth(musicPresence, goal, ref musicVel, smoothTime, dt);

        var music = AmbientMusic.Current;
        if (music == null)
        {
            if (goal > 0f && !warnedNoMusic)
            {
                warnedNoMusic = true;
                Debug.LogWarning("PortalAudio: wants to drive the music layer \"" + musicLayerName +
                                 "\" but there is no AmbientMusic object in the scene.", this);
            }
            return;
        }

        if (checkedMusic != music)
        {
            checkedMusic = music;
            if (!music.HasLayer(musicLayerName))
                Debug.LogWarning("PortalAudio: AmbientMusic has no extra layer named \"" + musicLayerName + "\".", this);
        }

        // Send the level while it's audible, plus one final 0 when it has faded out.
        float send = musicPresence > SilenceThreshold ? musicPresence : 0f;
        if (send > 0f || lastSentLevel > 0f)
        {
            if (debugLog && lastSentLevel <= 0f && send > 0f)
                Debug.Log("PortalAudio: music layer \"" + musicLayerName + "\" fading IN", this);
            if (debugLog && lastSentLevel > 0f && send <= 0f)
                Debug.Log("PortalAudio: music layer \"" + musicLayerName + "\" faded OUT", this);

            music.RequestLayerLevel(musicLayerName, send);
            lastSentLevel = send;
        }
    }

    private void OnDisable()
    {
        if (loopSource != null && loopActive)
        {
            loopSource.Stop();
            loopActive = false;
        }
        presence = 0f; presenceVel = 0f;
        passedThrough = false;

        // Don't leave the music layer on if this portal goes away while the player is near.
        if (lastSentLevel > 0f && AmbientMusic.Current != null)
            AmbientMusic.Current.RequestLayerLevel(musicLayerName, 0f);
        lastSentLevel = 0f;
        musicPresence = 0f; musicVel = 0f;
    }

    // 0 at maxDist (and beyond), 1 at minDist (and closer), shaped by the curve.
    private float Proximity(float dist, float minDist, float maxDist)
    {
        if (maxDist <= minDist) return dist <= minDist ? 1f : 0f;

        float t = Mathf.InverseLerp(maxDist, minDist, dist);
        if (proximityCurve != null && proximityCurve.length > 0)
            t = Mathf.Clamp01(proximityCurve.Evaluate(t));
        return t;
    }

    private float Smooth(float current, float goal, ref float velocity, float time, float dt)
    {
        if (time <= 0f) { velocity = 0f; return goal; }
        if (dt <= 0f) return current;
        return Mathf.Clamp01(Mathf.SmoothDamp(current, goal, ref velocity, time, Mathf.Infinity, dt));
    }

    private void OnPlayerEntered()
    {
        if (!soundEnabled || approachClip == null) return;
        if (Time.unscaledTime - lastApproachTime < approachCooldown) return;

        lastApproachTime = Time.unscaledTime;
        oneShotSource.PlayOneShot(approachClip, approachVolume);
    }

    private static Transform FindTarget()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) return player.transform;
        return Camera.main != null ? Camera.main.transform : null;
    }

    // Select the portal in the Scene view to see the ranges.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);     // loop: full volume
        Gizmos.DrawWireSphere(transform.position, minDistance);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);     // loop: silent
        Gizmos.DrawWireSphere(transform.position, maxDistance);

        if (!string.IsNullOrEmpty(musicLayerName))
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);  // music layer: full / silent
            Gizmos.DrawWireSphere(transform.position, musicMinDistance);
            Gizmos.DrawWireSphere(transform.position, musicMaxDistance);
        }
    }
}