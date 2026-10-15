using UnityEngine;

/// <summary>
/// Makes enemies that are moved by SETTING transform.position (dodges, lunges, knockback slides, strafing)
/// stop at walls and other solid level colliders, instead of passing through them.
///
/// It does not change how those moves work: the same positions are used, the only difference is that a move
/// is shortened when a solid wall is in the way (and slides along the wall instead of stopping dead).
///
/// Only the ENVIRONMENT blocks. The player, other enemies, allies and anything with IDamageable are ignored here,
/// exactly as before, so combat behaviour does not change. Triggers are ignored. Ground is never treated as a wall.
///
/// Enemies that already move through CharacterController.Move (ImpBrain, MinionEnemyBrain, RangedMinionBrain,
/// BaseEnemyBrain...) collide with walls by themselves and do not need this.
/// </summary>
public static class EnemyCollision
{
	private const int MaxPasses = 2;
	private static readonly RaycastHit[] hitBuffer = new RaycastHit[24];

	/// <summary>
	/// Returns 'delta' shortened so the body does not enter a wall. Only the horizontal part is checked;
	/// the vertical part is returned unchanged. Works even while the CharacterController is disabled (dodges).
	/// </summary>
	public static Vector3 ClampMove(Transform body, CharacterController cc, Vector3 delta)
	{
		Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
		float horizontalDistance = horizontal.magnitude;
		if (horizontalDistance < 0.0001f) return delta;

		Vector3 center;
		float radius, height, stepOffset, skin, slopeLimit;
		if (!GetCapsule(body, cc, out center, out radius, out height, out stepOffset, out skin, out slopeLimit)) return delta;

		float minWalkableY = Mathf.Cos(slopeLimit * Mathf.Deg2Rad);

		// the capsule, with its bottom raised by the step height so small steps and ground bumps never count as walls
		float half = Mathf.Max(0f, height * 0.5f - radius);
		float lift = Mathf.Min(stepOffset + skin, half * 2f);
		Vector3 top = center + Vector3.up * half;
		Vector3 bottom = center - Vector3.up * half + Vector3.up * lift;

		Vector3 moved = Vector3.zero;
		Vector3 remaining = horizontal;

		for (int pass = 0; pass < MaxPasses && remaining.sqrMagnitude > 0.000001f; pass++)
		{
			float dist = remaining.magnitude;
			Vector3 dir = remaining / dist;

			int count = Physics.CapsuleCastNonAlloc(top + moved, bottom + moved, radius, dir, hitBuffer, dist + skin,
				Physics.AllLayers, QueryTriggerInteraction.Ignore);

			bool blocked = false;
			float bestDist = float.MaxValue;
			Vector3 bestNormal = Vector3.zero;

			for (int i = 0; i < count; i++)
			{
				RaycastHit h = hitBuffer[i];
				if (h.collider == null) continue;
				if (h.distance <= 0f) continue;                       // already overlapping at the start: never get stuck
				if (h.normal.y > minWalkableY) continue;              // walkable ground, not a wall
				if (h.collider.transform.IsChildOf(body)) continue;   // myself
				if (IsCreature(h.collider)) continue;                 // player / enemies / allies: unchanged behaviour

				if (h.distance < bestDist)
				{
					bestDist = h.distance;
					bestNormal = h.normal;
					blocked = true;
				}
			}

			if (!blocked)
			{
				moved += remaining;
				remaining = Vector3.zero;
				break;
			}

			float allowed = Mathf.Min(Mathf.Max(0f, bestDist - skin), dist);
			moved += dir * allowed;

			// slide along the wall for the rest of the move instead of stopping dead
			Vector3 leftover = dir * (dist - allowed);
			bestNormal.y = 0f;
			if (bestNormal.sqrMagnitude < 0.0001f) break;
			bestNormal.Normalize();
			remaining = Vector3.ProjectOnPlane(leftover, bestNormal);
		}

		return new Vector3(moved.x, delta.y, moved.z);
	}

	private static bool IsCreature(Collider c)
	{
		if (c is CharacterController) return true;
		if (c.GetComponentInParent<IDamageable>() != null) return true;
		if (c.GetComponentInParent<PlayerController>() != null) return true;
		return false;
	}

	private static bool GetCapsule(Transform body, CharacterController cc, out Vector3 center, out float radius,
		out float height, out float stepOffset, out float skin, out float slopeLimit)
	{
		center = body.position;
		radius = 0.4f;
		height = 1.8f;
		stepOffset = 0.3f;
		skin = 0.05f;
		slopeLimit = 45f;

		Vector3 ls = body.lossyScale;
		float radiusScale = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z));
		float heightScale = Mathf.Abs(ls.y);

		if (cc != null)
		{
			// the values can be read even while the controller is disabled
			radius = Mathf.Max(0.05f, cc.radius * radiusScale);
			height = Mathf.Max(cc.height * heightScale, radius * 2f);
			center = body.TransformPoint(cc.center);
			stepOffset = cc.stepOffset * heightScale;
			skin = Mathf.Max(0.02f, cc.skinWidth * radiusScale);
			slopeLimit = cc.slopeLimit;
			return true;
		}

		Collider col = body.GetComponent<Collider>();
		if (col != null)
		{
			Bounds b = col.bounds;
			if (b.size.sqrMagnitude > 0.0001f)
			{
				radius = Mathf.Max(0.05f, Mathf.Min(b.extents.x, b.extents.z));
				height = Mathf.Max(b.size.y, radius * 2f);
				center = b.center;
				return true;
			}
		}

		return false;
	}
}
