using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(Button))]
public class SkillTreeNode : MonoBehaviour, ISelectHandler, IDeselectHandler, IEventSystemHandler
{
    [Header("Combo Binding")]
    public int targetComboSlot = 1;

    [Header("Attack Settings")]
    public AttackData attackData;
    public SkillTreeNode parentNode;

    [Header("Explicit D-Pad Navigation Links")]
    public Selectable navUp;
    public Selectable navDown;
    public Selectable navLeft;
    public Selectable navRight;

    [Header("Unlock Input & UI")]
    [Tooltip("Drag your 'Hold to Unlock' input action reference (e.g. Gamepad South / Button South) here.")]
    public InputActionReference unlockActionRef;
    [Tooltip("Popup UI element displaying 'Hold A to Unlock'.")]
    public GameObject holdPromptUI;

    [Header("Glow & Flash Settings")]
    [Tooltip("How long (seconds) the player must hold the unlock button to buy this node.")]
    [Min(0.05f)] public float requiredHoldDuration = 2f;

    [Tooltip("Colour of the pulsing glow on a node you can afford and unlock right now (it pulses between this and white).")]
    public Color availableGlowColor = new Color(1f, 1f, 0.4f, 1f);
    [Tooltip("How fast the available-node glow pulses.")]
    [Min(0f)] public float availablePulseSpeed = 8f;

    [Tooltip("Colour of the flashing overlay while the unlock button is being held. Its alpha scales the flash strength.")]
    public Color holdFlashColor = Color.white;

    [Tooltip("Colour of the burst when the node finishes unlocking.")]
    public Color successGlowColor = Color.white;
    [Tooltip("How long (seconds) the unlock burst stays visible.")]
    [Min(0f)] public float successGlowDuration = 0.5f;

    [Header("State")]
    [Tooltip("Check this only if this node should start unlocked by default. Unchecked nodes will require purchase or game start allocation.")]
    public bool isUnlocked = false;

    private Button button;
    private Image buttonImage;
    private Image flashOverlayImage;
    private float holdTimer = 0f;
    private string saveKey;

    private void Awake()
    {
        button = GetComponent<Button>();
        buttonImage = GetComponent<Image>();
        button.onClick.AddListener(OnNodeClicked);

        // Hardcode/auto-generate the flash overlay child image if it doesn't already exist
        Transform existingFlash = transform.Find("HardcodedFlashOverlay");
        if (existingFlash != null)
        {
            flashOverlayImage = existingFlash.GetComponent<Image>();
        }
        else
        {
            GameObject flashObj = new GameObject("HardcodedFlashOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            flashObj.transform.SetParent(transform, false);
            RectTransform rt = flashObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            flashOverlayImage = flashObj.GetComponent<Image>();
            flashOverlayImage.color = new Color(1f, 1f, 1f, 0f);
            flashOverlayImage.raycastTarget = false;
        }

        if (attackData != null)
        {
            saveKey = SaveKeyFor(attackData);
            // CHANGED: Removed "parentNode == null ? 1 : 0". Now it defaults strictly to 0 (locked) unless saved or explicitly set.
            isUnlocked = PlayerPrefs.GetInt(saveKey, isUnlocked ? 1 : 0) == 1;
        }

        if (holdPromptUI != null) holdPromptUI.SetActive(false);
        if (flashOverlayImage != null) flashOverlayImage.gameObject.SetActive(false);
    }

    private void Start()
    {
        SetupExplicitNavigation();
        UpdateVisuals();
    }

    private void OnEnable()
    {
        if (unlockActionRef != null && unlockActionRef.action != null)
            unlockActionRef.action.Enable();
    }

    private void OnDisable()
    {
        if (unlockActionRef != null && unlockActionRef.action != null)
            unlockActionRef.action.Disable();

        if (holdPromptUI != null) holdPromptUI.SetActive(false);
    }

    public void SetupExplicitNavigation()
    {
        Navigation nav = new Navigation();
        nav.mode = Navigation.Mode.Explicit;

        nav.selectOnUp = navUp;
        nav.selectOnDown = navDown;
        nav.selectOnLeft = navLeft;
        nav.selectOnRight = navRight;

        button.navigation = nav;
    }

    private static string SaveKeyFor(AttackData data) => "NodeUnlocked_" + data.name;

    /// <summary>
    /// Editor/dev helper (called by ComboUIManager): forgets this node's saved unlock and
    /// puts it back to its starting state.
    /// </summary>
    public void ResetUnlockSaveData()
    {
        if (attackData != null) PlayerPrefs.DeleteKey(SaveKeyFor(attackData));
        isUnlocked = false; // Reset safely to locked by default
        holdTimer = 0f;
    }

    // True only if EVERY node above this one in the chain is unlocked, not just the direct parent.
    private bool AncestorsUnlocked()
    {
        SkillTreeNode p = parentNode;
        int guard = 0;
        while (p != null && guard++ < 64)
        {
            if (!p.isUnlocked) return false;
            p = p.parentNode;
        }
        return true;
    }

    public bool IsUnlockedEffective => isUnlocked && AncestorsUnlocked();

    public bool CanBeUnlocked()
    {
        return AncestorsUnlocked();
    }

    public bool IsAvailableToUnlock => !IsUnlockedEffective && CanBeUnlocked() && HasEnoughSouls();

    public bool HasEnoughSouls()
    {
        if (attackData == null) return true;
        return SoulManager.Instance != null && SoulManager.Instance.CanAfford(attackData.soulCost);
    }

    private void Update()
    {
        UpdateVisuals();

        if (IsUnlockedEffective) return;

        bool isSelected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        bool canUnlockNow = IsAvailableToUnlock;

        if (isSelected && canUnlockNow)
        {
            if (holdPromptUI != null && !holdPromptUI.activeSelf)
                holdPromptUI.SetActive(true);

            bool isHolding = (unlockActionRef != null && unlockActionRef.action != null && unlockActionRef.action.IsPressed()) ||
                             (Gamepad.current != null && Gamepad.current.buttonSouth.isPressed) ||
                             (Keyboard.current != null && (Keyboard.current.spaceKey.isPressed || Keyboard.current.enterKey.isPressed));

            if (isHolding)
            {
                holdTimer += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(holdTimer / requiredHoldDuration);

                UpdateFlashProgress(progress);

                if (holdTimer >= requiredHoldDuration)
                {
                    UnlockAttack();
                }
            }
            else
            {
                holdTimer = Mathf.Max(0f, holdTimer - (Time.unscaledDeltaTime * 3f));
                UpdateFlashProgress(holdTimer / requiredHoldDuration);
            }
        }
        else
        {
            if (holdPromptUI != null && holdPromptUI.activeSelf)
                holdPromptUI.SetActive(false);
            holdTimer = 0f;
            if (flashOverlayImage != null && flashOverlayImage.gameObject.activeSelf)
                flashOverlayImage.gameObject.SetActive(false);
        }
    }

    private void UpdateFlashProgress(float progress)
    {
        if (flashOverlayImage == null) return;

        if (progress > 0f)
        {
            if (!flashOverlayImage.gameObject.activeSelf) flashOverlayImage.gameObject.SetActive(true);

            float alpha = Mathf.Clamp01(Mathf.Sin(progress * Mathf.PI * 5f + Time.unscaledTime * 15f) * 0.5f + 0.5f);
            float intensity = Mathf.Lerp(0.6f, 1.0f, progress);
            flashOverlayImage.color = new Color(holdFlashColor.r, holdFlashColor.g, holdFlashColor.b, alpha * intensity * holdFlashColor.a);
        }
        else
        {
            if (flashOverlayImage.gameObject.activeSelf) flashOverlayImage.gameObject.SetActive(false);
        }
    }

    private void UnlockAttack()
    {
        if (attackData == null) return;
        if (!CanBeUnlocked()) return;

        if (SoulManager.Instance != null)
        {
            if (!SoulManager.Instance.SpendSouls(attackData.soulCost)) return;
        }

        isUnlocked = true;
        PlayerPrefs.SetInt(saveKey, 1);
        PlayerPrefs.Save();

        holdTimer = 0f;
        if (holdPromptUI != null) holdPromptUI.SetActive(false);

        StartCoroutine(SuccessGlowRoutine());

        if (ComboUIManager.Instance != null)
        {
            ComboUIManager.Instance.RefreshAllNodes();
        }
    }

    private IEnumerator SuccessGlowRoutine()
    {
        if (flashOverlayImage != null)
        {
            flashOverlayImage.gameObject.SetActive(true);
            flashOverlayImage.color = successGlowColor;
        }

        yield return new WaitForSecondsRealtime(successGlowDuration);

        if (flashOverlayImage != null)
        {
            flashOverlayImage.gameObject.SetActive(false);
        }
    }

    public void OnNodeClicked()
    {
        if (IsUnlockedEffective && attackData != null && ComboUIManager.Instance != null)
        {
            ComboUIManager.Instance.TryEquipToSpecificSlot(attackData, targetComboSlot - 1);
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (attackData != null && SkillPreviewManager.Instance != null)
        {
            SkillPreviewManager.Instance.ShowPreview(attackData);
        }
    }

    public void OnDeselect(BaseEventData eventData)
    {
        holdTimer = 0f;
        if (holdPromptUI != null) holdPromptUI.SetActive(false);
        if (flashOverlayImage != null) flashOverlayImage.gameObject.SetActive(false);
    }

    public void UpdateVisuals()
    {
        if (buttonImage == null) return;

        if (attackData != null && attackData.attackIcon != null)
        {
            buttonImage.sprite = attackData.attackIcon;
            buttonImage.enabled = true;
        }

        if (IsUnlockedEffective)
        {
            buttonImage.color = Color.white;
            button.interactable = true;
        }
        else if (IsAvailableToUnlock)
        {
            float pulse = (Mathf.Sin(Time.unscaledTime * availablePulseSpeed) + 1f) * 0.5f;
            buttonImage.color = Color.Lerp(availableGlowColor, Color.white, pulse);
            button.interactable = true;
        }
        else
        {
            Color c = buttonImage.color;
            c.a = 0.35f;
            buttonImage.color = c;
            button.interactable = false;
        }
    }

    public void AnimateLineToParent()
    {
        UILineConnector connector = GetComponent<UILineConnector>();
        if (connector != null)
        {
            connector.AnimateLine();
        }
    }
}