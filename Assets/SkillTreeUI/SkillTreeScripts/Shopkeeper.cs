using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shopkeeper : MonoBehaviour
{
    [Header("Narrative Unlock Requirement")]
    [Tooltip("Assign Bhide's dialogue sequence asset here. The shop will stay locked until this sequence is played.")]
    public DialogueSequence requiredDialogueToUnlock;

    [Header("Exit & Teleport Settings")]
    [Tooltip("Assign a GameObject/Transform outside the trigger zone where the player appears after exiting the shop.")]
    public Transform exitTeleportPoint;

    [Header("SkillTree Fade Settings")]
    [Tooltip("CanvasGroup on your SkillTree panel. If left empty, it is taken from ComboUIManager's skillTreePanel (and added there if missing).")]
    public CanvasGroup skillTreeCanvasGroup;
    [Tooltip("How long the skill tree takes to fade in / fade out (seconds). Higher = slower and softer.")]
    public float fadeDuration = 0.8f;

    [Header("Input Data References")]
    [Tooltip("Drag your 'Back/Cancel' action from your PlayerControls asset here.")]
    public InputActionReference backActionRef;

    /// <summary>
    /// Raised every time the player has fully left the shop (fade finished, menu closed, player back outside).
    /// The tutorial listens to it. Nothing needs to subscribe for the shop to work.
    /// </summary>
    public static event System.Action ShopClosed;

    private bool isShopOpen = false;
    private bool isFading = false;

    // Caps how much a single slow frame can advance the fade, so a hitch can't make it jump.
    private const float MaxFadeStep = 1f / 30f;

    private void OnEnable()
    {
        EnableBackAction();
    }

    private void OnDisable()
    {
        // Never leave ComboUIManager unable to close itself
        if (ComboUIManager.Instance != null)
            ComboUIManager.Instance.suppressBackClose = false;

        if (backActionRef != null && backActionRef.action != null)
            backActionRef.action.Disable();
    }

    private void EnableBackAction()
    {
        if (backActionRef != null && backActionRef.action != null)
            backActionRef.action.Enable();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !isShopOpen && !isFading)
        {
            if (IsUnlocked())
            {
                StartCoroutine(EnterShopRoutine(other.gameObject));
            }
            else
            {
                Debug.Log($"Shopkeeper is locked. Complete dialogue sequence '{requiredDialogueToUnlock.name}' first.");
            }
        }
    }

    private void Update()
    {
        bool backPressed = backActionRef != null &&
                           backActionRef.action != null &&
                           backActionRef.action.WasPressedThisFrame();

        if (backPressed && isShopOpen && !isFading)
        {
            StartCoroutine(ExitShopRoutine());
        }
    }

    private IEnumerator EnterShopRoutine(GameObject playerObj)
    {
        isFading = true;

        PlayerInput playerInput = playerObj.GetComponent<PlayerInput>();
        if (playerInput != null)
        {
            playerInput.DeactivateInput();
            // DeactivateInput() can also disable the Back action if it lives in the same
            // asset; re-enable it so the shop can still be closed.
            EnableBackAction();
        }

        // Hold the panel at alpha 0 BEFORE the menu opens so there is no flash
        CanvasGroup group = GetSkillTreeGroup();
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
        }

        if (ComboUIManager.Instance != null)
        {
            // Shopkeeper now owns closing, so ComboUIManager doesn't hide the panel
            // instantly on Back and the fade-out can actually be seen.
            ComboUIManager.Instance.suppressBackClose = true;
            ComboUIManager.Instance.OpenMenu();
        }

        if (group != null)
        {
            // Let OpenMenu's UI setup / layout land while the panel is still invisible,
            // so that hitch doesn't happen in the middle of the fade.
            yield return null;

            yield return FadeRoutine(group, 0f, 1f);
            group.interactable = true;
        }

        isShopOpen = true;
        isFading = false;
    }

    private IEnumerator ExitShopRoutine()
    {
        isFading = true;

        // Fade out while the panel is still fully open
        CanvasGroup group = GetSkillTreeGroup();
        if (group != null)
        {
            group.interactable = false;
            yield return FadeRoutine(group, 1f, 0f);
        }

        if (ComboUIManager.Instance != null)
        {
            ComboUIManager.Instance.suppressBackClose = false;
            ComboUIManager.Instance.CloseMenu();
        }

        // Panel is closed now: put alpha back so any other way of opening the
        // menu still shows it normally.
        if (group != null)
        {
            group.alpha = 1f;
            group.interactable = true;
        }

        isShopOpen = false;

        // Teleport the player to the designated exit point outside the trigger zone
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            if (exitTeleportPoint != null)
            {
                CharacterController cc = playerObj.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                playerObj.transform.position = exitTeleportPoint.position;
                playerObj.transform.rotation = exitTeleportPoint.rotation;

                if (cc != null) cc.enabled = true;
            }

            PlayerInput playerInput = playerObj.GetComponent<PlayerInput>();
            if (playerInput != null) playerInput.ActivateInput();
        }

        isFading = false;
        ShopClosed?.Invoke();
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float from, float to)
    {
        float duration = Mathf.Max(0.01f, fadeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Unscaled time (works while paused), with a capped step so a slow frame can't skip ahead
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);
            float t = Mathf.Clamp01(elapsed / duration);

            // Smootherstep: very soft start AND end, no visible "kick"
            float eased = t * t * t * (t * (t * 6f - 15f) + 10f);

            group.alpha = Mathf.Lerp(from, to, eased);
            yield return null;
        }

        group.alpha = to;
    }

    private CanvasGroup GetSkillTreeGroup()
    {
        if (skillTreeCanvasGroup != null) return skillTreeCanvasGroup;

        if (ComboUIManager.Instance != null && ComboUIManager.Instance.skillTreePanel != null)
        {
            GameObject panel = ComboUIManager.Instance.skillTreePanel;
            skillTreeCanvasGroup = panel.GetComponent<CanvasGroup>();
            if (skillTreeCanvasGroup == null)
                skillTreeCanvasGroup = panel.AddComponent<CanvasGroup>();
        }

        return skillTreeCanvasGroup;
    }

    public bool IsUnlocked()
    {
        if (requiredDialogueToUnlock == null) return true;
        return HubNarrativeManager.Instance != null && HubNarrativeManager.Instance.IsSequenceCompleted(requiredDialogueToUnlock);
    }
}