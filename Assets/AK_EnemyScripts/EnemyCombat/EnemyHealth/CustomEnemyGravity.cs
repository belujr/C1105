using UnityEngine;
using System.Collections;
using CombatSystem.Data;

[RequireComponent(typeof(CharacterController))]
public class CustomEnemyGravity : MonoBehaviour
{
    [Header("Gravity Settings")]
    [SerializeField] private float gravity = -20f;
    
    [Header("Drop Deployment & Scatter")]
    [Tooltip("Distance above ground to re-enable the CharacterController collider before landing.")]
    [SerializeField] private float reenableHeightThreshold = 1.5f;
    [Tooltip("Maximum random horizontal scatter force applied during the drop to prevent stacking.")]
    [SerializeField] private float scatterForce = 2.0f;

    [Header("Collision Filtering")]
    [Tooltip("Layers to ignore during the drop raycast check.")]
    [SerializeField] private LayerMask ignoreLayer;

    private CharacterController characterController;
    private Component animationEngine;
    private float verticalVelocity = 0f;
    private Vector3 horizontalVelocity = Vector3.zero;
    private bool isDroppedFromShip = false;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        animationEngine = GetComponent("EnemyAnimationEngine");
    }

    private void OnEnable()
    {
        verticalVelocity = 0f;
        
        Vector2 randomCircle = Random.insideUnitCircle.normalized * Random.Range(0.5f, scatterForce);
        horizontalVelocity = new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (characterController != null)
        {
            characterController.enabled = false;
        }
        
        isDroppedFromShip = true;
        StartCoroutine(ManageDropRoutine());
    }

    private IEnumerator ManageDropRoutine()
    {
        while (isDroppedFromShip)
        {
            verticalVelocity += gravity * Time.deltaTime;
            
            Vector3 dropMove = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
            transform.position += dropMove;

            horizontalVelocity = Vector3.Lerp(horizontalVelocity, Vector3.zero, Time.deltaTime * 3f);

            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, reenableHeightThreshold, ~ignoreLayer) || 
                (characterController != null && characterController.isGrounded))
            {
                if (characterController != null)
                {
                    characterController.enabled = true;
                }

                PlayLandingAnimationFromProfile();

                isDroppedFromShip = false;
                break;
            }

            yield return null;
        }
    }

    private void PlayLandingAnimationFromProfile()
    {
        if (animationEngine == null) return;

        EnemyAnimProfile profile = null;

        // Fetch profile dynamically from BaseEnemyBrain or EnemyDummyController
        Component brain = GetComponent("BaseEnemyBrain");
        if (brain != null)
        {
            var dataField = brain.GetType().GetField("enemyData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (dataField != null)
            {
                ScriptableObject dataSO = dataField.GetValue(brain) as ScriptableObject;
                if (dataSO != null)
                {
                    var profileProp = dataSO.GetType().GetProperty("AnimationProfile") ?? dataSO.GetType().GetProperty("animProfile");
                    if (profileProp != null)
                    {
                        profile = profileProp.GetValue(dataSO) as EnemyAnimProfile;
                    }
                }
            }
        }

        AnimationClip landingClip = profile != null ? profile.landingClip : null;
        float transition = profile != null ? profile.landingTransitionDuration : 0.1f;
        float speed = profile != null ? profile.landingPlaybackSpeed : 1.0f;

        if (landingClip == null) return;

        var method = animationEngine.GetType().GetMethod("PlayAnimation") ?? 
                     animationEngine.GetType().GetMethod("Play");

        if (method != null)
        {
            var parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                string pName = parameters[i].Name.ToLower();

                if (pType == typeof(AnimationClip))
                {
                    args[i] = landingClip;
                }
                else if (pType == typeof(string))
                {
                    args[i] = "Landing";
                }
                else if (pType == typeof(float))
                {
                    if (pName.Contains("speed") || pName.Contains("rate") || pName.Contains("multiplier"))
                    {
                        args[i] = speed;
                    }
                    else
                    {
                        args[i] = transition;
                    }
                }
                else if (pType.IsValueType)
                {
                    args[i] = System.Activator.CreateInstance(pType);
                }
                else
                {
                    args[i] = null;
                }
            }

            try
            {
                method.Invoke(animationEngine, args);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CustomEnemyGravity] Failed to play landing animation from profile: {ex.Message}", this);
            }
        }
    }

    private void Update()
    {
        if (isDroppedFromShip) return;

        if (characterController == null || !characterController.enabled) return;

        if (characterController.isGrounded)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        characterController.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        isDroppedFromShip = false;
    }
}