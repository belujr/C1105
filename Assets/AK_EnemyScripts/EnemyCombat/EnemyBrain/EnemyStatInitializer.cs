using UnityEngine;
using CombatSystem.Data;
using System.Collections;

[RequireComponent(typeof(DummyHealth))]
public class EnemyStatInitializer : MonoBehaviour
{
    [Header("Data Asset Reference (Inspector Driven)")]
    [SerializeField] private BaseEnemyDataSO enemyData;

    private DummyHealth dummyHealth;
    private CharacterController characterController;
    private BaseEnemyBrain enemyBrain;

    private void Awake()
    {
        dummyHealth = GetComponent<DummyHealth>();
        characterController = GetComponent<CharacterController>();
        enemyBrain = GetComponent<BaseEnemyBrain>();
    }

    private void OnEnable()
    {
        StopAllCoroutines();
        StartCoroutine(DelayedResetAndInitializeRoutine());
    }

    public void Initialize()
    {
        StopAllCoroutines();
        StartCoroutine(DelayedResetAndInitializeRoutine());
    }

    private IEnumerator DelayedResetAndInitializeRoutine()
    {
        // Wait one frame so studio components (DummyHealth) finish their startup first,
        // then we forcefully override and revive them!
        yield return null; 

        if (enemyData == null) yield break;

        if (characterController != null)
        {
            characterController.enabled = true;
        }

        if (enemyBrain != null)
        {
            enemyBrain.enabled = true;
            enemyBrain.ResetBrain(); 
        }

        if (dummyHealth != null)
        {
            dummyHealth.enabled = true;

            var type = dummyHealth.GetType();

            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                string fName = field.Name.ToLower();
                if (fName.Contains("health") || fName.Contains("hp"))
                {
                    if (field.FieldType == typeof(float))
                        field.SetValue(dummyHealth, enemyData.MaxHealth);
                    else if (field.FieldType == typeof(int))
                        field.SetValue(dummyHealth, Mathf.RoundToInt(enemyData.MaxHealth));
                }
                else if (fName.Contains("dead") || fName.Contains("death"))
                {
                    if (field.FieldType == typeof(bool))
                        field.SetValue(dummyHealth, false);
                }
            }

            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.CanWrite)
                {
                    string pName = prop.Name.ToLower();
                    if (pName.Contains("health") || pName.Contains("hp"))
                    {
                        if (prop.PropertyType == typeof(float))
                        {
                            try { prop.SetValue(dummyHealth, enemyData.MaxHealth); } catch {}
                        }
                        else if (prop.PropertyType == typeof(int))
                        {
                            try { prop.SetValue(dummyHealth, Mathf.RoundToInt(enemyData.MaxHealth)); } catch {}
                        }
                    }
                    else if (pName.Contains("dead"))
                    {
                        if (prop.PropertyType == typeof(bool))
                        {
                            try { prop.SetValue(dummyHealth, false); } catch {}
                        }
                    }
                }
            }

            var methods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var method in methods)
            {
                string mName = method.Name.ToLower();
                if ((mName.Contains("revive") || mName.Contains("reset") || mName.Contains("respawn") || mName.Contains("init") || mName.Contains("heal")) 
                    && method.GetParameters().Length == 0)
                {
                    try { method.Invoke(dummyHealth, null); } catch {}
                }
            }
        }
    }

    public BaseEnemyDataSO GetAssignedData() => enemyData;
}