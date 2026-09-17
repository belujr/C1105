using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewEnemyDodgeProfile", menuName = "Combat System/Enemy Dodge Profile")]
public class EnemyDodgeProfileSO : ScriptableObject
{
    [Header("Default Dodge Settings")]
    public AnimationClip defaultDodgeClip;
    public float defaultTransitionDuration = 0.05f;
    public float defaultPlaybackSpeed = 1.0f;
    [Tooltip("How far the enemy physical slides/offsets when performing this dodge.")]
    public float defaultMoveDistance = 0.8f;
    [Tooltip("The local direction vector (e.g., -Vector3.forward for back, -Vector3.right for left, Vector3.right for right).")]
    public Vector3 defaultMoveDirection = -Vector3.forward;

    [Header("Attack-Specific Dodges")]
    public List<SpecificDodgeData> specificDodges = new List<SpecificDodgeData>();

    public SpecificDodgeData GetSpecificDodge(int attackID)
    {
        if (specificDodges == null) return null;
        foreach (var dodge in specificDodges)
        {
            if (dodge.attackID == attackID)
            {
                return dodge;
            }
        }
        return null;
    }
}

[System.Serializable]
public class SpecificDodgeData
{
    public string description = "Attack Name / ID";
    public int attackID;
    public AnimationClip dodgeClip;
    public float transitionDuration = 0.05f;
    public float playbackSpeed = 1.0f;
    
    [Header("Physical Offset Movement")]
    [Tooltip("How far the enemy slides during this specific dodge.")]
    public float moveDistance = 0.8f;
    [Tooltip("Direction relative to the enemy's facing: Back = -Vector3.forward, Left = -Vector3.right, Right = Vector3.right.")]
    public Vector3 moveDirection = -Vector3.forward;
}