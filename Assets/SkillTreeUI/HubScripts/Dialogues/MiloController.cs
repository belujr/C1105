using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MiloController : MonoBehaviour
{
    public static MiloController Instance;

    [Header("References")]
    [Tooltip("Leave empty if this script is attached directly to Milo's GameObject")]
    public GameObject miloVisualObject;
    
    [Tooltip("1. Where Milo spawns after Trench's dialogue finishes")]
    public Transform miloSpawnPoint;
    
    [Header("Pathing & Destinations")]
    [Tooltip("3. Array of waypoints Milo walks through to reach Master Ren")]
    public List<Transform> waypointsToRen = new List<Transform>();
    
    [Tooltip("4. Master Ren's final destination point")]
    public Transform masterRenPoint;

    [Header("The 3 Dialogue Sequences")]
    [Tooltip("Sequence 1: Played while Milo spawns and walks to the player (and waits)")]
    public DialogueSequence dialogueSpawnToPlayer;

    [Tooltip("Sequence 2: Played while Milo walks from the player to Master Ren")]
    public DialogueSequence dialoguePlayerToRen;

    [Tooltip("Sequence 3: Played line-by-line when player clicks Milo after he reaches Ren")]
    public DialogueSequence miloIdleAtRenSequence;

    [Header("Movement Settings (X/Z Plane Only)")]
    [Tooltip("How close Milo gets to the player before stopping")]
    public float playerStopDistance = 1.5f;
    
    [Tooltip("Time in seconds Milo stays by the player after reaching him before walking to Ren")]
    public float timeToStayAtPlayer = 1.0f;
    
    [Tooltip("Time in seconds it takes Milo to walk from the player to Master Ren through the waypoints")]
    public float timeToReachRen = 5.0f;

    private Transform playerTransform;
    private Animator miloAnimator;
    private float fixedY;

    // State tracking for Ren arrival and cyclical idle dialogue
    [HideInInspector] public bool isAtRen = false;
    private int renIdleLineIndex = 0;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (miloVisualObject == null) miloVisualObject = gameObject;
        miloAnimator = miloVisualObject.GetComponent<Animator>();

        // Hide Milo initially when the scene starts
        miloVisualObject.SetActive(false);
    }

    /// <summary>
    /// Hook this public function directly to Trench's narrative step `onStepCompleted` event.
    /// </summary>
    public void BeginMiloSequence()
    {
        if (miloVisualObject == null) return;

        // Dynamically find the active player in the scene
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }

        // Spawn Milo at his spawnpoint and lock his Y height to a flat plane
        miloVisualObject.SetActive(true);
        if (miloSpawnPoint != null)
        {
            fixedY = miloSpawnPoint.position.y;
            Vector3 spawnPos = miloSpawnPoint.position;
            spawnPos.y = fixedY;
            miloVisualObject.transform.position = spawnPos;
            miloVisualObject.transform.rotation = miloSpawnPoint.rotation;
        }
        else
        {
            fixedY = miloVisualObject.transform.position.y;
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
                Debug.LogError("MiloController could not find the Player in the scene!");
                yield break;
            }
        }

        // --- SEQUENCE 1: Play Dialogue while walking to Player ---
        if (dialogueSpawnToPlayer != null && DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartSequence(dialogueSpawnToPlayer);
        }

        // Walk to Player (X and Z plane only)
        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

        float walkToPlayerSpeed = 3.5f;
        while (playerTransform != null)
        {
            Vector3 playerPosXZ = new Vector3(playerTransform.position.x, fixedY, playerTransform.position.z);
            Vector3 currentPosXZ = new Vector3(miloVisualObject.transform.position.x, fixedY, miloVisualObject.transform.position.z);

            if (Vector3.Distance(currentPosXZ, playerPosXZ) <= playerStopDistance) break;

            Vector3 nextPos = Vector3.MoveTowards(currentPosXZ, playerPosXZ, walkToPlayerSpeed * Time.deltaTime);
            nextPos.y = fixedY; 
            miloVisualObject.transform.position = nextPos;

            RotateTowardsXZ(playerPosXZ);
            yield return null;
        }

        // Stop walking animation when reaching player
        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);

        // Wait the configurable amount of time by the player
        yield return new WaitForSeconds(timeToStayAtPlayer);

        // --- SEQUENCE 2: Play Dialogue while walking towards Master Ren ---
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

        // Traverse path to Master Ren over `timeToReachRen` seconds
        if (fullPath.Count > 0)
        {
            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

            float totalDistance = 0f;
            Vector3 lastPos = new Vector3(miloVisualObject.transform.position.x, fixedY, miloVisualObject.transform.position.z);
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
                    Vector3 currentPosXZ = new Vector3(miloVisualObject.transform.position.x, fixedY, miloVisualObject.transform.position.z);
                    if (Vector3.Distance(currentPosXZ, targetPoint) < 0.1f) break;

                    Vector3 nextPos = Vector3.MoveTowards(currentPosXZ, targetPoint, travelSpeed * Time.deltaTime);
                    nextPos.y = fixedY;
                    miloVisualObject.transform.position = nextPos;

                    RotateTowardsXZ(targetPoint);
                    yield return null;
                }
            }

            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);
        }

        // Milo has reached Master Ren and stays there permanently (not destroyed)
        isAtRen = true;
        Debug.Log("Milo has arrived at Master Ren and is stationed there.");
    }

    /// <summary>
    /// Called when the player interacts with Milo while he is stationed at Master Ren.
    /// Plays one single dialogue line at a time, cycling through `miloIdleAtRenSequence`.
    /// </summary>
    public void HandleMiloInteraction()
    {
        if (!isAtRen) return; // Ignore clicks while he is still walking/busy

        if (miloIdleAtRenSequence == null || miloIdleAtRenSequence.lines == null || miloIdleAtRenSequence.lines.Count == 0)
        {
            Debug.LogWarning("Milo Idle At Ren Sequence has no lines assigned!");
            return;
        }

        // Get the current single line
        DialogueLine lineToPlay = miloIdleAtRenSequence.lines[renIdleLineIndex];

        // Play just this single line using DialogueUI's StartDynamicLines method
        if (DialogueUI.Instance != null)
        {
            DialogueUI.Instance.StartDynamicLines(new List<DialogueLine> { lineToPlay });
        }

        // Advance index and loop back to start if end of sequence is reached
        renIdleLineIndex = (renIdleLineIndex + 1) % miloIdleAtRenSequence.lines.Count;
    }

    private void RotateTowardsXZ(Vector3 targetPosition)
    {
        Vector3 currentPos = miloVisualObject.transform.position;
        Vector3 direction = (targetPosition - currentPos).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            miloVisualObject.transform.rotation = Quaternion.Slerp(miloVisualObject.transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }
}