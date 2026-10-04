using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    [Header("Speaker Profile")]
    [Tooltip("Assign the character's SpeakerProfile asset.")]
    public SpeakerProfile speakerProfile;

    [Header("Dialogue Variations")]
    [Tooltip("Add multiple text variations. The game will randomly select one when this line plays.")]
    [TextArea(2, 5)]
    public string[] textOptions;

    // Properties drawing directly from the assigned SpeakerProfile
    public string SpeakerName => speakerProfile != null ? speakerProfile.speakerName : string.Empty;
    public string SpeakerSubtitle => speakerProfile != null ? speakerProfile.speakerSubtitle : string.Empty;
    public Sprite SpeakerSprite => speakerProfile != null ? speakerProfile.speakerSprite : null;
    public Color AccentColor => speakerProfile != null ? speakerProfile.GetAccentColor() : Color.white;

    // Helper method to retrieve a random line from the array
    public string GetRandomText()
    {
        if (textOptions == null || textOptions.Length == 0)
        {
            return string.Empty;
        }

        int randomIndex = Random.Range(0, textOptions.Length);
        return textOptions[randomIndex];
    }
}