using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    public string speakerName;
    public string speakerSubtitle; // Role or designation (e.g., "The First Caretaker")[cite: 20]
    public Sprite speakerSprite;   // 2D Sprite portrait for this character line
    [TextArea(2, 5)] public string text;
}