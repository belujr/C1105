using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// =====================================================================================================================
//  PLAY MODE SAVER
//
//  Unity throws away everything you change while the game is running. With this tool turned ON, the changes you make
//  while playing are remembered and applied back to your real scene when you stop the game:
//    - moving, rotating and scaling objects (Scene view handles or the Inspector)
//    - any value you change in the Inspector (FireVFX, AmbientVFX, lights, colors, curves, checkboxes ...)
//
//  Put this file in a folder called "Editor" anywhere inside Assets (for example Assets/Editor/PlayModeSaver.cs).
//  Open it from the menu:  Tools > Play Mode Saver > Open Window   (the big button turns it on and off)
//
//  What it cannot do: objects that only exist while playing (spawned enemies, generated effects), objects in
//  DontDestroyOnLoad, adding or deleting objects and components, and references to other scene objects.
//  Everything it applies can be undone with Ctrl+Z.
// =====================================================================================================================

[InitializeOnLoad]
public static class PlayModeSaver
{
    private const string KeyEnabled = "PlayModeSaver.Enabled";
    private const string KeyAutoSave = "PlayModeSaver.AutoSaveScene";
    private const string KeyPending = "PlayModeSaver.Pending";
    private const string KeyLastResult = "PlayModeSaver.LastResult";

    private static readonly string[] TransformPaths =
    {
        "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
        "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
        "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z"
    };

    public class Tracked
    {
        public UnityEngine.Object target;
        public string path;
    }

    [Serializable]
    public class Record
    {
        public string scene;
        public string indexPath;
        public string namePath;
        public string componentType;
        public int componentIndex;
        public string propertyPath;
        public string valueType;
        public string value;
    }

    [Serializable]
    private class RecordList
    {
        public List<Record> items = new List<Record>();
    }

    [Serializable]
    private class CurveBox
    {
        public AnimationCurve curve;
    }

    /// <summary>Everything the user changed during this play session (key = object id + property path).</summary>
    public static readonly Dictionary<string, Tracked> tracked = new Dictionary<string, Tracked>();
    public static int skippedRuntimeChanges;

    public static bool Enabled
    {
        get { return EditorPrefs.GetBool(KeyEnabled, false); }
        set { EditorPrefs.SetBool(KeyEnabled, value); }
    }

    public static bool AutoSaveScene
    {
        get { return EditorPrefs.GetBool(KeyAutoSave, false); }
        set { EditorPrefs.SetBool(KeyAutoSave, value); }
    }

    public static string LastResult
    {
        get { return SessionState.GetString(KeyLastResult, ""); }
        private set { SessionState.SetString(KeyLastResult, value); }
    }

    static PlayModeSaver()
    {
        Undo.postprocessModifications += OnPostprocessModifications;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    // ------------------------------------------------------------------ watching what you change
    private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
    {
        if (!Enabled || !Application.isPlaying) return modifications;

        for (int i = 0; i < modifications.Length; i++)
        {
            PropertyModification pm = modifications[i].currentValue;
            if (pm == null) continue;
            TrackProperty(pm.target, pm.propertyPath);
        }
        return modifications;
    }

    public static bool TrackProperty(UnityEngine.Object target, string path)
    {
        if (target == null || string.IsNullOrEmpty(path)) return false;
        if (path == "m_ObjectHideFlags") return false;
        if (EditorUtility.IsPersistent(target)) return false;   // assets save themselves

        GameObject go = GetGameObject(target);
        if (go == null) return false;

        // objects that only exist while playing, or are not in a saved scene, cannot be found again later
        if (string.IsNullOrEmpty(go.scene.path))
        {
            skippedRuntimeChanges++;
            return false;
        }

        string key = target.GetInstanceID() + "|" + path;
        if (!tracked.ContainsKey(key)) tracked.Add(key, new Tracked { target = target, path = path });
        return true;
    }

    /// <summary>Remember the current position, rotation and scale of the selected objects.</summary>
    public static int CaptureSelection()
    {
        int count = 0;
        GameObject[] selected = Selection.gameObjects;
        for (int i = 0; i < selected.Length; i++)
        {
            Transform t = selected[i].transform;
            bool any = false;
            for (int p = 0; p < TransformPaths.Length; p++)
                if (TrackProperty(t, TransformPaths[p])) any = true;
            if (any) count++;
        }
        return count;
    }

    private static GameObject GetGameObject(UnityEngine.Object o)
    {
        GameObject g = o as GameObject;
        if (g != null) return g;
        Component c = o as Component;
        return c != null ? c.gameObject : null;
    }

    // ------------------------------------------------------------------ play mode events
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.EnteredPlayMode:
                tracked.Clear();
                skippedRuntimeChanges = 0;
                break;

            case PlayModeStateChange.ExitingPlayMode:
                if (Enabled) CollectAndStore();
                else SessionState.EraseString(KeyPending);
                tracked.Clear();
                break;

            case PlayModeStateChange.EnteredEditMode:
                // wait one moment so the scene is fully restored
                EditorApplication.delayCall += ApplyStored;
                break;
        }
    }

    // ------------------------------------------------------------------ store (while still playing)
    private static void CollectAndStore()
    {
        RecordList list = new RecordList();
        int skipped = 0;

        foreach (KeyValuePair<string, Tracked> pair in tracked)
        {
            Tracked tr = pair.Value;
            if (tr.target == null) { skipped++; continue; }

            GameObject go = GetGameObject(tr.target);
            if (go == null) { skipped++; continue; }

            SerializedObject so = new SerializedObject(tr.target);
            SerializedProperty sp = so.FindProperty(tr.path);
            if (sp == null) { skipped++; continue; }

            string type, value;
            if (!TryEncode(sp, out type, out value)) { skipped++; continue; }

            Record r = new Record
            {
                scene = go.scene.path,
                indexPath = IndexPath(go.transform),
                namePath = NamePath(go.transform),
                propertyPath = tr.path,
                valueType = type,
                value = value,
                componentType = "",
                componentIndex = 0
            };

            Component c = tr.target as Component;
            if (c != null)
            {
                r.componentType = c.GetType().FullName;
                r.componentIndex = ComponentIndex(go, c);
            }
            list.items.Add(r);
        }

        SessionState.SetString(KeyPending, JsonUtility.ToJson(list));

        if (skipped > 0 || skippedRuntimeChanges > 0)
        {
            Debug.Log("[PlayModeSaver] " + (skipped + skippedRuntimeChanges) +
                      " change(s) could not be kept (objects that only exist while playing, or values that cannot be saved).");
        }
    }

    // ------------------------------------------------------------------ apply (back in edit mode)
    private static void ApplyStored()
    {
        string json = SessionState.GetString(KeyPending, "");
        if (string.IsNullOrEmpty(json)) return;
        SessionState.EraseString(KeyPending);

        RecordList list = JsonUtility.FromJson<RecordList>(json);
        if (list == null || list.items.Count == 0) return;

        // one group per object / component, so each one is applied in a single go
        Dictionary<string, List<Record>> groups = new Dictionary<string, List<Record>>();
        for (int i = 0; i < list.items.Count; i++)
        {
            Record r = list.items[i];
            string key = r.scene + "|" + r.indexPath + "|" + r.componentType + "|" + r.componentIndex;
            List<Record> g;
            if (!groups.TryGetValue(key, out g))
            {
                g = new List<Record>();
                groups.Add(key, g);
            }
            g.Add(r);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Play Mode Saver");

        int applied = 0, failed = 0, objects = 0;
        HashSet<Scene> dirtyScenes = new HashSet<Scene>();

        foreach (KeyValuePair<string, List<Record>> pair in groups)
        {
            List<Record> records = pair.Value;
            Record first = records[0];

            GameObject go = ResolveGameObject(first);
            if (go == null) { failed += records.Count; continue; }

            UnityEngine.Object target = go;
            if (!string.IsNullOrEmpty(first.componentType))
            {
                target = ResolveComponent(go, first);
                if (target == null) { failed += records.Count; continue; }
            }

            SerializedObject so = new SerializedObject(target);

            // array sizes first, so the elements exist before their values are set
            bool resized = false;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].valueType != "arraysize") continue;
                SerializedProperty sp = so.FindProperty(records[i].propertyPath);
                if (sp != null && TryDecode(sp, records[i].valueType, records[i].value)) { applied++; resized = true; }
                else failed++;
            }
            if (resized)
            {
                so.ApplyModifiedProperties();
                so.Update();
            }

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].valueType == "arraysize") continue;
                SerializedProperty sp = so.FindProperty(records[i].propertyPath);
                if (sp != null && TryDecode(sp, records[i].valueType, records[i].value)) applied++;
                else failed++;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            dirtyScenes.Add(go.scene);
            objects++;
        }

        foreach (Scene s in dirtyScenes) EditorSceneManager.MarkSceneDirty(s);
        Undo.CollapseUndoOperations(undoGroup);

        if (AutoSaveScene && dirtyScenes.Count > 0) EditorSceneManager.SaveOpenScenes();

        string message = "Kept " + applied + " change(s) on " + objects + " object(s)" +
                         (failed > 0 ? ", " + failed + " could not be applied (the object was not found)" : "") + ". " +
                         (AutoSaveScene ? "Scene saved." : "Press Ctrl+S to save the scene.") + " Ctrl+Z undoes it.";
        LastResult = message;
        Debug.Log("[PlayModeSaver] " + message);
    }

    // ------------------------------------------------------------------ finding the same object again
    private static string IndexPath(Transform t)
    {
        List<int> indices = new List<int>();
        while (t != null)
        {
            indices.Add(t.GetSiblingIndex());
            t = t.parent;
        }
        indices.Reverse();
        return string.Join("/", indices.ConvertAll(x => x.ToString()).ToArray());
    }

    private static string NamePath(Transform t)
    {
        List<string> names = new List<string>();
        while (t != null)
        {
            names.Add(t.name);
            t = t.parent;
        }
        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static int ComponentIndex(GameObject go, Component c)
    {
        string typeName = c.GetType().FullName;
        int index = 0;
        Component[] all = go.GetComponents<Component>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            if (all[i] == c) return index;
            if (all[i].GetType().FullName == typeName) index++;
        }
        return 0;
    }

    private static GameObject ResolveGameObject(Record r)
    {
        Scene scene = SceneManager.GetSceneByPath(r.scene);
        if (!scene.IsValid() || !scene.isLoaded) return null;

        GameObject[] roots = scene.GetRootGameObjects();
        string[] idx = r.indexPath.Split('/');
        string[] names = r.namePath.Split('/');

        // 1) by position in the hierarchy (and the names must match)
        bool ok = idx.Length == names.Length;
        Transform t = null;
        for (int i = 0; ok && i < idx.Length; i++)
        {
            int index;
            if (!int.TryParse(idx[i], out index)) { ok = false; break; }

            if (i == 0)
            {
                if (index < 0 || index >= roots.Length) { ok = false; break; }
                t = roots[index].transform;
            }
            else
            {
                if (index < 0 || index >= t.childCount) { ok = false; break; }
                t = t.GetChild(index);
            }

            if (t.name != names[i]) ok = false;
        }
        if (ok && t != null) return t.gameObject;

        // 2) by name, if the order changed
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name != names[0]) continue;
            if (names.Length == 1) return roots[i];
            Transform found = roots[i].transform.Find(string.Join("/", names, 1, names.Length - 1));
            if (found != null) return found.gameObject;
        }
        return null;
    }

    private static Component ResolveComponent(GameObject go, Record r)
    {
        int index = 0;
        Component[] all = go.GetComponents<Component>();
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].GetType().FullName != r.componentType) continue;
            if (index == r.componentIndex) return all[i];
            index++;
        }
        return null;
    }

    // ------------------------------------------------------------------ values <-> text
    private static string F(float v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    private static float ParseF(string s)
    {
        float v;
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return v;
    }

    private static float[] ParseList(string s)
    {
        string[] parts = s.Split(',');
        float[] result = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) result[i] = ParseF(parts[i]);
        return result;
    }

    private static bool TryEncode(SerializedProperty sp, out string type, out string value)
    {
        type = "";
        value = "";

        switch (sp.propertyType)
        {
            case SerializedPropertyType.Integer:
                type = "int"; value = sp.intValue.ToString(CultureInfo.InvariantCulture); return true;
            case SerializedPropertyType.Boolean:
                type = "bool"; value = sp.boolValue ? "1" : "0"; return true;
            case SerializedPropertyType.Float:
                type = "float"; value = F(sp.floatValue); return true;
            case SerializedPropertyType.String:
                type = "string"; value = sp.stringValue ?? ""; return true;
            case SerializedPropertyType.Color:
                { Color c = sp.colorValue; type = "color"; value = F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a); return true; }
            case SerializedPropertyType.Vector2:
                { Vector2 v = sp.vector2Value; type = "vector2"; value = F(v.x) + "," + F(v.y); return true; }
            case SerializedPropertyType.Vector3:
                { Vector3 v = sp.vector3Value; type = "vector3"; value = F(v.x) + "," + F(v.y) + "," + F(v.z); return true; }
            case SerializedPropertyType.Vector4:
                { Vector4 v = sp.vector4Value; type = "vector4"; value = F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w); return true; }
            case SerializedPropertyType.Quaternion:
                { Quaternion q = sp.quaternionValue; type = "quaternion"; value = F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w); return true; }
            case SerializedPropertyType.Rect:
                { Rect r = sp.rectValue; type = "rect"; value = F(r.x) + "," + F(r.y) + "," + F(r.width) + "," + F(r.height); return true; }
            case SerializedPropertyType.Enum:
                type = "enum"; value = sp.enumValueIndex.ToString(CultureInfo.InvariantCulture); return true;
            case SerializedPropertyType.LayerMask:
                type = "layermask"; value = sp.intValue.ToString(CultureInfo.InvariantCulture); return true;
            case SerializedPropertyType.ArraySize:
                type = "arraysize"; value = sp.intValue.ToString(CultureInfo.InvariantCulture); return true;
            case SerializedPropertyType.AnimationCurve:
                type = "curve"; value = JsonUtility.ToJson(new CurveBox { curve = sp.animationCurveValue }); return true;
            case SerializedPropertyType.ObjectReference:
                return TryEncodeObject(sp.objectReferenceValue, out type, out value);
        }
        return false;
    }

    // references to ASSETS (materials, textures, prefabs ...) can be kept; references to scene objects cannot
    private static bool TryEncodeObject(UnityEngine.Object o, out string type, out string value)
    {
        type = "asset";
        value = "";
        if (o == null) { value = "null"; return true; }
        if (!EditorUtility.IsPersistent(o)) return false;

        string path = AssetDatabase.GetAssetPath(o);
        if (string.IsNullOrEmpty(path)) return false;
        value = path + "|" + o.name + "|" + o.GetType().AssemblyQualifiedName;
        return true;
    }

    private static bool TryDecode(SerializedProperty sp, string type, string value)
    {
        try
        {
            switch (type)
            {
                case "int": if (sp.propertyType != SerializedPropertyType.Integer) return false; sp.intValue = int.Parse(value, CultureInfo.InvariantCulture); return true;
                case "bool": if (sp.propertyType != SerializedPropertyType.Boolean) return false; sp.boolValue = value == "1"; return true;
                case "float": if (sp.propertyType != SerializedPropertyType.Float) return false; sp.floatValue = ParseF(value); return true;
                case "string": if (sp.propertyType != SerializedPropertyType.String) return false; sp.stringValue = value; return true;
                case "color": { float[] f = ParseList(value); sp.colorValue = new Color(f[0], f[1], f[2], f[3]); return true; }
                case "vector2": { float[] f = ParseList(value); sp.vector2Value = new Vector2(f[0], f[1]); return true; }
                case "vector3": { float[] f = ParseList(value); sp.vector3Value = new Vector3(f[0], f[1], f[2]); return true; }
                case "vector4": { float[] f = ParseList(value); sp.vector4Value = new Vector4(f[0], f[1], f[2], f[3]); return true; }
                case "quaternion": { float[] f = ParseList(value); sp.quaternionValue = new Quaternion(f[0], f[1], f[2], f[3]); return true; }
                case "rect": { float[] f = ParseList(value); sp.rectValue = new Rect(f[0], f[1], f[2], f[3]); return true; }
                case "enum": sp.enumValueIndex = int.Parse(value, CultureInfo.InvariantCulture); return true;
                case "layermask": sp.intValue = int.Parse(value, CultureInfo.InvariantCulture); return true;
                case "arraysize": sp.intValue = int.Parse(value, CultureInfo.InvariantCulture); return true;
                case "curve":
                    {
                        CurveBox box = JsonUtility.FromJson<CurveBox>(value);
                        if (box == null || box.curve == null) return false;
                        sp.animationCurveValue = box.curve;
                        return true;
                    }
                case "asset": return DecodeObject(sp, value);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[PlayModeSaver] Could not apply " + sp.propertyPath + ": " + e.Message);
        }
        return false;
    }

    private static bool DecodeObject(SerializedProperty sp, string value)
    {
        if (sp.propertyType != SerializedPropertyType.ObjectReference) return false;
        if (value == "null") { sp.objectReferenceValue = null; return true; }

        string[] parts = value.Split('|');
        if (parts.Length < 3) return false;

        string path = parts[0], assetName = parts[1];
        Type type = Type.GetType(parts[2]);

        UnityEngine.Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].name != assetName) continue;
            if (type != null && !type.IsInstanceOfType(all[i])) continue;
            sp.objectReferenceValue = all[i];
            return true;
        }
        return false;
    }
}

// =====================================================================================================================
//  The window with the on / off button
// =====================================================================================================================
public class PlayModeSaverWindow : EditorWindow
{
    private Vector2 scroll;

    [MenuItem("Tools/Play Mode Saver/Open Window")]
    public static void Open()
    {
        PlayModeSaverWindow w = GetWindow<PlayModeSaverWindow>("Play Mode Saver");
        w.minSize = new Vector2(320f, 260f);
    }

    [MenuItem("Tools/Play Mode Saver/Enabled")]
    private static void ToggleEnabled()
    {
        PlayModeSaver.Enabled = !PlayModeSaver.Enabled;
    }

    [MenuItem("Tools/Play Mode Saver/Enabled", true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked("Tools/Play Mode Saver/Enabled", PlayModeSaver.Enabled);
        return true;
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        bool on = PlayModeSaver.Enabled;

        Color old = GUI.backgroundColor;
        GUI.backgroundColor = on ? new Color(0.35f, 0.85f, 0.4f) : new Color(0.75f, 0.75f, 0.75f);
        string label = on
            ? "PLAY MODE SAVER: ON\nChanges you make while playing are kept"
            : "PLAY MODE SAVER: OFF\nClick to turn on";
        if (GUILayout.Button(label, GUILayout.Height(54f))) PlayModeSaver.Enabled = !on;
        GUI.backgroundColor = old;

        EditorGUILayout.Space();
        PlayModeSaver.AutoSaveScene = EditorGUILayout.ToggleLeft("Save the scene automatically after applying", PlayModeSaver.AutoSaveScene);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "How to use: turn it ON, press Play, then move objects in the Scene view or change values in the Inspector. " +
            "When you stop the game, those changes are applied to the real scene (Ctrl+Z undoes them, Ctrl+S saves the scene).\n\n" +
            "Not kept: objects that only exist while playing (spawned enemies, generated effects), added or deleted objects.",
            MessageType.Info);

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Captured in this play session", EditorStyles.boldLabel);

            HashSet<UnityEngine.Object> objects = new HashSet<UnityEngine.Object>();
            foreach (KeyValuePair<string, PlayModeSaver.Tracked> pair in PlayModeSaver.tracked)
                if (pair.Value.target != null) objects.Add(pair.Value.target);

            EditorGUILayout.LabelField(PlayModeSaver.tracked.Count + " value(s) on " + objects.Count + " object(s)");
            if (PlayModeSaver.skippedRuntimeChanges > 0)
                EditorGUILayout.LabelField(PlayModeSaver.skippedRuntimeChanges + " ignored (objects that only exist while playing)");

            if (!on)
                EditorGUILayout.HelpBox("The saver is OFF, so nothing is being captured.", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture selected objects now"))
            {
                int n = PlayModeSaver.CaptureSelection();
                if (!on) Debug.LogWarning("[PlayModeSaver] Turn the saver ON first, it only keeps changes while it is ON.");
                else Debug.Log("[PlayModeSaver] Captured the position, rotation and scale of " + n + " object(s).");
            }
            if (GUILayout.Button("Discard captured"))
            {
                PlayModeSaver.tracked.Clear();
                PlayModeSaver.skippedRuntimeChanges = 0;
            }
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(80f));
            foreach (UnityEngine.Object o in objects)
            {
                if (o == null) continue;
                Component c = o as Component;
                string title = c != null ? c.gameObject.name + "  (" + c.GetType().Name + ")" : o.name;
                if (GUILayout.Button(title, EditorStyles.label)) Selection.activeObject = o;
            }
            EditorGUILayout.EndScrollView();
        }
        else
        {
            string last = PlayModeSaver.LastResult;
            if (!string.IsNullOrEmpty(last))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last time", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(last, MessageType.None);
            }
        }
    }
}
