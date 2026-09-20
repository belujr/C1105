using UnityEngine;
using System.Collections;

namespace UnityChan
{
    public class RandomWind : MonoBehaviour
    {
        private SpringBone[] springBones;

        [Header("Main Switch")]
        public bool isWindActive = true;

        [Header("Wind Direction & Power")]
        [Tooltip("The X, Y, Z direction of the wind.")]
        public Vector3 windDirection = new Vector3(0, 0, -1);

        [Tooltip("If true, the wind rotates with the character. If false, it blows in a fixed world direction.")]
        public bool isLocalDirection = true;

        [Tooltip("The base strength of the wind.")]
        public float windIntensity = 0.01f;

        [Header("Wind Flutter (Noise)")]
        [Tooltip("How fast the wind pulses/flutters.")]
        public float flutterSpeed = 2.0f;

        [Tooltip("0 = steady continuous wind. 1 = highly gusty/random wind.")]
        [Range(0f, 1f)]
        public float flutterVariation = 1.0f;

        void Start()
        {
            springBones = GetComponent<SpringManager>().springBones;
        }

        void Update()
        {
            Vector3 force = Vector3.zero;

            if (isWindActive && springBones != null)
            {
                // 1. Calculate the target direction
                Vector3 normalizedDirection = windDirection.normalized;
                Vector3 actualDirection = isLocalDirection ? transform.TransformDirection(normalizedDirection) : normalizedDirection;

                // 2. Calculate the random wind noise
                float noise = Mathf.PerlinNoise(Time.time * flutterSpeed, 0.0f);

                // 3. Blend between a steady wind and the gusty noise
                float currentPower = windIntensity * Mathf.Lerp(1.0f, noise, flutterVariation);

                // 4. Apply to final force
                force = actualDirection * currentPower;
            }

            for (int i = 0; i < springBones.Length; i++)
            {
                springBones[i].springForce = force;
            }
        }
    }
}