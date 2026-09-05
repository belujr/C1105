using UnityEngine;

[CreateAssetMenu(fileName = "TankEnemyData", menuName = "Combat/Enemy Type Data/Tank Enemy Data")]
public class TankEnemyDataSO : BaseEnemyDataSO
{
    [Header("Tank Shock-Trooper Traits")]
    [SerializeField] private float superArmorThreshold = 30f;
    [SerializeField] private float groundSlamCooldown = 5.0f;

    public float SuperArmorThreshold => superArmorThreshold;
    public float GroundSlamCooldown => groundSlamCooldown;
}