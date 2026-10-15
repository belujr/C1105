using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class MapController : MonoBehaviour
{
    public Camera mapCamera;
    public GameObject mapBoardRoot;
    public LevelSpawner levelSpawner;
    public GameObject mainCameraRig;

    public MapNode currentNode;
    public List<MapNode> startingNodes;

    [Header("Glow Animation Settings")]
    [ColorUsage(true, true)] public Color glowColor = Color.white;
    public Color normalColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    public float animationDuration = 1.5f;

    private MapNode hoveredNode;
    private Vector3 originalScale;
    private bool isAnimating = false;
    private bool stickMoved = false;

    // Fog belongs to the level, so it is switched off while the map is showing
    private bool levelFogEnabled;
    private bool fogStored;

    // Cinematic map intro (MapIntroAnimator sits on this same GameObject)
    private MapIntroAnimator intro;

    // Post-processing look for the map (MapPostFX sits on this same GameObject)
    private MapPostFX fx;

    // Spotlight + power-up pop for the hovered chamber (MapNodeHighlighter sits on this same GameObject)
    private MapNodeHighlighter highlighter;

    // Chambers the player has finished. They stay greyed out on the map.
    private readonly HashSet<MapNode> completedNodes = new HashSet<MapNode>();
    private readonly List<MapNode> pendingGreyOut = new List<MapNode>();

    public static MapController Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            intro = GetComponent<MapIntroAnimator>();
            fx = GetComponent<MapPostFX>();
            highlighter = GetComponent<MapNodeHighlighter>();
            DontDestroyOnLoad(transform.root.gameObject);
        }
        else
        {
            Destroy(transform.root.gameObject);
        }
    }

    void OnEnable()
    {
        // Only allow the true, surviving MapController to listen for scene changes
        if (Instance == this)
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
    }

    void OnDisable()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }
    }

    void Update()
    {
        // Block input if the map is glowing OR if we are currently loading a scene
        if (isAnimating || SceneTransitionManager.IsTransitioning) return;

        // While the map intro plays, ignore everything except "skip"
        if (intro != null && intro.IsPlaying)
        {
            bool skip = (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                     || (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame
                                                   || Keyboard.current.enterKey.wasPressedThisFrame));
            if (skip)
            {
                intro.Skip();
                if (fx != null) fx.Skip();
            }
            return;
        }

        // Chambers completed while the map was hidden grey out now, in front of the player
        if (pendingGreyOut.Count > 0 && mapBoardRoot.activeInHierarchy)
        {
            foreach (MapNode n in pendingGreyOut) ApplyCompletedLook(n);
            pendingGreyOut.Clear();
        }

        // 1. INPUT: Safely check for Right Trigger OR 'H' Key
        bool togglePressed = false;
        if (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame) togglePressed = true;
        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) togglePressed = true;

        if (togglePressed)
        {
            if (SceneManager.GetActiveScene().name == "Scene_Level")
            {
                // Only allow teleport to Hub if we are currently playing a level (map is hidden)
                if (!mapBoardRoot.activeInHierarchy)
                {
                    SceneTransitionManager.LoadScene("Scene_Hub");
                }
            }
            else
            {
                // We are in Scene_Hub. Load Scene_Level specifically to view the map.
                SceneTransitionManager.LoadScene("Scene_Level");
            }
            return;
        }

        // 2. BLOCKER: If the map isn't visible, don't run map navigation
        if (!mapBoardRoot.activeInHierarchy) return;

        // 3. DETERMINE VALID PATHS
        List<MapNode> validNodes = (currentNode == null) ? startingNodes : currentNode.nextNodes;
        if (validNodes.Count == 0) return;

        if (hoveredNode == null || !validNodes.Contains(hoveredNode))
        {
            SetHover(validNodes[0]);
        }

        // 4. GAMEPAD NAVIGATION
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.leftStick.ReadValue();

            if (stick.x > 0.5f && !stickMoved)
            {
                CycleNode(1, validNodes);
                stickMoved = true;
            }
            else if (stick.x < -0.5f && !stickMoved)
            {
                CycleNode(-1, validNodes);
                stickMoved = true;
            }
            else if (Mathf.Abs(stick.x) < 0.2f)
            {
                stickMoved = false;
            }

            // Confirm Selection (South Button: A on Xbox, Cross on PS)
            if (Gamepad.current.buttonSouth.wasPressedThisFrame && hoveredNode != null)
            {
                StartCoroutine(AnimatePathAndLoad(currentNode, hoveredNode));
            }
        }
    }

    // ------------------------------------------------------------------ completed chambers

    public bool IsCompleted(MapNode node)
    {
        return node != null && completedNodes.Contains(node);
    }

    /// <summary>
    /// Call this when the chamber the player entered is finished, for example from your level-complete code:
    ///     MapController.Instance.CompleteCurrentChamber();
    /// The chamber greys out the next time the map is shown.
    /// </summary>
    public void CompleteCurrentChamber()
    {
        MarkCompleted(currentNode);
    }

    public void MarkCompleted(MapNode node)
    {
        if (node == null || !completedNodes.Add(node)) return;
        pendingGreyOut.Add(node);
    }

    private void ApplyCompletedLook(MapNode node)
    {
        if (node == null) return;

        MapNodeCompletion look = node.GetComponent<MapNodeCompletion>();
        if (look == null) look = node.gameObject.AddComponent<MapNodeCompletion>();
        look.SetCompleted(true, true);
    }

    // Right-click the component title (the three dots) while playing to try it on the hovered chamber
    [ContextMenu("Test: complete hovered chamber")]
    private void TestCompleteHovered()
    {
        MarkCompleted(hoveredNode);
    }

    private void CycleNode(int direction, List<MapNode> validNodes)
    {
        if (validNodes.Count <= 1) return;

        int currentIndex = validNodes.IndexOf(hoveredNode);
        if (currentIndex == -1) currentIndex = 0;

        currentIndex = (currentIndex + direction + validNodes.Count) % validNodes.Count;
        SetHover(validNodes[currentIndex]);
    }

    private void SetHover(MapNode newNode)
    {
        if (hoveredNode != null)
        {
            hoveredNode.transform.localScale = originalScale;
        }

        hoveredNode = newNode;
        originalScale = hoveredNode.transform.localScale;
        hoveredNode.transform.localScale = originalScale * 1.5f;

        if (highlighter != null) highlighter.OnHover(hoveredNode);
    }

    // Puts the hovered icon back to normal size so the intro animation starts from clean values
    private void ResetHover()
    {
        if (hoveredNode != null) hoveredNode.transform.localScale = originalScale;
        hoveredNode = null;
    }

    private IEnumerator AnimatePathAndLoad(MapNode fromNode, MapNode toNode)
    {
        isAnimating = true;
        currentNode = toNode;

        if (highlighter != null) highlighter.OnSelect(toNode);

        if (fromNode != null && fromNode.pathLines.ContainsKey(toNode))
        {
            LineRenderer lr = fromNode.pathLines[toNode];
            float elapsed = 0f;

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / animationDuration;

                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(glowColor, 0.0f),
                        new GradientColorKey(glowColor, percent),
                        new GradientColorKey(normalColor, Mathf.Clamp01(percent + 0.01f)),
                        new GradientColorKey(normalColor, 1.0f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 1f)
                    }
                );

                lr.colorGradient = gradient;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(0.5f);
        }

        StartSelectedChamber();
        isAnimating = false;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When Scene_Level loads, ONLY ensure the map is ready. Never auto-spawn the level.
        if (scene.name == "Scene_Level")
        {
            // Find hidden objects
            levelSpawner = FindObjectOfType<LevelSpawner>(true);
            IsoCameraRig rig = FindObjectOfType<IsoCameraRig>(true);
            if (rig != null) mainCameraRig = rig.gameObject;

            HideFogForMap();
            ResetHover();

            mapBoardRoot.SetActive(true);
            mapCamera.gameObject.SetActive(true);
            if (mainCameraRig != null) mainCameraRig.SetActive(false);

            // Cinematic intro: forest rises, icons pop in, paths draw
            if (intro != null) intro.Play();
            if (fx != null) fx.Show();
            if (highlighter != null) highlighter.Show();
        }
    }

    private void HideFogForMap()
    {
        levelFogEnabled = RenderSettings.fog;   // remember what the level wants
        fogStored = true;
        RenderSettings.fog = false;
    }

    private void RestoreFogForLevel()
    {
        if (fogStored) RenderSettings.fog = levelFogEnabled;
    }

    private void StartSelectedChamber()
    {
        // Clean transition: fade out -> swap map for level while the screen is black -> fade in
        bool started = SceneTransitionManager.RunTransition(EnterLevel);
        if (!started) EnterLevel(); // fallback, should not normally happen
    }

    private void EnterLevel()
    {
        // Make sure the intro can never be left half-finished when the map is switched off
        if (intro != null) intro.Skip();
        if (fx != null) fx.Hide();
        if (highlighter != null) highlighter.Hide();

        RestoreFogForLevel();

        mapBoardRoot.SetActive(false);
        mapCamera.gameObject.SetActive(false);

        if (mainCameraRig != null) mainCameraRig.SetActive(true);
        if (levelSpawner != null) levelSpawner.GenerateLevel();
    }
}