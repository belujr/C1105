// Put in an "Editor" folder. Adds test buttons to the ShieldDissolve and BeaconHealth inspectors.
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShieldDissolve))]
public class ShieldDissolveEditor : Editor
{
    float preview;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var t = (ShieldDissolve)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test", EditorStyles.boldLabel);

        // Works in edit mode AND play mode: drag to tune the look. Set back to 0 when done.
        EditorGUI.BeginChangeCheck();
        preview = EditorGUILayout.Slider("Preview Dissolve", preview, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) t.SetDissolve(preview);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Break (animated)")) t.Break();
            if (GUILayout.Button("Restore")) t.Restore();
            EditorGUILayout.EndHorizontal();
        }
        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Break / Restore buttons work in Play mode.", MessageType.None);
    }
}

[CustomEditor(typeof(BeaconHealth))]
public class BeaconHealthEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var b = (BeaconHealth)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test Buttons (Play Mode)", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("1. Activate / Register Kill (+1)"))
                b.RegisterEnemyKilled();

            if (GUILayout.Button("2. Hit Shield (test ripple)"))
                b.TakeDamage(1f, b.transform.position + Random.onUnitSphere * 3f, Vector3.up);

            if (GUILayout.Button("3. Fill Quota (drops shield -> dissolve)"))
            {
                for (int i = 0; i < b.RequiredKillQuota && b.IsShieldActive; i++)
                    b.RegisterEnemyKilled();
            }

            if (GUILayout.Button("4. Damage Core (100)"))
                b.TakeDamage(100f, Vector3.zero, Vector3.up);

            if (GUILayout.Button("5. Destroy Core"))
                b.TakeDamage(999999f, Vector3.zero, Vector3.up);
        }
    }
}