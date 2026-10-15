using System.Collections;
using UnityEngine;

public sealed class TutorialChecklistUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Transform container;
    [SerializeField] private TutorialChecklistRow rowPrefab;

    private TutorialChecklistRow[] rows = new TutorialChecklistRow[0];
    private bool[] completed = new bool[0];

    public bool IsConfigured => container != null && rowPrefab != null && !rowPrefab.gameObject.scene.IsValid();

    public void Build(TutorialObjectiveData data)
    {
        if (data == null || !IsConfigured) return;

        ClearContainer();
        rows = new TutorialChecklistRow[data.Steps.Count];
        completed = new bool[rows.Length];
        
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = Instantiate(rowPrefab, container);
            // CRITICAL: Keep active so VerticalLayoutGroup physically stacks them
            rows[i].gameObject.SetActive(true); 
            rows[i].Initialize(data.Steps[i]);
        }

        // Fade in the first step immediately
        if (rows.Length > 0)
        {
            rows[0].PlayFadeIn();
        }
    }

    public void SetProgress(int index, float current, float target)
    {
        if (Valid(index)) rows[index].SetProgress(current, target);
    }

    public void SetDone(int index)
    {
        if (!Valid(index) || completed[index]) return;
        completed[index] = true;
        
        StartCoroutine(CompleteAndAdvanceSequence(index));
    }

    private IEnumerator CompleteAndAdvanceSequence(int index)
    {
        rows[index].PlayCompletionSequence();
        yield return new WaitForSeconds(1f);

        int nextIndex = index + 1;
        if (Valid(nextIndex))
        {
            rows[nextIndex].PlayFadeIn();
        }
    }

    public void Show() => (visualRoot != null ? visualRoot : gameObject).SetActive(true);
    public void Hide() => (visualRoot != null ? visualRoot : gameObject).SetActive(false);

    private bool Valid(int index) => index >= 0 && index < rows.Length && rows[index] != null;

    private void ClearContainer()
    {
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            GameObject child = container.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }
}