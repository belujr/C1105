using UnityEngine;

public class TankEnemyBrain : BaseEnemyBrain
{
    private TankEnemyDataSO TankData => enemyData as TankEnemyDataSO;
    private float slamCooldownTimer = 0f;

    protected override void Update()
    {
        base.Update();
        if (slamCooldownTimer > 0f)
        {
            slamCooldownTimer -= Time.deltaTime;
        }
    }

    protected override void HandleRequestTokenState(float distanceToTarget)
    {
        if (TankData == null || TankData.AvailableAttacks == null || TankData.AvailableAttacks.Count == 0) 
            return;

        // Tanks prioritize AOE Ground Slam if off cooldown and available in arsenal (index 1), else standard attack (index 0)
        if (slamCooldownTimer <= 0f && TankData.AvailableAttacks.Count > 1 && TankData.AvailableAttacks[1] is AOEGroundSlamAttackSO)
        {
            selectedAttack = TankData.AvailableAttacks[1];
        }
        else
        {
            selectedAttack = TankData.AvailableAttacks[0];
        }

        if (GlobalTokenManager.Instance != null)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
            if (hasToken)
            {
                currentState = AIState.Attack;
                stateTimer = selectedAttack.StartupTime + selectedAttack.ActiveTime + selectedAttack.RecoveryTime;

                // Trigger cooldown if heavy slam was chosen
                if (selectedAttack is AOEGroundSlamAttackSO)
                {
                    slamCooldownTimer = TankData.GroundSlamCooldown;
                }
            }
            else
            {
                // Tanks ignore minor token constraints via Super-Armor, marching forward at reduced speed
                Vector3 dir = (target.position - transform.position).normalized;
                dir.y = 0f;
                characterController.Move(dir * (TankData.MoveSpeed * 0.4f) * Time.deltaTime);

                if (dir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, TankData.RotationSpeed * Time.deltaTime);
                }
            }
        }
        else
        {
            currentState = AIState.Attack;
        }
    }
}