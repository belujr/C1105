using UnityEngine;

namespace UnityChan
{
    public class SpringCollider : MonoBehaviour
    {
        public float radius = 0.1f;
        public float height = 0.0f; // If 0, it acts as a sphere. If > 0, it acts as a capsule along the local Y axis.
        public Vector3 offset = Vector3.zero;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Vector3 center = transform.TransformPoint(offset);

            if (height <= 0)
            {
                Gizmos.DrawWireSphere(center, radius);
            }
            else
            {
                // Draw a capsule shape
                Vector3 up = transform.up * (height * 0.5f);
                Vector3 top = center + up;
                Vector3 bottom = center - up;

                Gizmos.DrawWireSphere(top, radius);
                Gizmos.DrawWireSphere(bottom, radius);

                Gizmos.DrawLine(top + transform.right * radius, bottom + transform.right * radius);
                Gizmos.DrawLine(top - transform.right * radius, bottom - transform.right * radius);
                Gizmos.DrawLine(top + transform.forward * radius, bottom + transform.forward * radius);
                Gizmos.DrawLine(top - transform.forward * radius, bottom - transform.forward * radius);
            }
        }
    }
}