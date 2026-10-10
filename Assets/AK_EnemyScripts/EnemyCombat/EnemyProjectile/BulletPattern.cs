using UnityEngine;

public enum FireMode { Single, Aimed, Fan, Ring, Spiral, Wave, Scatter, Salvo }

public class BulletPattern : MonoBehaviour
{
    [Header("Emitters")]
    public FireMode mode = FireMode.Single;
    public int count = 1;
    public float arc = 0f;
    public float angleOffset = 0f;
    public float aimLead = 0f;
    public float jitter = 0f;
    public int seed = 0;
    
    [Header("Waves")]
    public int waves = 1;
    public float interval = 0.1f;
    public float perWaveRotationDelta = 0f;
    public AnimationCurve perWaveCountCurve;
    public AnimationCurve perWaveArcCurve;

    [Header("Kinematics & Payload")]
    public float bulletSpeed = 10f;
    public AnimationCurve speedCurve;
    public float acceleration = 0f;
    public float lifetime = 5f;
    public float hitRadius = 0.5f;
    public Vector3 scale = Vector3.one;
    public float damage = 10f;
    public float knockback = 0f;
    public int attackID = 0;
    public string playerReactionName = "Hit_Light";
    public float stun = 0.2f;

    [Header("Modifiers")]
    public float homingStrength = 0f;
    public float homingDelay = 0f;
    public float homingDuration = 0f;
    public float homingTurnCap = 180f;
    public int bounce = 0;
    public int pierce = 0;
    public float sineWobble = 0f;

    [Header("Staging & Execution")]
    public BulletPattern stageOnSpawn;
    public BulletPattern stageAfterTime;
    public float stageTime = 0f;
    public BulletPattern stageOnExpire;
    public BulletPattern stageOnWallHit;
    public float castTime = 0.5f;
    public bool lockAimDuringTelegraph = true;
    public int safetyCap = 200;

#if UNITY_EDITOR
    private void OnValidate()
    {
        count = Mathf.Max(1, count);
        waves = Mathf.Max(1, waves);
        interval = Mathf.Max(0f, interval);
        safetyCap = Mathf.Max(1, safetyCap);
        hitRadius = Mathf.Max(0.05f, hitRadius);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 fwd = transform.forward;

        float startAngle = count > 1 ? -arc * 0.5f + angleOffset : angleOffset;
        float step = count > 1 ? arc / (count - 1) : 0f;

        // Calculate approximate physical gap between bullets at a 3-meter dodge distance
        float arcLengthAt3m = arc > 0 ? (arc * Mathf.Deg2Rad * 3f) : 0f;
        float gapAt3m = count > 1 ? (arcLengthAt3m / (count - 1)) - (hitRadius * 2f) : 99f;
        bool isUndodgeable = count > 1 && gapAt3m < 0.6f; // Assuming 0.6m is the minimum player width

        Gizmos.color = isUndodgeable ? new Color(1f, 0f, 0f, 0.8f) : new Color(0.2f, 1f, 0.2f, 0.6f);
        
        for (int w = 0; w < waves; w++)
        {
            float waveRot = w * perWaveRotationDelta;
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + (step * i) + waveRot;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * fwd;
                Gizmos.DrawRay(origin, dir * 3f);
                Gizmos.DrawWireSphere(origin + dir * 3f, hitRadius);
            }
        }

        UnityEditor.Handles.Label(origin + Vector3.up * 1.5f, 
            $"Total Bullets: {count * waves}\nDodge Gap @ 3m: {gapAt3m:F2}m\n{(isUndodgeable ? "WARNING: UNDODGEABLE" : "Fair")}");
    }
#endif
}