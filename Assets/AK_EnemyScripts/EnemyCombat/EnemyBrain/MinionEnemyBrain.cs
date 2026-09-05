using UnityEngine;

public class MinionEnemyBrain : BaseEnemyBrain
{
    private MinionEnemyDataSO MinionData => enemyData as MinionEnemyDataSO;

    protected override void HandleRequestTokenState(float distanceToTarget)
    {
        if (MinionData == null || MinionData.AvailableAttacks == null || MinionData.AvailableAttacks.Count == 0) 
            return;

        // Select a random attack from the minion's arsenal
        selectedAttack = MinionData.AvailableAttacks[Random.Range(0, MinionData.AvailableAttacks.Count)];

        if (GlobalTokenManager.Instance != null)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
            if (hasToken)
            {
                currentState = AIState.Attack;
                stateTimer = selectedAttack.StartupTime + selectedAttack.ActiveTime + selectedAttack.RecoveryTime;
            }
            else
            {
                // Tactical pack circling behavior when melee tokens are fully saturated
                Vector3 tangent = Vector3.Cross(Vector3.up, (target.position - transform.position)).normalized;
                characterController.Move(tangent * (MinionData.MoveSpeed * 0.7f) * Time.deltaTime);

                // Look toward target while circling
                Vector3 dir = (target.position - transform.position).normalized;
                dir.y = 0f;
                if (dir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, MinionData.RotationSpeed * Time.deltaTime);
                }
            }
        }
        else
        {
            currentState = AIState.Attack;
        }
    }
}