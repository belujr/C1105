#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// FIXES VFX THAT WORK IN THE EDITOR BUT ARE MISSING IN BUILDS.
///
/// Why it happens: the VFX scripts (FireVFX, AmbientVFX, PlayerHitFX, AOEShockwaveVFX, DashVFX...) find their shaders with
/// Shader.Find("VFX/..."). In the editor that always works, but a built game only contains shaders that some material or
/// scene references. A shader that nothing references is left out of the build, Shader.Find returns null,
/// and the effect silently does not appear.
///
/// What this does: adds every shader whose name starts with "VFX/" or "Dash/" to
/// Project Settings > Graphics > "Always Included Shaders". It runs automatically before EVERY build, and you can also
/// run it by hand from the menu:  Tools > VFX > Add Custom Shaders To Builds
///
/// Put this file in a folder called "Editor" (for example Assets/Editor/).
/// </summary>
public class VFXShaderIncluder : IPreprocessBuildWithReport
{
	// every shader whose name starts with one of these is added
	private static readonly string[] Prefixes = { "VFX/", "Dash/" };

	public int callbackOrder { get { return 0; } }

	public void OnPreprocessBuild(BuildReport report)
	{
		AddShaders(false);
	}

	[MenuItem("Tools/VFX/Add Custom Shaders To Builds")]
	public static void AddFromMenu()
	{
		AddShaders(true);
	}

	private static bool HasPrefix(string shaderName)
	{
		for (int i = 0; i < Prefixes.Length; i++)
			if (shaderName.StartsWith(Prefixes[i])) return true;
		return false;
	}

	private static void AddShaders(bool verbose)
	{
		Object graphicsSettings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
		if (graphicsSettings == null)
		{
			Debug.LogError("[VFXShaderIncluder] Could not open ProjectSettings/GraphicsSettings.asset. " +
				"Add the shaders by hand in Project Settings > Graphics > Always Included Shaders.");
			return;
		}

		SerializedObject so = new SerializedObject(graphicsSettings);
		SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
		if (list == null)
		{
			Debug.LogError("[VFXShaderIncluder] 'Always Included Shaders' was not found in the Graphics settings.");
			return;
		}

		HashSet<Shader> present = new HashSet<Shader>();
		for (int i = 0; i < list.arraySize; i++)
		{
			Shader s = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
			if (s != null) present.Add(s);
		}

		List<string> addedNames = new List<string>();
		string[] guids = AssetDatabase.FindAssets("t:Shader");
		for (int g = 0; g < guids.Length; g++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[g]);
			if (!path.StartsWith("Assets/")) continue;

			Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
			if (shader == null || !HasPrefix(shader.name) || present.Contains(shader)) continue;

			int index = list.arraySize;
			list.InsertArrayElementAtIndex(index);
			list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
			present.Add(shader);
			addedNames.Add(shader.name);
		}

		if (addedNames.Count > 0)
		{
			so.ApplyModifiedProperties();
			AssetDatabase.SaveAssets();
			Debug.Log("[VFXShaderIncluder] Added " + addedNames.Count + " shader(s) to 'Always Included Shaders': " + string.Join(", ", addedNames));
		}
		else if (verbose)
		{
			Debug.Log("[VFXShaderIncluder] Nothing to add - every shader starting with " + string.Join(" / ", Prefixes) + " is already included.");
		}
	}
}
#endif
