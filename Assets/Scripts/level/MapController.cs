using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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

    [Tooltip("If this is 0, the level will load instantly. Keep it above 1.0.")]
    public float animationDuration = 1.5f;

    private MapNode hoveredNode;
    private Vector3 originalScale;
    private bool isAnimating = false;

    void Update()
    {
        if (Mouse.current == null || isAnimating) return;

        Ray ray = mapCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            MapNode hitNode = hit.collider.GetComponent<MapNode>();
            bool isValidTarget = false;

            if (currentNode == null)
                isValidTarget = (hitNode != null && startingNodes.Contains(hitNode));
            else
                isValidTarget = (hitNode != null && currentNode.nextNodes.Contains(hitNode));

            if (isValidTarget)
            {
                if (hoveredNode != hitNode)
                {
                    ResetHover();
                    hoveredNode = hitNode;
                    originalScale = hoveredNode.transform.localScale;
                    hoveredNode.transform.localScale = originalScale * 1.5f;
                }

                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    StartCoroutine(AnimatePathAndLoad(currentNode, hitNode));
                }
            }
            else
            {
                ResetHover();
            }
        }
        else
        {
            ResetHover();
        }
    }

    private void ResetHover()
    {
        if (hoveredNode != null)
        {
            hoveredNode.transform.localScale = originalScale;
            hoveredNode = null;
        }
    }

    private IEnumerator AnimatePathAndLoad(MapNode fromNode, MapNode toNode)
    {
        isAnimating = true;
        currentNode = toNode;
        ResetHover();

        // Only animate if traveling BETWEEN chambers (not picking the very first one)
        if (fromNode != null && fromNode.pathLines.ContainsKey(toNode))
        {
            Debug.Log("Starting Glow Animation...");
            LineRenderer lr = fromNode.pathLines[toNode];
            float elapsed = 0f;

            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / animationDuration;

                // Create a sharp, traveling gradient
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(glowColor, 0.0f),
                        new GradientColorKey(glowColor, percent), // Glow up to the current percent
                        new GradientColorKey(normalColor, Mathf.Clamp01(percent + 0.01f)), // Instant drop-off to normal color
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
            Debug.Log("Glow Animation Finished.");
        }
        else
        {
            // If picking the first starting node, just wait briefly
            yield return new WaitForSeconds(0.5f);
        }

        StartSelectedChamber();
        isAnimating = false;
    }

    private void StartSelectedChamber()
    {
        mapBoardRoot.SetActive(false);
        mapCamera.gameObject.SetActive(false);
        mainCameraRig.SetActive(true);
        levelSpawner.GenerateLevel();
    }
}