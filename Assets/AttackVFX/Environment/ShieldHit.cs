using System.Collections;
using UnityEngine;

public class ShieldHit : MonoBehaviour
{
    [SerializeField] Renderer ripple;          // child sphere with ripple material
    [SerializeField] float maxRadius = 1.5f;
    [SerializeField] float duration = 0.6f;
    [SerializeField] Color hitColor = Color.white;

    Material rippleMat;
    Coroutine routine;

    void Awake()
    {
        rippleMat = ripple.material;   // instance
        ripple.enabled = false;
    }

    // Call this from your attack/projectile code
    public void Hit(Vector3 worldPoint)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Ripple(worldPoint));
    }

    IEnumerator Ripple(Vector3 point)
    {
        ripple.enabled = true;
        rippleMat.SetVector("_SphereCenter", point);
        rippleMat.SetColor("_FronColor", hitColor);   // your property is spelled FronColor

        for (float t = 0; t < 1f; t += Time.deltaTime / duration)
        {
            rippleMat.SetFloat("_SphereRadius", Mathf.Lerp(0f, maxRadius, t));
            yield return null;
        }
        ripple.enabled = false;
    }
}