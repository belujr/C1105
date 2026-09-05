using UnityEngine;

[CreateAssetMenu(fileName = "MinionEnemyData", menuName = "Combat/Enemy Type Data/Minion Enemy Data")]
public class MinionEnemyDataSO : BaseEnemyDataSO
{
    [Header("Minion Pack Traits")]
    [SerializeField] private float circleRadius = 4.0f;
    [SerializeField] private float retreatDuration = 1.5f;

    public float CircleRadius => circleRadius;
    public float RetreatDuration => retreatDuration;
}