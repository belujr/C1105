using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BeaconHealthHUD : MonoBehaviour
{
    [Header("UI References (required)")]
    [SerializeField] private CanvasGroup beaconGroup;
    [SerializeField] private RectTransform revealMask;
    [SerializeField] private RectTransform revealContent;
    [SerializeField] private Image healthFill;

    [Header("Fade Groups (optional)")]
    [SerializeField] private CanvasGroup healthFillGroup;
    [SerializeField] private CanvasGroup portraitGroup;
    [SerializeField] private CanvasGroup nameSpriteGroup;

    [Header("Scene")]
    [Tooltip("The bar only shows while a scene with this name is loaded " +
             "(active OR additive). Leave empty to allow any scene.")]
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
    private bool warnedScene;
    private float fullWidth;
    private float fullHeight;

    // FIX: the old check only looked at the ACTIVE scene, so an
    // additively loaded level (or a different scene name) kept the
    // bar hidden forever.
    private bool InLevel
    {
        get
        {
            if (string.IsNullOrEmpty(levelSceneName))
                return true;

            Scene level = SceneManager.GetSceneByName(levelSceneName);
            return level.IsValid() && level.isLoaded;
        }
    }

    private void OnEnable()
    {
        ready = false;
        presented = false;
        warnedScene = false;

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

        ConfigureFill();
        ConfigureGroup(beaconGroup);
        ConfigureGroup(healthFillGroup);
        ConfigureGroup(portraitGroup);
        ConfigureGroup(nameSpriteGroup);

        var retryDelay = new WaitForSecondsRealtime(0.25f);

        // FIX: the old version gave up for good if the layout had no
        // size on the first frame. Keep retrying until it does.
        bool warnedSize = false;

        while (!MeasureCluster())
        {
            if (!warnedSize)
            {
                Debug.LogWarning(
                    "[BeaconHealthHUD] Beacon Group has zero Width or " +
                    "Height. Waiting for the layout to get a size.", this);
                warnedSize = true;
            }

            yield return retryDelay;
        }

        beaconGroup.alpha = 0f;
        ready = true;

        while (true)
        {
            RefreshBeacon();
            bool vulnerable = IsVulnerable(beacon);

            if (!InLevel)
            {
                if (vulnerable && !warnedScene)
                {
                    Debug.LogWarning(
                        "[BeaconHealthHUD] The shield is down, but the bar " +
                        $"stays hidden because no scene named '{levelSceneName}' " +
                        "is loaded (active scene: '" +
                        SceneManager.GetActiveScene().name + "'). " +
                        "Fix 'Level Scene Name' or clear it.", this);
                    warnedScene = true;
                }

                HideImmediately();
            }
            else if (vulnerable && !presented)
            {
                ShowBeacon();
            }
            else if (!vulnerable && presented)
            {
                HideBeacon();
            }

            yield return retryDelay;
        }
    }

    private bool MeasureCluster()
    {
        Canvas.ForceUpdateCanvases();

        RectTransform cluster = beaconGroup.transform as RectTransform;
        if (cluster == null)
            return false;

        fullWidth = cluster.rect.width;
        fullHeight = cluster.rect.height;

        if (fullWidth <= 0f || fullHeight <= 0f)
            return false;

        // Keep the artwork full-sized inside a centered clipping mask.
        CenterRect(revealMask);
        CenterRect(revealContent);

        revealContent.sizeDelta = new Vector2(fullWidth, fullHeight);
        revealMask.sizeDelta = new Vector2(0f, fullHeight);
        return true;
    }

    // FIX: the old version bound to the FIRST BeaconHealth it found and
    // never looked again. With more than one beacon in the level, it
    // could watch the wrong one while a different shield dropped.
    private void RefreshBeacon()
    {
        if (IsVulnerable(beacon))
            return;

        BeaconHealth best = beacon;
        BeaconHealth[] all =
            FindObjectsByType<BeaconHealth>(FindObjectsSortMode.None);

        foreach (BeaconHealth candidate in all)
        {
            if (Score(candidate) > Score(best))
                best = candidate;
        }

        if (best != null && best != beacon)
            Bind(best);
    }

    // Vulnerable beats activated, activated beats idle, idle beats destroyed.
    private int Score(BeaconHealth b)
    {
        if (b == null) return -1;
        if (IsDestroyed(b)) return 0;
        if (IsVulnerable(b)) return 3;
        if (b.IsActivated) return 2;
        return 1;
    }

    private bool IsDestroyed(BeaconHealth b)
    {
        return b != null && (bool)Destroyed.GetValue(b);
    }

    private bool IsVulnerable(BeaconHealth b)
    {
        return b != null &&
               b.IsActivated &&
               !b.IsShieldActive &&
               !IsDestroyed(b);
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
        if (group == null)
            return;

        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void ConfigureFill()
    {
        // fillAmount only crops a Filled image that has a Source Image.
        if (healthFill.type != Image.Type.Filled)
        {
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        }

        if (healthFill.sprite == null)
        {
            Debug.LogWarning(
                "[BeaconHealthHUD] Health Fill has no Source Image, so it " +
                "will not shrink when the beacon takes damage.", healthFill);
        }
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
        if (!ready || !InLevel || presented || !IsVulnerable(beacon))
            return;

        // FIX: alpha does nothing on a disabled object. The script hides
        // the bar with alpha, so the object itself must stay active.
        if (!beaconGroup.gameObject.activeSelf)
            beaconGroup.gameObject.SetActive(true);

        if (!beaconGroup.gameObject.activeInHierarchy)
        {
            Debug.LogWarning(
                "[BeaconHealthHUD] A parent of Beacon Group is inactive, " +
                "so the bar cannot be seen.", beaconGroup);
        }

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

        revealMask.sizeDelta = new Vector2(fullWidth, fullHeight);

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
        if (healthFillGroup != null) healthFillGroup.alpha = alpha;
        if (portraitGroup != null) portraitGroup.alpha = alpha;
        if (nameSpriteGroup != null) nameSpriteGroup.alpha = alpha;
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

        // FIX: the three fade groups are now optional. Before, one empty
        // slot (like the newly added Name Sprite Group) disabled the
        // whole HUD with only a warning in the Console.
        valid &= Check(beaconGroup, "Beacon Group");
        valid &= Check(revealMask, "Reveal Mask");
        valid &= Check(revealContent, "Reveal Content");
        valid &= Check(healthFill, "Health Fill");

        valid &= CheckField(CurrentHP, "currentCoreHealth");
        valid &= CheckField(MaximumHP, "maxCoreHealth");
        valid &= CheckField(Destroyed, "isDestroyed");

        if (!valid)
            return false;

        if (revealMask == beaconGroup.transform)
        {
            Debug.LogWarning(
                "[BeaconHealthHUD] Reveal Mask must be a CHILD of Beacon " +
                "Group, not the same object.", this);
            return false;
        }

        // FIX: add the missing mask instead of disabling the HUD.
        if (revealMask.GetComponent<RectMask2D>() == null)
        {
            revealMask.gameObject.AddComponent<RectMask2D>();
            Debug.LogWarning(
                "[BeaconHealthHUD] Reveal Mask had no Rect Mask 2D, so " +
                "one was added at runtime. Add it in the prefab too.",
                revealMask);
        }

        return true;
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