using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MiloController : MonoBehaviour
{
    public static MiloController Instance;

    [Header("References")]
    [Tooltip("Milo's character model/GameObject")]
    public GameObject miloVisualObject;
    
    [Tooltip("Where Milo appears after Trench's dialogue finishes")]
    public Transform miloSpawnPoint;
    
    [Header("Waypoints to Master Ren")]
    [Tooltip("Add your sequence of transforms (e.g., 3 to 5 or more) leading from the player to Master Ren")]
    public List<Transform> waypointsToRen = new List<Transform>();

    [Header("Movement Settings")]
    public float walkSpeed = 3.5f;
    [Tooltip("How close Milo gets to the player before stopping")]
    public float stoppingDistanceToPlayer = 1.5f;
    [Tooltip("How close Milo needs to get to each waypoint before moving to the next")]
    public float waypointThreshold = 0.2f;

    private Transform playerTransform;
    private Animator miloAnimator;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (miloVisualObject != null)
        {
            miloVisualObject.SetActive(false);
            miloAnimator = miloVisualObject.GetComponent<Animator>();
        }
    }

    /// <summary>
    /// Hook this public function to Trench's narrative step `onStepCompleted` event.
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

        // Spawn Milo at his starting point
        miloVisualObject.SetActive(true);
        if (miloSpawnPoint != null)
        {
            miloVisualObject.transform.position = miloSpawnPoint.position;
            miloVisualObject.transform.rotation = miloSpawnPoint.rotation;
        }

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

        // --- PHASE 1: Walk towards the Player ---
        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

        while (playerTransform != null && Vector3.Distance(miloVisualObject.transform.position, playerTransform.position) > stoppingDistanceToPlayer)
        {
            Vector3 targetPos = playerTransform.position;
            miloVisualObject.transform.position = Vector3.MoveTowards(
                miloVisualObject.transform.position,
                targetPos,
                walkSpeed * Time.deltaTime
            );

            RotateTowards(targetPos);
            yield return null;
        }

        // Reached player, stop walking animation
        if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);

        // --- PHASE 2: Wait 1 second near the player ---
        yield return new WaitForSeconds(1.0f);

        // --- PHASE 3: Walk through the fixed waypoints towards Master Ren ---
        if (waypointsToRen != null && waypointsToRen.Count > 0)
        {
            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", true);

            foreach (Transform waypoint in waypointsToRen)
            {
                if (waypoint == null) continue;

                while (Vector3.Distance(miloVisualObject.transform.position, waypoint.position) > waypointThreshold)
                {
                    miloVisualObject.transform.position = Vector3.MoveTowards(
                        miloVisualObject.transform.position,
                        waypoint.position,
                        walkSpeed * Time.deltaTime
                    );

                    RotateTowards(waypoint.position);
                    yield return null;
                }
            }

            // Reached Master Ren's destination point, stop walking
            if (miloAnimator != null) miloAnimator.SetBool("IsWalking", false);
        }

        Debug.Log("Milo completed his walk sequence and is waiting by Master Ren.");
    }

    private void RotateTowards(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - miloVisualObject.transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            miloVisualObject.transform.rotation = Quaternion.Slerp(miloVisualObject.transform.rotation, targetRot, Time.deltaTime * 10f);
        }
    }
}