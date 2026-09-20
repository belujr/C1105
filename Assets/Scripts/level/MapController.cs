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
    private bool isTeleporting = false; // Prevents input mashing during scene loads

    public static MapController Instance;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
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
        if (isAnimating || isTeleporting) return;

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
                    Debug.Log("Teleporting to Hub...");
                    StartCoroutine(SwitchSceneSeamlessly("Scene_Hub"));
                }
            }
            else
            {
                // We are in Scene_Hub. Load Scene_Level specifically to view the map.
                Debug.Log("Loading Level Scene to view map...");
                StartCoroutine(SwitchSceneSeamlessly("Scene_Level"));
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
    }

    private IEnumerator AnimatePathAndLoad(MapNode fromNode, MapNode toNode)
    {
        isAnimating = true;
        currentNode = toNode;

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

    private IEnumerator SwitchSceneSeamlessly(string sceneName)
    {
        isTeleporting = true; // Lock controls

        // Load the new scene in the background to prevent game freezing
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // Wait until Unity has fully prepared the next scene
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        isTeleporting = false; // Unlock controls once arrived
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

            mapBoardRoot.SetActive(true);
            mapCamera.gameObject.SetActive(true);
            if (mainCameraRig != null) mainCameraRig.SetActive(false);
        }
    }

    private void StartSelectedChamber()
    {
        mapBoardRoot.SetActive(false);
        mapCamera.gameObject.SetActive(false);

        if (mainCameraRig != null) mainCameraRig.SetActive(true);
        if (levelSpawner != null) levelSpawner.GenerateLevel();
    }
}