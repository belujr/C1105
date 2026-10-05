using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MiloController : MonoBehaviour
{
    public static MiloController Instance;

    [Header("References")]
    [Tooltip("Assign the visual child GameObject of Milo (e.g., 3D Model). Leave empty to auto-hide SpriteRenderer/SkinnedMeshRenderer.")]
    public GameObject miloVisualObject;
    
    [Tooltip("1. Where Milo spawns after Trench's dialogue finishes")]
    public Transform miloSpawnPoint;
    
    [Header("Pathing & Destinations")]
    [Tooltip("3. Array of waypoints Milo walks through to reach Master Ren")]
    public List<Transform> waypointsToRen = new List<Transform>();
    
    [Tooltip("4. Master Ren's final destination point")]
    public Transform masterRenPoint;

    [Header("The 3 Dialogue Sequences")]
    public DialogueSequence dialogueSpawnToPlayer;
    public DialogueSequence dialoguePlayerToRen;
    public DialogueSequence miloIdleAtRenSequence;

    [Header("Movement Settings (X/Z Plane Only)")]
    public float playerStopDistance = 1.5f;
    public float timeToStayAtPlayer = 1.0f;
    public float timeToReachRen = 5.0f;

    private Transform playerTransform;
    private Animator miloAnimator;
    private float fixedY;

    [HideInInspector] public bool isAtRen = false;
    private int renIdleLineIndex = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else 
        {
            Destroy(gameObject);
            return;
        }

        // Locate Animator
        miloAnimator = GetComponentInChildren<Animator>();

        // Hide visuals without deactivating this script component
        SetMiloVisibility(false);
    }

    private void SetMiloVisibility(bool visible)
    {
        if (miloVisualObject != null && miloVisualObject != gameObject)
        {
            miloVisualObject.SetActive(visible);
        }
        else
        {
            // Toggle child renderers if no separate visual object is assigned
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = visible;
            }
        }
    }

    public void BeginMiloSequence()
    {
        SetMiloVisibility(true);

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }

        if (miloSpawnPoint != null)
        {
            fixedY = miloSpawnPoint.position.y;
            Vector3 spawnPos = miloSpawnPoint.position;
            spawnPos.y = fixedY;
            transform.position = spawnPos;
            transform.rotation = miloSpawnPoint.rotation;
        }
        else
        {
            fixedY = transform.position.y;
        }

        isAtRen = false;
        renIdleLineIndex = 0;

        StartCoroutine(MiloSequenceRoutine());
    }

    private IEnumerator MiloSequenceRoutine()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
            else
            {
                Debug.LogError("[MiloController] Player GameObject with tag 'Player' not found!");
                yield break;
            }
        }

        // SEQUENCE 1: Dialogue while walking to Player
        if (dialogueSpawnToPlayer != null && DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartSequence(dialogueSpawnToPlayer);
        }

        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

        float walkToPlayerSpeed = 3.5f;
        while (playerTransform != null)
        {
            Vector3 playerPosXZ = new Vector3(playerTransform.position.x, fixedY, playerTransform.position.z);
            Vector3 currentPosXZ = new Vector3(transform.position.x, fixedY, transform.position.z);

            if (Vector3.Distance(currentPosXZ, playerPosXZ) <= playerStopDistance) break;

            Vector3 nextPos = Vector3.MoveTowards(currentPosXZ, playerPosXZ, walkToPlayerSpeed * Time.deltaTime);
            nextPos.y = fixedY; 
            transform.position = nextPos;

            RotateTowardsXZ(playerPosXZ);
            yield return null;
        }

        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);

        yield return new WaitForSeconds(timeToStayAtPlayer);

        // SEQUENCE 2: Dialogue while walking to Ren
        if (dialoguePlayerToRen != null && DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartSequence(dialoguePlayerToRen);
        }

        List<Vector3> fullPath = new List<Vector3>();
        foreach (var wp in waypointsToRen)
        {
            if (wp != null) fullPath.Add(new Vector3(wp.position.x, fixedY, wp.position.z));
        }
        if (masterRenPoint != null)
        {
            fullPath.Add(new Vector3(masterRenPoint.position.x, fixedY, masterRenPoint.position.z));
        }

        if (fullPath.Count > 0)
        {
            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

            float totalDistance = 0f;
            Vector3 lastPos = new Vector3(transform.position.x, fixedY, transform.position.z);
            foreach (var point in fullPath)
            {
                totalDistance += Vector3.Distance(lastPos, point);
                lastPos = point;
            }

            float travelSpeed = totalDistance / Mathf.Max(timeToReachRen, 0.1f);

            foreach (var targetPoint in fullPath)
            {
                while (true)
                {
                    Vector3 currentPosXZ = new Vector3(transform.position.x, fixedY, transform.position.z);
                    if (Vector3.Distance(currentPosXZ, targetPoint) < 0.1f) break;

                    Vector3 nextPos = Vector3.MoveTowards(currentPosXZ, targetPoint, travelSpeed * Time.deltaTime);
                    nextPos.y = fixedY;
                    transform.position = nextPos;

                    RotateTowardsXZ(targetPoint);
                    yield return null;
                }
            }

            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);
        }

        isAtRen = true;
    }

    public void HandleMiloInteraction()
    {
        if (!isAtRen) return;

        if (miloIdleAtRenSequence == null || miloIdleAtRenSequence.lines == null || miloIdleAtRenSequence.lines.Count == 0)
        {
            return;
        }

        DialogueLine lineToPlay = miloIdleAtRenSequence.lines[renIdleLineIndex];

        if (DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> { lineToPlay });
        }

        renIdleLineIndex = (renIdleLineIndex + 1) % miloIdleAtRenSequence.lines.Count;
    }

    private void RotateTowardsXZ(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }
}