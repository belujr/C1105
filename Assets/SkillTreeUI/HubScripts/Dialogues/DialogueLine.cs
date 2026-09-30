using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    public string speakerName;
    public string speakerSubtitle; // Role or designation (e.g., "The First Caretaker")[cite: 2]
    public Sprite speakerSprite;   // 2D Sprite portrait for this character line[cite: 2]

    [Header("Dialogue Variations")]
    [Tooltip("Add multiple text variations. The game will randomly select one when this line plays.")]
    [TextArea(2, 5)] 
    public string[] textOptions;

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