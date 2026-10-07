using UnityEngine;

[CreateAssetMenu(fileName = "NewSpaceshipData", menuName = "CombatRoguelike/Spawner/Spaceship Data")]
public class SpaceshipData : ScriptableObject
{
    [Header("Ship Identity")]
    public string shipTypeName = "Medium Dropper";
    public GameObject shipPrefab;

    [Header("Payload Configuration")]
    public GameObject[] enemyPrefabs;
    [Range(1, 10)] public int payloadCapacity = 4;
    public float dropInterval = 0.5f;
    public float dropScatterRadius = 3f;

    [Header("Flight Timing & Speed")]
    [Tooltip("Duration in seconds of the approach phase.")]
    public float approachDuration = 2.0f;
    [Tooltip("Duration in seconds of the exit phase.")]
    public float exitDuration = 1.5f;
    public AnimationCurve approachCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve exitCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [Header("Stabilize Spring (Overshoot)")]
    public float springStiffness = 150f;
    public float springDamping = 10f;
    [Tooltip("How close the spring velocity must be to 0 to allow dropping.")]
    public float settleVelocityThreshold = 0.5f;

    [Header("Hover Visuals")]
    public float hoverAltitude = 8f;
    public float noiseBobAmplitude = 0.5f;
    public float noiseBobSpeed = 2f;
    public float maxBankAngle = 25f;
    public float maxPitchAngle = 15f;
    
    [Header("Exit Anticipation")]
    [Tooltip("Time spent dipping before accelerating away.")]
    public float anticipationDuration = 0.4f;
    public float anticipationDipAmount = 1.5f;
    public float anticipationPitch = -10f;
}