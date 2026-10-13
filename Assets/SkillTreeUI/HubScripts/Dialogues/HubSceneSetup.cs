using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class HubStepEvent
{
    public string label;
    public DialogueSequence sequence;
    [Tooltip("-1 = fire whenever this sequence completes, in any run. Otherwise only in that run.")]
    public int runNumber = -1;
    public UnityEvent onCompleted;
}

/// <summary>
/// Lives in Scene_Hub only. Holds everything that needs Hub scene references, because the persistent
/// HubNarrativeManager cannot. Its presence also tells the manager "we are in the Hub".
/// </summary>
public class HubSceneSetup : MonoBehaviour
{
    public static HubSceneSetup Current { get; private set; }

    [Header("VFX & Spawn")]
    public GameObject vesselConjureVFX;
    public Transform spawnPoint;
    public GameObject playerPrefab;

    [Tooltip("Seconds to wait after the player is conjured before this run's opening step plays.")]
    public float openingDelay = 1.5f;

    [Header("Step Events (scene objects)")]
    [Tooltip("Fired when a narrative step completes. Put Hub-scene targets here, e.g. Milo's BeginMiloSequence.")]
    public List<HubStepEvent> stepEvents = new List<HubStepEvent>();

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    private IEnumerator Start()
    {
        SpawnPlayer();

        yield return new WaitForSeconds(openingDelay);

        if (HubNarrativeManager.Instance == null)
        {
            Debug.LogError("[HubSceneSetup] No HubNarrativeManager found. Add it to the first scene (Scene_Level).");
            yield break;
        }

        HubNarrativeManager.Instance.PlayOpeningStep();
    }

    private void SpawnPlayer()
    {
        if (spawnPoint == null)
        {
            Debug.LogWarning("[HubSceneSetup] No spawnPoint assigned - skipping vessel conjure.");
            return;
        }

        if (vesselConjureVFX != null)
            Instantiate(vesselConjureVFX, spawnPoint.position, Quaternion.identity);

        if (playerPrefab == null) return;

        GameObject activePlayer = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);

        PlayerDissolveController dissolve = activePlayer.GetComponent<PlayerDissolveController>();
        if (dissolve != null) dissolve.TriggerDissolveIn();

        IsoCameraRig camRig = FindFirstObjectByType<IsoCameraRig>();
        if (camRig != null)
        {
            FieldInfo targetField = typeof(IsoCameraRig).GetField("target", BindingFlags.NonPublic | BindingFlags.Instance);
            if (targetField != null) targetField.SetValue(camRig, activePlayer.transform);
        }
    }

    /// <summary>Called by HubNarrativeManager when a step finishes.</summary>
    public void InvokeStepEvents(DialogueSequence sequence, int run)
    {
        foreach (HubStepEvent e in stepEvents)
        {
            if (e == null || e.sequence != sequence) continue;
            if (e.runNumber >= 0 && e.runNumber != run) continue;
            e.onCompleted?.Invoke();
        }
    }
}