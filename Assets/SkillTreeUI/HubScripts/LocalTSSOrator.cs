using System.Collections;
using System.IO;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Networking;

public class LocalTTSOrator : MonoBehaviour
{
    public static LocalTTSOrator Instance;

    [Header("Audio Reference")]
    public AudioSource audioSource;

    [Header("TTS Voice & Prosody Parameters")]
    [Tooltip("Global speech speed (e.g., slow, medium, fast, x-fast)")]
    public string speechRate = "fast";
    [Tooltip("Base pitch for the dialogue text (e.g., x-low, low, medium, high, x-high)")]
    public string bodyPitch = "low";
    [Tooltip("Pitch specifically for screaming the player's name")]
    public string namePitch = "x-high";
    [Tooltip("Speed specifically for screaming the player's name")]
    public string nameRate = "x-fast";

    [Header("Dynamic Graph Curves (0 to 1 Normalized Clip Duration)")]
    [Tooltip("Plot points here to control volume intensity dynamically over the course of the scream.")]
    public AnimationCurve volumeCurve = AnimationCurve.Linear(0, 1, 1, 1);
    [Tooltip("Plot points here to dynamically shift pitch up or down throughout playback.")]
    public AnimationCurve pitchCurve = AnimationCurve.Constant(0, 1, 0.88f);

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void TriggerNPCScream(string playerName)
    {
        StartCoroutine(SpeakRoutine(playerName));
    }

    private IEnumerator SpeakRoutine(string playerName)
    {
        yield return new WaitForSeconds(2f); 

        string cleanName = playerName.Trim();
        string tempPath = Path.Combine(Application.temporaryCachePath, "temp_npc_speech.wav").Replace("/", "\\");
        string xmlPath = Path.Combine(Application.temporaryCachePath, "temp_ssml.xml").Replace("/", "\\");

        if (File.Exists(tempPath)) File.Delete(tempPath);
        if (File.Exists(xmlPath)) File.Delete(xmlPath);

        // SSML string built using inspector-tweakable parameters
        string ssmlPayload = $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>" +
            $"<prosody rate='{speechRate}' pitch='{bodyPitch}' volume='x-loud'>" +
                $"<emphasis level='strong'>" +
                    $"<prosody pitch='{namePitch}' volume='x-loud' rate='{nameRate}'>{cleanName}!</prosody>" +
                $"</emphasis> " +
                $"<break time='40ms'/>" +
                $"WHAT ARE YOU STILL DOING HERE? GET OUT THERE AND GET KILLING!" +
            $"</prosody>" +
        $"</speak>";

        File.WriteAllText(xmlPath, ssmlPayload);

        string psCommand = $"Add-Type -AssemblyName System.Speech; " +
                           $"$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                           $"$synth.SetOutputToWaveFile('{tempPath}'); " +
                           $"$synth.SpeakSsml([System.IO.File]::ReadAllText('{xmlPath}')); " +
                           $"$synth.Dispose();";

        ProcessStartInfo psi = new ProcessStartInfo()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using (Process process = Process.Start(psi))
        {
            process.WaitForExit();
        }

        if (File.Exists(tempPath))
        {
            string url = "file://" + tempPath;
            using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    audioSource.clip = clip;
                    audioSource.Play();

                    // Evaluate the Inspector graphs in real-time over the clip's duration
                    yield return StartCoroutine(EvaluateGraphsRoutine(clip));
                }
                else
                {
                    UnityEngine.Debug.LogError("Failed to load local speech audio: " + www.error);
                }
            }
        }
        else
        {
            UnityEngine.Debug.LogError("Failed to generate speech file via Windows backend.");
        }
    }

    private IEnumerator EvaluateGraphsRoutine(AudioClip clip)
    {
        float duration = clip.length;
        float elapsed = 0f;

        while (elapsed < duration && audioSource.isPlaying)
        {
            float normalizedTime = elapsed / duration;
            
            // Sample the Inspector graphs from 0 to 1 progress
            audioSource.volume = volumeCurve.Evaluate(normalizedTime);
            audioSource.pitch = pitchCurve.Evaluate(normalizedTime);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Ensure final frame matches the end of the curve
        audioSource.volume = volumeCurve.Evaluate(1f);
        audioSource.pitch = pitchCurve.Evaluate(1f);
    }
}