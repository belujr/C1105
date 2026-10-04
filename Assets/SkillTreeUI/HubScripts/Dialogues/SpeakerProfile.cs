using UnityEngine;

[CreateAssetMenu(fileName = "NewSpeakerProfile", menuName = "Narrative/Speaker Profile")]
public class SpeakerProfile : ScriptableObject
{
    public string speakerName;
    public string speakerSubtitle; // Designation/Role (e.g., "The First Caretaker")
    public Sprite speakerSprite;   // Portrait

    [Header("Title Background Accent Color")]
    [Tooltip("Click the swatch to open the color picker panel.")]
    public Color titleAccentColor = Color.white;

    [Header("Text Color Overrides (Optional)")]
    public bool overrideTextColor = false;
    public Color nameTextColor = Color.white;
    public Color subtitleTextColor = new Color(0.9f, 0.9f, 0.9f, 1f);

    /// <summary>
    /// Returns the accent color selected in the color picker window.
    /// </summary>
    public Color GetAccentColor()
    {
        return titleAccentColor;
    }
}