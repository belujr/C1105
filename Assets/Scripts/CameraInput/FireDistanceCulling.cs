using System.Collections.Generic;
using UnityEngine;

public class FireDistanceCulling : MonoBehaviour
{
    public float activeDistance = 45f;   // fire is on inside this distance
    public float hysteresis = 5f;        // avoids flicker at the border
    public float checkInterval = 0.25f;

    class Entry
    {
        public FireVFX fire;
        public bool active = true;
    }

    readonly List<Entry> entries = new List<Entry>();
    float timer;

    void Start()
    {
        foreach (var f in FindObjectsByType<FireVFX>(FindObjectsSortMode.None))
            entries.Add(new Entry { fire = f });
    }

    void Update()
    {
        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = checkInterval;

        Transform center = IsoCameraRig.Instance != null ? IsoCameraRig.Instance.Target : null;
        if (center == null) return;

        foreach (var e in entries)
        {
            if (e.fire == null) continue;

            float d = Vector3.Distance(center.position, e.fire.transform.position);
            bool want = e.active ? d < activeDistance + hysteresis : d < activeDistance;

            if (want != e.active)
            {
                e.active = want;
                SetState(e.fire, want);
            }
        }
    }

    static void SetState(FireVFX fire, bool on)
    {
        foreach (var l in fire.GetComponentsInChildren<Light>(true)) l.enabled = on;
        foreach (var ps in fire.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (on) ps.Play();
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}