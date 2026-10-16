using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this inside a chamber (prefab) on a trigger Collider covering the room's entrance. When the Player walks in,
/// the assigned TutorialSequence starts on the LevelObjectiveManager.
///
/// Each objective in the sequence is either condition based (stays until the player does it, e.g. a 3-hit chain) or
/// time based (stays for a few seconds, e.g. "Clear all the invaders"). That is set on the TutorialMissionData asset.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ChamberObjectiveTrigger : MonoBehaviour
{
    [Header("What to show")]
    [Tooltip("The objectives for this chamber (Create > Tutorial > Mission Sequence).")]
    [SerializeField] private TutorialSequence sequence;
    [Tooltip("ON: progress is saved and the sequence never plays again once finished (use for the X / Y teaching).\n" +
             "OFF: plays every time the player enters (use for a plain 'Clear all the invaders' sequence).")]
    [SerializeField] private bool showOnlyOnce = true;

    [Header("Events")]
    [Tooltip("Fires once when the last objective of the sequence is completed (or, for a timed one, has run out its time).")]
    public UnityEvent onObjectivesCompleted;

    private bool fired;

    private void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (fired || !other.CompareTag("Player")) return;

        LevelObjectiveManager manager = LevelObjectiveManager.Instance;
        if (manager == null)
        {
            Debug.LogWarning("[ChamberObjectiveTrigger] No LevelObjectiveManager in the scene.", this);
            return;
        }

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        bool started = manager.Begin(this, sequence, player, showOnlyOnce,
                                     () => onObjectivesCompleted.Invoke());
        if (started) fired = true;
    }

    private void OnDestroy()
    {
        // The chamber is destroyed by LevelSpawner.ClearLevel: take its objectives off the screen with it.
        LevelObjectiveManager manager = LevelObjectiveManager.Instance;
        if (manager != null) manager.Cancel(this);
    }
}