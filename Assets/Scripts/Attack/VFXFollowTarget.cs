using UnityEngine;

/// <summary>
/// Keeps a spawned VFX stuck to the point where it hit, even when the target moves (knockback, ragdoll).
/// Follows position only. Rotation and scale of the VFX are left alone.
/// Added at runtime by CombatHitboxController, so you don't need to put it on the prefab.
/// </summary>
[DefaultExecutionOrder(100)] // run after most movement scripts so it doesn't lag a frame behind
public class VFXFollowTarget : MonoBehaviour
{
    private Transform target;
    private Vector3 localOffset;

    public void Attach(Transform newTarget)
    {
        target = newTarget;
        // Remember where we are relative to the target, so we stick to that exact spot on it
        localOffset = target.InverseTransformPoint(transform.position);
    }

    // LateUpdate runs every frame, including during hit stop (when timeScale is nearly 0)
    private void LateUpdate()
    {
        if (target == null)
        {
            // Enemy was destroyed: stay where we are and stop following
            enabled = false;
            return;
        }

        transform.position = target.TransformPoint(localOffset);
    }
}