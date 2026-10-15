using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public sealed class TutorialChecklistRow : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private GameObject checkmark;
    [SerializeField] private TMP_Text progressText;

    private CanvasGroup canvasGroup;
    private bool seconds;

    public void Initialize(TutorialStep step)
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        
        // Hide the entire row visually, but keep it active for the Layout Group
        canvasGroup.alpha = 0f; 

        // Force text alpha to 100% so they don't get stuck invisible
        if (label != null)
        {
            Color c = label.color;
            c.a = 1f;
            label.color = c;
            label.text = step.GetLabel();
        }
        
        if (progressText != null)
        {
            Color c = progressText.color;
            c.a = 1f;
            progressText.color = c;
        }

        seconds = step.Action == TutorialAction.Move;
        if (checkmark != null) checkmark.SetActive(false);
        SetProgress(0f, step.Target);
    }

    public void SetProgress(float current, float target)
    {
        if (progressText == null) return;
        current = Mathf.Clamp(current, 0f, target);
        int display = seconds ? Mathf.FloorToInt(current * 10f) : Mathf.FloorToInt(current);
        if (seconds) progressText.SetText("{0:1}/{1:1}s", current, target);
        else progressText.SetText("{0}/{1}", current, target);
    }

    public void PlayFadeIn()
    {
        StartCoroutine(FadeInRoutine());
    }

    public void PlayCompletionSequence()
    {
        StartCoroutine(FlashCheckmarkRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        float duration = 0.5f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        canvasGroup.alpha = 1f;
    }

    private IEnumerator FlashCheckmarkRoutine()
    {
        if (checkmark == null) yield break;
        
        for (int i = 0; i < 3; i++)
        {
            checkmark.SetActive(true);
            yield return new WaitForSeconds(0.15f);
            checkmark.SetActive(false);
            yield return new WaitForSeconds(0.15f);
        }
        
        checkmark.SetActive(true);
    }
}