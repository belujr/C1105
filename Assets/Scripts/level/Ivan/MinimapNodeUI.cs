using UnityEngine;
using UnityEngine.UI;

public class MinimapNodeUI : MonoBehaviour
{
    [Header("UI Elements")]
    public Image backgroundImage;
    public Image shapeImage;
    public Image questionMarkImage;
    public GameObject playerIndicator;

    [Header("Color States")]
    public Color currentColor = new Color(1f, 0.82f, 0.2f, 1f);          // Yellow
    public Color visitedColor = Color.white;                              // White
    public Color unvisitedColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);  // Dark Grey/Black

    public RectTransform RectTransform => (RectTransform)transform;

    public void UpdateVisuals(RoomState state, Sprite shapeSprite)
    {
        switch (state)
        {
            case RoomState.Current:
                gameObject.SetActive(true);
                backgroundImage.color = currentColor;
                shapeImage.gameObject.SetActive(true);
                shapeImage.sprite = shapeSprite;
                shapeImage.color = Color.black;
                questionMarkImage.gameObject.SetActive(false);
                playerIndicator.SetActive(true);
                break;

            case RoomState.Visited:
                gameObject.SetActive(true);
                backgroundImage.color = visitedColor;
                shapeImage.gameObject.SetActive(true);
                shapeImage.sprite = shapeSprite;
                shapeImage.color = Color.black;
                questionMarkImage.gameObject.SetActive(false);
                playerIndicator.SetActive(false);
                break;

            case RoomState.UnvisitedAdjacent:
                gameObject.SetActive(true);
                backgroundImage.color = unvisitedColor;
                shapeImage.gameObject.SetActive(false);
                questionMarkImage.gameObject.SetActive(true);
                playerIndicator.SetActive(false);
                break;

            case RoomState.Hidden:
            default:
                gameObject.SetActive(false);
                break;
        }
    }
}