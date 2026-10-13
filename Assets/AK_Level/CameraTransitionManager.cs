using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class CameraTransitionManager : MonoBehaviour
{
    public enum CameraState { Title, Transitioning, Gameplay }

    [Header("Camera References")]
    [SerializeField, Tooltip("The sole rendering camera.")] private Camera gameplayCamera;
    [SerializeField, Tooltip("Disabled camera storing the opening shot.")] private Camera titleCamera;
    [SerializeField, Tooltip("IsoCameraRig on Main Camera.")] private IsoCameraRig gameplayRig;

    [Header("Player References")]
    [SerializeField, Tooltip("Runtime reference: automatically resolved from IsoCameraRig.Target. Leave empty in Edit Mode.")] private PlayerController playerController;
    [SerializeField, Tooltip("Runtime reference: automatically resolved from the spawned player. Leave empty in Edit Mode.")] private PlayerInputHandler playerInput;

    [Header("Curved Path")]
    [SerializeField, Tooltip("First Bezier handle: pull back and rise.")] private Transform controlPoint1;
    [SerializeField, Tooltip("Second Bezier handle: sweep toward the forest.")] private Transform controlPoint2;
    [SerializeField, Min(0f), Tooltip("Unscaled hold time before movement.")] private float holdDuration = 1f;
    [SerializeField, Min(0.1f), Tooltip("Unscaled travel time after the hold.")] private float transitionDuration = 5f;
    [SerializeField, Tooltip("Travel easing: keep smoothly increasing from 0 to 1.")]
    private AnimationCurve travelCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Tooltip("Blend from title rotation to the live gameplay rotation.")]
    private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Tooltip("Blend from title FOV to the original Main Camera FOV.")]
    private AnimationCurve fovCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Runtime Status")]
    [SerializeField] private CameraState currentState = CameraState.Title;
    public CameraState CurrentState => currentState;
    private bool ready, handingOff;
    private bool playerBound;
    private bool waitingWarningLogged;
    private float waitingTime;
    private float gameplayFov, elapsed, startFov;
    private Vector3 startPosition;
    private Quaternion startRotation;

    private void Awake()
    {
        bool valid = true;
        valid &= CheckReference(gameplayCamera, nameof(gameplayCamera));
        valid &= CheckReference(titleCamera, nameof(titleCamera));
        valid &= CheckReference(gameplayRig, nameof(gameplayRig));
        valid &= CheckReference(travelCurve, nameof(travelCurve));
        valid &= CheckReference(rotationCurve, nameof(rotationCurve));
        valid &= CheckReference(fovCurve, nameof(fovCurve));
        if (!valid) { enabled = false; return; }
        if (gameplayCamera == titleCamera || gameplayRig.transform != gameplayCamera.transform ||
            gameplayCamera.orthographic || titleCamera.orthographic)
        {
            Debug.LogWarning("CameraTransitionManager: Assign different perspective cameras, " +
                "and gameplayRig on gameplayCamera.", this);
            enabled = false;
            return;
        }
        // Ignore Inspector prefab references. Bind only after the real player spawns.
        playerController = null;
        playerInput = null;
        gameplayFov = gameplayCamera.fieldOfView;
        ready = true;
        ApplyTitleState();
    }

    private void Start() { if (ready) ApplyTitleState(); }

    private void Update()
    {
        if (!ready || playerBound || currentState != CameraState.Title) return;
        // HubNarrativeManager assigns the spawned player to this rig in Start.
        // Our early Update locks that instance before its first input/logic Update.
        if (TryBindSpawnedPlayer(false)) return;
        waitingTime += Time.unscaledDeltaTime;
        if (waitingTime >= 10f && !waitingWarningLogged)
        {
            waitingWarningLogged = true;
            Debug.LogWarning("CameraTransitionManager: Still waiting for 'gameplayRig.Target' " +
                "to reference a spawned player with PlayerController and PlayerInputHandler.", this);
        }
    }

    private bool TryBindSpawnedPlayer(bool reportMissing)
    {
        if (playerBound)
            return CheckReference(playerController, nameof(playerController)) &
                CheckReference(playerInput, nameof(playerInput));

        Transform target = gameplayRig.Target;
        if (target == null || !target.gameObject.scene.IsValid() || !target.gameObject.activeInHierarchy)
        {
            if (reportMissing)
                Debug.LogWarning("CameraTransitionManager: 'gameplayRig.Target' must reference " +
                    "the active spawned player in the scene, not a prefab asset.", this);
            return false;
        }

        PlayerController controller = target.GetComponent<PlayerController>();
        PlayerInputHandler input = target.GetComponent<PlayerInputHandler>();
        if (controller == null || input == null)
        {
            if (reportMissing)
            {
                CheckReference(controller, "playerController on gameplayRig.Target");
                CheckReference(input, "playerInput on gameplayRig.Target");
            }
            return false;
        }

        playerController = controller;
        playerInput = input;
        playerBound = true;
        controller.enabled = false;
        input.enabled = false;
        Debug.Log("CameraTransitionManager: Bound and locked spawned player '" +
            target.name + "'.", this);
        return true;
    }

    private void ApplyTitleState()
    {
        if (playerBound)
        {
            if (CheckReference(playerController, nameof(playerController))) playerController.enabled = false;
            if (CheckReference(playerInput, nameof(playerInput))) playerInput.enabled = false;
        }
        gameplayRig.enabled = false;
        titleCamera.enabled = false;
        gameplayCamera.enabled = true;
        gameplayCamera.transform.SetPositionAndRotation(titleCamera.transform.position, titleCamera.transform.rotation);
        gameplayCamera.fieldOfView = titleCamera.fieldOfView;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        currentState = CameraState.Title;
    }

    [ContextMenu("Test / Reapply Title Shot")]
    public void ReapplyTitleShot()
    {
        if (!Application.isPlaying || !ready || currentState != CameraState.Title)
        {
            Debug.LogWarning("CameraTransitionManager: ReapplyTitleShot requires Play Mode in Title. " +
                "Restart Play Mode to repeat the preview.", this);
            return;
        }
        ApplyTitleState();
        Debug.Log("CameraTransitionManager: Title shot reapplied.", this);
    }

    [ContextMenu("Test / Play Transition")]
    public void PlayTransition()
    {
        if (!Application.isPlaying || !ready || currentState != CameraState.Title) return;
        bool valid = true;
        valid &= CheckReference(controlPoint1, nameof(controlPoint1));
        valid &= CheckReference(controlPoint2, nameof(controlPoint2));
        valid &= TryBindSpawnedPlayer(true);
        if (!valid) return;
        startPosition = gameplayCamera.transform.position;
        startRotation = gameplayCamera.transform.rotation;
        startFov = gameplayCamera.fieldOfView;
        gameplayRig.ClearTransitionShake();
        elapsed = 0f;
        handingOff = false;
        currentState = CameraState.Transitioning;
        Debug.Log("CameraTransitionManager: Transitioning.", this);
    }

    private void LateUpdate()
    {
        if (!ready || currentState != CameraState.Transitioning || handingOff) return;
        bool valid = true;
        valid &= CheckReference(gameplayCamera, nameof(gameplayCamera));
        valid &= CheckReference(gameplayRig, nameof(gameplayRig));
        valid &= CheckReference(controlPoint1, nameof(controlPoint1));
        valid &= CheckReference(controlPoint2, nameof(controlPoint2));
        if (!valid) { enabled = false; return; }
        if (!gameplayRig.TryGetTransitionPose(Time.unscaledDeltaTime, out Vector3 endPosition, out Quaternion endRotation))
        {
            enabled = false;
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01((elapsed - holdDuration) / Mathf.Max(0.1f, transitionDuration));
        float u = EvaluateCurve(travelCurve, t);
        float v = 1f - u;
        Vector3 position = v * v * v * startPosition +
            3f * v * v * u * controlPoint1.position +
            3f * v * u * u * controlPoint2.position + u * u * u * endPosition;
        gameplayCamera.transform.SetPositionAndRotation(position,
            Quaternion.Slerp(startRotation, endRotation, EvaluateCurve(rotationCurve, t)));
        gameplayCamera.fieldOfView = Mathf.Lerp(startFov, gameplayFov, EvaluateCurve(fovCurve, t));
        if (t >= 1f) { handingOff = true; StartCoroutine(HandOff()); }
    }

    private float EvaluateCurve(AnimationCurve curve, float t)
    {
        // Exact endpoints even when Inspector curves are edited.
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return Mathf.Clamp01(curve.Evaluate(t));
    }

    private IEnumerator HandOff()
    {
        // Resume next frame; unlike WaitForEndOfFrame this also works in Scene view.
        yield return null;
        if (!CheckReference(gameplayRig, nameof(gameplayRig)) ||
            !CheckReference(playerInput, nameof(playerInput)) ||
            !CheckReference(playerController, nameof(playerController))) yield break;
        gameplayRig.enabled = true;
        playerInput.enabled = true;
        // Refresh cached input before enabling the controller.
        yield return null;
        if (!CheckReference(playerController, nameof(playerController))) yield break;
        playerController.enabled = true;
        currentState = CameraState.Gameplay;
        Debug.Log("CameraTransitionManager: Gameplay. PlayerInputHandler enabled=" +
            playerInput.enabled + ", PlayerController enabled=" + playerController.enabled +
            ", player active=" + playerController.gameObject.activeInHierarchy +
            ", movement locked=" + playerController.IsMovementLocked + ".", this);
    }

    private bool CheckReference(object reference, string fieldName)
    {
        bool missing = reference == null || (reference is Object unityObject && unityObject == null);
        if (!missing) return true;
        Debug.LogWarning($"CameraTransitionManager: Missing Inspector field '{fieldName}'.", this);
        return false;
    }
}
