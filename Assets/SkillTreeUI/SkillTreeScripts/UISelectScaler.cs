using UnityEngine;
using UnityEngine.EventSystems;

public class UISelectScaler : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    public float scaleMultiplier = 1.15f;

    public void OnSelect(BaseEventData eventData)
    {
        transform.localScale = Vector3.one * scaleMultiplier;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        transform.localScale = Vector3.one; //back 2 normal size
    }
}