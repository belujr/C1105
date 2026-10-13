using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Put on TitleMenu (the object with the CanvasGroup). D-pad up/down moves between the buttons,
// the submit action presses the highlighted one by running its existing OnClick.
public class MenuGamepadNav : MonoBehaviour
{
    [Header("Buttons (top to bottom)")]
    [SerializeField] private Button[] buttons;            // Play, Settings, Quit

    [Header("Input")]
    [SerializeField] private InputActionReference navigateAction; // Vector2, e.g. <Gamepad>/dpad
    [SerializeField] private InputActionReference submitAction;   // Button, e.g. <Gamepad>/buttonSouth

    [Header("Colors")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.3f);
    [SerializeField] private Color pressedColor = new Color(1f, 0.5f, 0.2f);

    private CanvasGroup group;
    private int index;

    private void Awake() => group = GetComponent<CanvasGroup>();

    private void OnEnable()
    {
        navigateAction?.action?.Enable();
        submitAction?.action?.Enable();
        index = 0;
        Refresh();
    }

    private void Update()
    {
        if (group != null && !group.interactable) return; // ignore input while fading out

        // stop the EventSystem from also submitting a button the mouse selected (double press)
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
            EventSystem.current.SetSelectedGameObject(null);

        var nav = navigateAction?.action;
        if (nav != null && nav.WasPressedThisFrame())
        {
            float y = nav.ReadValue<Vector2>().y;
            if (y > 0.5f) Move(-1);
            else if (y < -0.5f) Move(1);
        }

        var submit = submitAction?.action;
        if (submit != null && submit.WasPressedThisFrame() && buttons.Length > 0)
        {
            Button b = buttons[index];
            if (b == null || !b.interactable) return;
            b.image.color = pressedColor;
            b.onClick.Invoke();
        }
    }

    private void Move(int dir)
    {
        if (buttons.Length == 0) return;
        index = (index + dir + buttons.Length) % buttons.Length; // wraps around
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null && buttons[i].image != null)
                buttons[i].image.color = i == index ? highlightColor : normalColor;
    }
}