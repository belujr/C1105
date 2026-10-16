using UnityEngine;

/// <summary>
/// A trigger collider that switches an AmbientMusic extra layer when the player walks in.
/// Typical use: a "Buildup" layer (Replaces Music ticked, Priority 2) that crossfades the
/// current music (BGM or Combat) into a build-up track as the player enters this zone.
///
/// Setup: create an empty GameObject, add a Box Collider (this script ticks Is Trigger for
/// you), size it over the area, add this component, and type the layer name.
/// The player must be tagged "Player" (same as your portal).
/// </summary>
[RequireComponent(typeof(Collider))]
public class MusicTrigger : MonoBehaviour
{
    [Header("Music")]
    [Tooltip("Name of the Extra Layer in AmbientMusic. Must match exactly.")]
    [SerializeField] private string layerName = "Buildup";
    [Tooltip("ON: entering the zone switches the layer ON. OFF: entering the zone switches the layer OFF (use it to end a build-up).")]
    [SerializeField] private bool turnLayerOn = true;
    [Tooltip("ON: when the player leaves the zone the layer goes back (switches off, or on if 'Turn Layer On' is off). OFF: one-way, it stays.")]
    [SerializeField] private bool revertOnExit = false;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    private int playerColliders;   // the player may have several colliders; count them so exit isn't triggered early

    // Called when you add the component, so the collider is a trigger from the start.
    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerColliders++;
        if (playerColliders == 1) Apply(turnLayerOn);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerColliders = Mathf.Max(0, playerColliders - 1);
        if (playerColliders == 0 && revertOnExit) Apply(!turnLayerOn);
    }

    private void OnDisable()
    {
        playerColliders = 0;
    }

    private void Apply(bool on)
    {
        var music = AmbientMusic.Current;
        if (music == null)
        {
            Debug.LogWarning("MusicTrigger: there is no AmbientMusic object in this scene.", this);
            return;
        }

        if (!music.HasLayer(layerName))
        {
            Debug.LogWarning("MusicTrigger: AmbientMusic has no extra layer named \"" + layerName +
                             "\" (check spelling and that the layer has a clip).", this);
            return;
        }

        music.SetLayerActive(layerName, on);
        if (debugLog) Debug.Log("MusicTrigger: layer \"" + layerName + "\" switched " + (on ? "ON" : "OFF"), this);
    }
}