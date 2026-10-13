using UnityEngine;

// Single source of truth for the current run. Uses the same key as HubNarrativeManager,
// so it works from any scene (e.g. scene_level on death) without needing the manager.
public static class RunState
{
    private const string Key = "Narrative_CurrentRunNumber";

    public static int Current => PlayerPrefs.GetInt(Key, 0);

    public static void Advance()
    {
        PlayerPrefs.SetInt(Key, Current + 1);
        PlayerPrefs.Save();
    }
}