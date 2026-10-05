using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class PatrolZone : MonoBehaviour
{
    [Header("Zone Configuration")]
    [Tooltip("Minimum distance minions must keep from each other's target destinations.")]
    public float minimumSpacing = 3.0f;
    [Tooltip("Radius to check for environmental obstacles around the generated point.")]
    public float obstacleCheckRadius = 1.0f;
    [Tooltip("The layer assigned to your background trees and walls.")]
    public LayerMask obstacleMask;

    private BoxCollider zoneCollider;
    
    // O(1) Dictionary tracking exactly where every minion in this zone is heading
    private Dictionary<int, Vector3> reservedPoints = new Dictionary<int, Vector3>();
    
    // Zero-allocation buffer for the physics check
    private static readonly Collider[] overlapBuffer = new Collider[1];

    private void Awake()
    {
        zoneCollider = GetComponent<BoxCollider>();
        zoneCollider.isTrigger = true;
    }

    public Vector3 GetValidPatrolPoint(int minionID, Vector3 currentMinionPos)
    {
        Bounds bounds = zoneCollider.bounds;
        Vector3 bestPoint = currentMinionPos; 
        bool foundValid = false;

        // Cap attempts to 15 per request to prevent CPU spikes in dense areas
        for (int i = 0; i < 15; i++)
        {
            Vector3 randomPoint = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                currentMinionPos.y, // Maintain the minion's current ground height
                Random.Range(bounds.min.z, bounds.max.z)
            );

            // 1. Coordination Check: Is another minion already walking here?
            bool tooClose = false;
            foreach (var reserved in reservedPoints.Values)
            {
                if ((randomPoint - reserved).sqrMagnitude < (minimumSpacing * minimumSpacing))
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // 2. Environment Check: Is this point inside a tree trunk?
            int hits = Physics.OverlapSphereNonAlloc(randomPoint, obstacleCheckRadius, overlapBuffer, obstacleMask);
            if (hits > 0) continue;

            // 3. Path Clearance Check: Is there a wall between the minion and this point?
            Vector3 dirToPoint = randomPoint - currentMinionPos;
            if (Physics.Raycast(currentMinionPos + Vector3.up, dirToPoint.normalized, dirToPoint.magnitude, obstacleMask))
            {
                continue; 
            }

            bestPoint = randomPoint;
            foundValid = true;
            break;
        }

        if (foundValid)
        {
            reservedPoints[minionID] = bestPoint; // Reserve the point for this specific minion
        }
        
        return bestPoint;
    }

    public void ReleasePoint(int minionID)
    {
        if (reservedPoints.ContainsKey(minionID))
        {
            reservedPoints.Remove(minionID);
        }
    }

    private void OnDrawGizmos()
    {
        if (zoneCollider == null) zoneCollider = GetComponent<BoxCollider>();
        if (zoneCollider != null)
        {
            Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
            Gizmos.DrawCube(zoneCollider.bounds.center, zoneCollider.bounds.size);
        }
    }
}