using UnityEngine;

// Put on the root of a layer prefab (e.g. the Run 0 forest). It only exists on the chosen run.
public class RunLayer : MonoBehaviour
{
    [SerializeField] private int visibleOnRun = 0;

    // Start (not Awake) so it reads the run after HubNarrativeManager's optional save reset.
    private void Start() => gameObject.SetActive(RunState.Current == visibleOnRun);
}