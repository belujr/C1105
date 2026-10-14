using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// EDITOR-ONLY helper. Put this file in any folder named "Editor" (e.g. Assets/Editor).
///
///  1) Every time you press Play it makes sure Scene_Loading is in Build Settings
///     (the #1 reason a loading scene never shows up)[cite: 2].
///  2) Tools > Scene Transitions > Check and Fix Setup shows a report of what is wrong:
///     missing scene, outdated scripts, or other scripts that load scenes behind the manager's back[cite: 2].
/// </summary>
[InitializeOnLoad]
public static class SceneTransitionSetup
{
    // Must match "loadingSceneName" in SceneTransitionManager[cite: 2]
    private const string LoadingSceneName = "Scene_Loading";

    static SceneTransitionSetup()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;

            EnsureLoadingSceneInBuild(out string message, out bool changed);
            if (changed) Debug.Log("SceneTransitionSetup: " + message);
        };
    }

    [MenuItem("Tools/Scene Transitions/Check and Fix Setup")]
    private static void CheckAndFix()
    {
        var report = new StringBuilder();

        // ---- 1) Loading scene + Build Settings (fixed automatically if possible)[cite: 2]
        EnsureLoadingSceneInBuild(out string buildMessage, out _);
        report.AppendLine("1) LOADING SCENE");
        report.AppendLine("   " + buildMessage);
        report.AppendLine("   Scenes in Build Settings:");

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0) report.AppendLine("   (none)");
        for (int i = 0; i < scenes.Length; i++)
            report.AppendLine($"   {i}: {scenes[i].path}{(scenes[i].enabled ? "" : "   [DISABLED]")}");

        report.AppendLine("   (If your active Build Profile overrides the global scene list, add the scene there too.)");

        // ---- 2) Are the scripts the latest versions?[cite: 2]
        report.AppendLine();
        report.AppendLine("2) SCRIPTS");
        report.AppendLine("   " + CheckScript("SceneTransitionManager", "FindLoadingSceneIndex"));
        report.AppendLine("   " + CheckScript("RunPortal", "SceneTransitionManager.LoadScene"));
        report.AppendLine("   " + CheckScript("MapController", "SceneTransitionManager.LoadScene"));

        // ---- 3) Anything else loading scenes by itself?[cite: 2]
        report.AppendLine();
        report.AppendLine("3) SCRIPTS THAT LOAD SCENES DIRECTLY (these skip the loading screen)");

        var offenders = new StringBuilder();
        int found = 0;

        // Optimized file scanning loop with pre-filtering to prevent editor lag spikes
        foreach (string file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(file);
            if (fileName == "SceneTransitionManager.cs" || fileName == "SceneTransitionSetup.cs") continue;

            // Fast check: Read file content once and skip immediately if it doesn't touch loading
            string fileContent = File.ReadAllText(file);
            if (!fileContent.Contains("LoadScene")) continue;

            string[] lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("/*")) continue;

                if (line.Contains("SceneManager.LoadScene") || line.Contains("LoadSceneAsync"))
                {
                    found++;
                    if (found <= 12) offenders.AppendLine($"   {fileName}:{i + 1}   {line}");
                }
            }
        }

        report.Append(found == 0 ? "   None found (good).\n" : offenders.ToString());
        if (found > 12) report.AppendLine($"   ...and {found - 12} more (see Console).");

        string text = report.ToString();
        Debug.Log("SceneTransitionSetup report:\n" + text);

        string dialogText = text.Length > 1800 ? text.Substring(0, 1800) + "\n...(full report in Console)" : text;
        EditorUtility.DisplayDialog("Scene Transitions: Setup Check", dialogText, "OK");
    }

    // ------------------------------------------------------------------ helpers

    private static string FindScenePath(string sceneName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    /// <summary>Adds / enables Scene_Loading in Build Settings if needed.</summary>[cite: 2]
    private static bool EnsureLoadingSceneInBuild(out string message, out bool changed)
    {
        changed = false;
        string path = FindScenePath(LoadingSceneName);

        if (path == null)
        {
            message = $"PROBLEM: no scene named '{LoadingSceneName}' exists in your Assets folder. " +
                      "Create it, or rename your loading scene to exactly that.";
            return false;
        }

        var scenes = EditorBuildSettings.scenes.ToList();
        int index = scenes.FindIndex(s => s.path == path);

        if (index >= 0 && scenes[index].enabled)
        {
            message = $"OK: '{path}' is in Build Settings.";
            return true;
        }

        if (index >= 0) scenes[index].enabled = true;
        else scenes.Add(new EditorBuildSettingsScene(path, true));

        EditorBuildSettings.scenes = scenes.ToArray();
        changed = true;
        message = $"FIXED: '{path}' was missing/disabled in Build Settings, it is now added and enabled.";
        return true;
    }

    private static string CheckScript(string scriptName, string mustContain)
    {
        string path = AssetDatabase.FindAssets(scriptName + " t:MonoScript", new[] { "Assets" })
            .Select(g => AssetDatabase.GUIDToAssetPath(g))
            .FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), scriptName, StringComparison.OrdinalIgnoreCase));

        if (path == null) return $"{scriptName}: NOT FOUND in the project.";

        return File.ReadAllText(path).Contains(mustContain)
            ? $"{scriptName}: OK ({path})"
            : $"{scriptName}: OUTDATED, replace it with the latest version ({path})";
    }
}