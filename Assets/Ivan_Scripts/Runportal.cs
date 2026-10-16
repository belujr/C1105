using UnityEngine;
using UnityEngine.Events;
 
// Trigger collider that sends the player to another scene with a seamless fade.
[RequireComponent(typeof(Collider))]
public class RunPortal : MonoBehaviour
{
    [SerializeField] private string targetScene = "scene_level";

    [Header("Tutorial")]
    [Tooltip("ON: during the tutorial the portal does nothing until the 'Traverse through the portal' objective " +
             "appears, so the controls can't be skipped. No effect once the tutorial is done or on later runs.")]
    [SerializeField] private bool lockedUntilTutorialReady = true;
    [Tooltip("Player touched the portal while it is still locked (hook up a 'not yet' sound or hint).")]
    [SerializeField] private UnityEvent onLockedAttempt;
 
    private void OnTriggerEnter(Collider other)
    {
        // Ignore everything except the player, and ignore re-triggers mid-transition
        if (SceneTransitionManager.IsTransitioning || !other.CompareTag("Player")) return;

        // Tutorial: stay shut until the portal objective has appeared
        TutorialManager tutorial = TutorialManager.Instance;
        if (lockedUntilTutorialReady && tutorial != null && !tutorial.IsPortalOpen)
        {
            onLockedAttempt?.Invoke();
            return;
        }
 
        if (SceneTransitionManager.LoadScene(targetScene))
        {
            CameraTransitionManager.RequestGameplayEntry(targetScene); // skips a title shot if that scene has one

            // Completes the "Traverse through the portal" objective and marks the tutorial as finished
            if (tutorial != null) tutorial.Report(TutorialCondition.Portal);
        }
    }
}