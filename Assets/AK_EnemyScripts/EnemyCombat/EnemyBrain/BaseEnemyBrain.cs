using UnityEngine;
using System.Collections;
using CombatSystem.Data;

[RequireComponent(typeof(CharacterController))]
public abstract class BaseEnemyBrain : MonoBehaviour
{
    [Header("Data Configuration")]
    [SerializeField] protected BaseEnemyDataSO enemyData;

    protected Transform target;
    protected CharacterController characterController;
    protected Component animationEngine; 
    protected Component dummyController; 
    protected DummyHealth dummyHealth;

    protected enum AIState { Idle, Chase, RequestToken, Attack, Retreat, Dead }
    protected AIState currentState = AIState.Idle;
    private AIState lastState = (AIState)(-1); 

    protected BaseAttackDataSO selectedAttack;
    protected float stateTimer = 0f;
    protected float tokenWaitTimer = 0f; 
    protected bool hasToken = false;
    protected bool isDead = false;

    protected int currentStrafeDir = 1;
    protected float strafeTimer = 0f;

    private float verticalVelocity = 0f;
    private float gravity = -20f;

    protected virtual void Awake()
    {
        characterController = GetComponent<CharacterController>();
        animationEngine = GetComponent("EnemyAnimationEngine");
        dummyController = GetComponent("EnemyDummyController");
        dummyHealth = GetComponent<DummyHealth>();
        
        if (animationEngine == null)
        {
            Debug.LogError($"[BaseEnemyBrain] CRITICAL: 'EnemyAnimationEngine' component is missing from prefab {gameObject.name}!", this);
        }
    }

    protected virtual void Start()
    {
        if (enemyData == null)
        {
            Debug.LogError($"[BaseEnemyBrain] {gameObject.name} has no assigned BaseEnemyDataSO!", this);
            enabled = false;
            return;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            target = playerObj.transform;
        }
    }

    protected virtual void Update()
    {
        if (isDead || target == null) return;

        if (CheckIfStudioDummyIsDead())
        {
            HandleDeath();
            return;
        }

        ApplyGravity();
        ExecuteStateMachine();

        if (currentState != lastState)
        {
            OnStateEnter(currentState);
            lastState = currentState;
        }
    }

    private bool CheckIfStudioDummyIsDead()
    {
        if (dummyHealth == null) return false;

        var type = dummyHealth.GetType();
        
        var isDeadProp = type.GetProperty("IsDead") ?? type.GetProperty("IsDestroyed") ?? type.GetProperty("IsDeadOrInactive");
        if (isDeadProp != null && isDeadProp.GetValue(dummyHealth) is bool deadVal && deadVal) return true;

        var healthProp = type.GetProperty("CurrentHealth") ?? type.GetProperty("Health") ?? type.GetProperty("currentHealth");
        if (healthProp != null)
        {
            object val = healthProp.GetValue(dummyHealth);
            if (val is float f && f <= 0f) return true;
            if (val is int i && i <= 0) return true;
        }

        return false;
    }

    private void ApplyGravity()
    {
        if (characterController.isGrounded)
        {
            verticalVelocity = -2f; 
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
    }

    protected virtual void OnStateEnter(AIState newState)
    {
        currentStrafeDir = Random.value > 0.5f ? 1 : -1;

        if (dummyController != null)
        {
            var enabledProp = dummyController.GetType().GetProperty("enabled");
            if (enabledProp != null)
            {
                enabledProp.SetValue(dummyController, newState != AIState.Attack);
            }
        }

        switch (newState)
        {
            case AIState.Idle:
                PlayAnimation("Idle", 0.15f);
                break;
            case AIState.Chase:
                PlayAnimation("Walk", 0.15f); 
                break;
            case AIState.Retreat:
                string retreatStrafeKey = currentStrafeDir > 0 ? "StrafeRight" : "StrafeLeft";
                PlayAnimation(retreatStrafeKey, 0.15f);
                break;
            case AIState.Attack:
                if (selectedAttack != null)
                {
                    PlayAnimation(selectedAttack.AnimationClipName, 0.1f);
                }
                break;
        }
    }

    protected virtual void ExecuteStateMachine()
    {
        float distanceToTarget = Vector3.Distance(transform.position, target.position);

        switch (currentState)
        {
            case AIState.Idle:
                HandleIdleState(distanceToTarget);
                break;
            case AIState.Chase:
                HandleChaseState(distanceToTarget);
                break;
            case AIState.RequestToken:
                HandleRequestTokenState(distanceToTarget);
                break;
            case AIState.Attack:
                HandleAttackState();
                break;
            case AIState.Retreat:
                HandleRetreatState();
                break;
        }
    }

    protected virtual void HandleIdleState(float distanceToTarget)
    {
        Vector3 toTargetDir = (target.position - transform.position).normalized;
        toTargetDir.y = 0f;
        RotateInstant(toTargetDir);

        if (distanceToTarget <= enemyData.DetectionRange)
        {
            currentState = AIState.Chase;
        }
    }

    protected virtual void HandleChaseState(float distanceToTarget)
    {
        if (distanceToTarget <= enemyData.AttackStopDistance)
        {
            currentState = AIState.RequestToken;
            tokenWaitTimer = 0f; 
        }
        else
        {
            float currentSpeed = enemyData.MoveSpeed > 0f ? enemyData.MoveSpeed : 3.5f;

            Vector3 dir = (target.position - transform.position).normalized;
            dir.y = 0f;

            Vector3 totalMovement = (dir * currentSpeed) + (Vector3.up * verticalVelocity);
            characterController.Move(totalMovement * Time.deltaTime);

            RotateInstant(dir);
        }
    }

    protected virtual void HandleRequestTokenState(float distanceToTarget)
    {
        if (enemyData.AvailableAttacks == null || enemyData.AvailableAttacks.Count == 0) return;

        selectedAttack = enemyData.AvailableAttacks[Random.Range(0, enemyData.AvailableAttacks.Count)];
        tokenWaitTimer += Time.deltaTime;

        bool forceAttack = tokenWaitTimer >= 2.5f; 

        if (GlobalTokenManager.Instance != null && !forceAttack)
        {
            hasToken = GlobalTokenManager.Instance.RequestToken(transform, selectedAttack.RequiredTokenType);
        }
        else
        {
            hasToken = true; 
        }

        Vector3 toTargetDir = (target.position - transform.position).normalized;
        toTargetDir.y = 0f;
        RotateInstant(toTargetDir);

        if (hasToken)
        {
            tokenWaitTimer = 0f;
            currentState = AIState.Attack;
            stateTimer = selectedAttack.StartupTime + selectedAttack.ActiveTime + selectedAttack.RecoveryTime;
        }
        else
        {
            strafeTimer += Time.deltaTime;
            if (strafeTimer > 4.0f) 
            {
                currentStrafeDir *= -1;
                strafeTimer = 0f;
                OnStateEnter(currentState); 
            }

            string circleStrafeKey = currentStrafeDir > 0 ? "StrafeRight" : "StrafeLeft";
            PlayAnimation(circleStrafeKey, 0.15f);

            float currentSpeed = enemyData.MoveSpeed > 0f ? enemyData.MoveSpeed : 3.5f;
            Vector3 tangent = Vector3.Cross(Vector3.up, toTargetDir).normalized * currentStrafeDir;
            Vector3 totalMovement = (tangent * (currentSpeed * 0.6f)) + (Vector3.up * verticalVelocity);
            
            characterController.Move(totalMovement * Time.deltaTime);
        }
    }

    protected virtual void HandleAttackState()
    {
        stateTimer -= Time.deltaTime;
        characterController.Move(Vector3.up * verticalVelocity * Time.deltaTime);

        Vector3 toTargetDir = (target.position - transform.position).normalized;
        toTargetDir.y = 0f;
        RotateInstant(toTargetDir);

        float activeTriggerThreshold = selectedAttack.RecoveryTime + selectedAttack.ActiveTime;
        if (stateTimer <= activeTriggerThreshold && stateTimer > selectedAttack.RecoveryTime)
        {
            if (selectedAttack != null && target != null)
            {
                selectedAttack.ExecuteAttackPayload(transform, target);
            }
        }

        if (stateTimer <= 0f)
        {
            if (GlobalTokenManager.Instance != null && selectedAttack != null)
            {
                GlobalTokenManager.Instance.ReleaseToken(transform, selectedAttack.RequiredTokenType);
                hasToken = false;
            }

            currentState = AIState.Retreat;
            stateTimer = 1.2f; 
        }
    }

    protected virtual void HandleRetreatState()
    {
        stateTimer -= Time.deltaTime;

        float currentSpeed = enemyData.MoveSpeed > 0f ? enemyData.MoveSpeed : 3.5f;
        Vector3 toTargetDir = (target.position - transform.position).normalized;
        toTargetDir.y = 0f;

        Vector3 awayDir = -toTargetDir;
        Vector3 tangent = Vector3.Cross(Vector3.up, toTargetDir).normalized * currentStrafeDir;
        Vector3 retreatMove = (awayDir * 0.5f + tangent * 0.7f).normalized;

        Vector3 totalMovement = (retreatMove * (currentSpeed * 0.7f)) + (Vector3.up * verticalVelocity);
        characterController.Move(totalMovement * Time.deltaTime);

        RotateInstant(toTargetDir);

        if (stateTimer <= 0f)
        {
            currentState = AIState.Chase;
        }
    }

    protected void RotateInstant(Vector3 lookDir)
    {
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(lookDir);
        }
    }

    protected void PlayAnimation(string animKey, float crossfadeDuration)
    {
        if (animationEngine == null || enemyData == null || enemyData.AnimationProfile == null) return;

        AnimationClip clip = enemyData.AnimationProfile.GetAnimationClip(animKey);
        if (clip == null) return;

        var method = animationEngine.GetType().GetMethod("PlayAnimation") ?? 
                     animationEngine.GetType().GetMethod("Play") ??
                     animationEngine.GetType().GetMethod("CrossFade") ??
                     animationEngine.GetType().GetMethod("Crossfade");

        if (method != null)
        {
            var parameters = method.GetParameters();
            object[] args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var pType = parameters[i].ParameterType;
                string pName = parameters[i].Name.ToLower();

                if (pType == typeof(AnimationClip))
                {
                    args[i] = clip;
                }
                else if (pType == typeof(string))
                {
                    args[i] = animKey;
                }
                else if (pType == typeof(float))
                {
                    if (pName.Contains("speed") || pName.Contains("rate") || pName.Contains("multiplier"))
                    {
                        args[i] = 1.0f; 
                    }
                    else
                    {
                        args[i] = crossfadeDuration;
                    }
                }
                else if (pType.IsValueType)
                {
                    args[i] = System.Activator.CreateInstance(pType);
                }
                else
                {
                    args[i] = null;
                }
            }

            try
            {
                method.Invoke(animationEngine, args);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BaseEnemyBrain] Failed to invoke animation method '{method.Name}': {ex.Message}", this);
            }
        }
    }

    public virtual void HandleDeath()
    {
        if (isDead) return;
        isDead = true;
        currentState = AIState.Dead;

        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);
        }

        enabled = false;
        if (characterController != null) characterController.enabled = false;
        if (dummyController != null)
        {
            var comp = dummyController as Behaviour;
            if (comp != null) comp.enabled = false;
        }

        // Disable Gravity logic to prevent CharacterController.Move exceptions
        var customGravity = GetComponent("CustomEnemyGravity") as Behaviour;
        if (customGravity != null) customGravity.enabled = false;

        StartCoroutine(RecycleToPoolAfterDelay(1.5f));
    }

    private IEnumerator RecycleToPoolAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (EnemyObjectPool.Instance != null)
        {
            var poolMethod = EnemyObjectPool.Instance.GetType().GetMethod("ReturnToPool") ??
                             EnemyObjectPool.Instance.GetType().GetMethod("Despawn") ??
                             EnemyObjectPool.Instance.GetType().GetMethod("PoolObject");

            if (poolMethod != null)
            {
                poolMethod.Invoke(EnemyObjectPool.Instance, new object[] { gameObject });
                yield break;
            }
        }

        gameObject.SetActive(false);
    }

    public virtual void ResetBrain()
    {
        isDead = false;
        enabled = true;
        currentState = AIState.Idle;
        lastState = (AIState)(-1);
        stateTimer = 0f;
        tokenWaitTimer = 0f;

        // Restore Gravity logic for the pooled instance
        var customGravity = GetComponent("CustomEnemyGravity") as Behaviour;
        if (customGravity != null) customGravity.enabled = true;
    }

    protected virtual void OnDisable()
    {
        if (GlobalTokenManager.Instance != null)
        {
            GlobalTokenManager.Instance.ReleaseAllTokensForEnemy(transform);
        }
        hasToken = false;
        isDead = false;
    }
}