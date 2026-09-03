using UnityEngine;
using System.Reflection;
using System.Collections;

[RequireComponent(typeof(Collider))]
public class HubCameraTrigger : MonoBehaviour
{
    [Tooltip("Assign the Main Camera holding the IsoCameraRig. If left blank, it will auto-find it.")]
    public IsoCameraRig cameraRig;

    [Tooltip("Total time in seconds to complete the camera zoom transition.")]
    public float transitionDuration = 2.0f;

    [Tooltip("Easing curve for accelerating at the start and decelerating at the end.")]
    public AnimationCurve easeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private FieldInfo offsetField;
    private Vector3 originalOffset;
    private Vector3 cinematicOffset;
    private Coroutine transitionCoroutine;

    private void Start()
    {
        if (cameraRig == null) cameraRig = FindObjectOfType<IsoCameraRig>();

        // Access the private 'offset' field on IsoCameraRig using reflection
        offsetField = typeof(IsoCameraRig).GetField("offset", BindingFlags.NonPublic | BindingFlags.Instance);

        if (offsetField != null)
        {
            originalOffset = (Vector3)offsetField.GetValue(cameraRig);
            cinematicOffset = originalOffset * 2f;
        }
        else
        {
            Debug.LogError("Reflection failed: Could not find the private 'offset' field in IsoCameraRig.");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            transitionCoroutine = StartCoroutine(TransitionOffset(cinematicOffset));
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
            transitionCoroutine = StartCoroutine(TransitionOffset(originalOffset));
        }
    }

    private IEnumerator TransitionOffset(Vector3 targetOffset)
    {
        if (offsetField == null) yield break;

        Vector3 startOffset = (Vector3)offsetField.GetValue(cameraRig);
        float elapsedTime = 0f;

        while (elapsedTime < transitionDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsedTime / transitionDuration);

            // Evaluate the S-curve (0 to 1 progress)
            float easedT = easeCurve.Evaluate(normalizedTime);

            // Interpolate smoothly based on the evaluated curve
            Vector3 currentOffset = Vector3.LerpUnclamped(startOffset, targetOffset, easedT);
            offsetField.SetValue(cameraRig, currentOffset);

            yield return null;
        }

        // Lock to exact position at the end of the transition
        offsetField.SetValue(cameraRig, targetOffset);
    }
}