using UnityEngine;

public class WaterInteractor : MonoBehaviour
{
    const int MaxRipples = 16;
    static readonly Vector4[] ripples = new Vector4[MaxRipples];   // x, z, spawnTime, strength
    static int head;
    static bool initialized;

    public float waterHeight = 0f;        // world Y of your water plane
    public float spawnInterval = 0.12f;   // seconds between ripples while moving
    public float minSpeed = 0.2f;
    public float maxSpeed = 6f;

    Vector3 lastPos;
    float nextSpawn;

    void OnEnable()
    {
        if (initialized) return;
        for (int i = 0; i < MaxRipples; i++) ripples[i] = new Vector4(0, 0, -1000f, 0);
        initialized = true;
    }

    void Update()
    {
        float speed = (transform.position - lastPos).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPos = transform.position;

        bool inWater = transform.position.y < waterHeight + 0.3f;   // tweak for your character
        if (inWater && speed > minSpeed && Time.time >= nextSpawn)
        {
            nextSpawn = Time.time + spawnInterval;
            float strength = Mathf.Clamp01(speed / maxSpeed);
            ripples[head] = new Vector4(transform.position.x, transform.position.z,
                                        Time.timeSinceLevelLoad, strength);
            head = (head + 1) % MaxRipples;
        }

        Shader.SetGlobalVectorArray("_Ripples", ripples);
    }
}