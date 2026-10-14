using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BeaconHealthHUD : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private CanvasGroup beaconGroup;
    [SerializeField] private RectTransform revealMask;
    [SerializeField] private RectTransform revealContent;
    [SerializeField] private Image healthFill;
    [SerializeField] private CanvasGroup healthFillGroup;
    [SerializeField] private CanvasGroup portraitGroup;

    // Add a CanvasGroup to your name-sprite object and assign it here.
    [SerializeField] private CanvasGroup nameSpriteGroup;

    [Header("Scene")]
    [SerializeField] private string levelSceneName = "Scene_Level";

    [Header("Animation")]
    [SerializeField, Min(0.01f)] private float openDuration = 0.45f;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.2f;

    // Cache private-field lookups once. Never write to these fields.
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo CurrentHP =
        typeof(BeaconHealth).GetField("currentCoreHealth", Flags);

    private static readonly FieldInfo MaximumHP =
        typeof(BeaconHealth).GetField("maxCoreHealth", Flags);

    private static readonly FieldInfo Destroyed =
        typeof(BeaconHealth).GetField("isDestroyed", Flags);

    private BeaconHealth beacon;
    private Coroutine watcher;
    private Coroutine animationRoutine;

    private bool ready;
    private bool presented;
    private float fullWidth;
    private float fullHeight;

    private bool InLevel =>
        SceneManager.GetActiveScene().name == levelSceneName;

    private void OnEnable()
    {
        ready = false;
        presented = false;

        if (beaconGroup != null)
            beaconGroup.alpha = 0f;

        SceneManager.activeSceneChanged += OnSceneChanged;
        watcher = StartCoroutine(WatchBeacon());
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;

        if (watcher != null)
            StopCoroutine(watcher);

        StopAnimation();
        Unbind();

        watcher = null;
        ready = false;
        presented = false;

        if (beaconGroup != null)
            beaconGroup.alpha = 0f;
    }

    private IEnumerator WatchBeacon()
    {
        // Wait until scene Awake methods and initial UI setup finish.
        yield return null;

        if (!ValidateReferences())
            yield break;

        Canvas.ForceUpdateCanvases();

        RectTransform cluster =
            beaconGroup.transform as RectTransform;

        fullWidth = cluster.rect.width;
        fullHeight = cluster.rect.height;

        if (fullWidth <= 0f || fullHeight <= 0f)
        {
            Debug.LogWarning(
                "[BeaconHealthHUD] Beacon Group Rect Transform " +
                "must have a positive Width and Height.", this);
            yield break;
        }

        // Keep the artwork full-sized inside a centered clipping mask.
        CenterRect(revealMask);
        CenterRect(revealContent);

        revealContent.sizeDelta =
            new Vector2(fullWidth, fullHeight);

        revealMask.sizeDelta =
            new Vector2(0f, fullHeight);

        ConfigureGroup(beaconGroup);
        ConfigureGroup(healthFillGroup);
        ConfigureGroup(portraitGroup);
        ConfigureGroup(nameSpriteGroup);

        beaconGroup.alpha = 0f;
        ready = true;

        var retryDelay = new WaitForSecondsRealtime(0.5f);

        while (true)
        {
            if (!InLevel)
            {
                HideImmediately();
                Unbind();
            }
            else
            {
                // Search only when the previous beacon is absent.
                if (beacon == null)
                {
                    BeaconHealth found =
                        FindFirstObjectByType<BeaconHealth>();

                    if (found != null)
                        Bind(found);
                }

                // Also handles binding after the shield already dropped.
                bool vulnerable =
                    beacon != null &&
                    beacon.IsActivated &&
                    !beacon.IsShieldActive &&
                    !(bool)Destroyed.GetValue(beacon);

                if (vulnerable && !presented)
                    ShowBeacon();
                else if (!vulnerable && presented)
                    HideBeacon();
            }

            yield return retryDelay;
        }
    }

    private void OnSceneChanged(Scene previous, Scene next)
    {
        // Never leave the beacon visible in the Hub.
        if (!InLevel)
        {
            HideImmediately();
            Unbind();
        }
    }

    private void CenterRect(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition3D = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private void ConfigureGroup(CanvasGroup group)
    {
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void Bind(BeaconHealth found)
    {
        Unbind();
        beacon = found;

        beacon.OnShieldDropped.AddListener(ShowBeacon);
        beacon.OnCoreDamaged.AddListener(UpdateHealth);
        beacon.OnBeaconDestroyed.AddListener(HideBeacon);
    }

    private void Unbind()
    {
        if (beacon != null)
        {
            beacon.OnShieldDropped.RemoveListener(ShowBeacon);
            beacon.OnCoreDamaged.RemoveListener(UpdateHealth);
            beacon.OnBeaconDestroyed.RemoveListener(HideBeacon);
        }

        beacon = null;
    }

    private void UpdateHealth(float current, float maximum)
    {
        if (healthFill == null)
            return;

        healthFill.fillAmount = maximum > 0f
            ? Mathf.Clamp01(current / maximum)
            : 0f;
    }

    private void ShowBeacon()
    {
        if (!ready || !InLevel || beacon == null || presented)
            return;

        if (!beacon.IsActivated || beacon.IsShieldActive ||
            (bool)Destroyed.GetValue(beacon))
            return;

        presented = true;

        // Read actual starting health before showing the bar.
        UpdateHealth(
            (float)CurrentHP.GetValue(beacon),
            (float)MaximumHP.GetValue(beacon));

        StopAnimation();
        animationRoutine = StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        beaconGroup.alpha = 1f;
        SetDetailAlpha(0f);

        revealMask.sizeDelta = new Vector2(0f, fullHeight);

        float elapsed = 0f;

        // Open outward from the center without scaling the artwork.
        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float progress = Mathf.Clamp01(elapsed / openDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);

            revealMask.sizeDelta =
                new Vector2(fullWidth * eased, fullHeight);

            yield return null;
        }

        revealMask.sizeDelta =
            new Vector2(fullWidth, fullHeight);

        // Fade in the health fill, portrait, and name sprite.
        elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetDetailAlpha(Mathf.Clamp01(elapsed / fadeDuration));

            yield return null;
        }

        SetDetailAlpha(1f);
        animationRoutine = null;
    }

    private void SetDetailAlpha(float alpha)
    {
        healthFillGroup.alpha = alpha;
        portraitGroup.alpha = alpha;
        nameSpriteGroup.alpha = alpha;
    }

    private void HideBeacon()
    {
        if (!ready || !presented)
            return;

        presented = false;
        StopAnimation();
        animationRoutine = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float initialAlpha = beaconGroup.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            beaconGroup.alpha = Mathf.Lerp(
                initialAlpha,
                0f,
                Mathf.Clamp01(elapsed / fadeDuration));

            yield return null;
        }

        beaconGroup.alpha = 0f;
        animationRoutine = null;
    }

    private void HideImmediately()
    {
        StopAnimation();
        presented = false;

        if (beaconGroup != null)
            beaconGroup.alpha = 0f;
    }

    private void StopAnimation()
    {
        if (animationRoutine == null)
            return;

        StopCoroutine(animationRoutine);
        animationRoutine = null;
    }

    private bool ValidateReferences()
    {
        bool valid = true;

        valid &= Check(beaconGroup, "Beacon Group");
        valid &= Check(revealMask, "Reveal Mask");
        valid &= Check(revealContent, "Reveal Content");
        valid &= Check(healthFill, "Health Fill");
        valid &= Check(healthFillGroup, "Health Fill Group");
        valid &= Check(portraitGroup, "Portrait Group");
        valid &= Check(nameSpriteGroup, "Name Sprite Group");

        valid &= CheckField(CurrentHP, "currentCoreHealth");
        valid &= CheckField(MaximumHP, "maxCoreHealth");
        valid &= CheckField(Destroyed, "isDestroyed");

        if (revealMask != null &&
            revealMask.GetComponent<RectMask2D>() == null)
        {
            Debug.LogWarning(
                "[BeaconHealthHUD] Reveal Mask is missing " +
                "a Rect Mask 2D component.", this);
            valid = false;
        }

        return valid;
    }

    private bool Check(Object reference, string label)
    {
        if (reference != null)
            return true;

        Debug.LogWarning(
            $"[BeaconHealthHUD] Missing {label} reference.", this);

        return false;
    }

    private bool CheckField(FieldInfo field, string fieldName)
    {
        if (field != null)
            return true;

        Debug.LogWarning(
            $"[BeaconHealthHUD] Private field '{fieldName}' " +
            "was not found in BeaconHealth.", this);

        return false;
    }
}