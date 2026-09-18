using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillPreviewManager : MonoBehaviour
{
    public static SkillPreviewManager Instance;

    [Header("Text References")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI descriptionText;

    [Header("Pip Sprites (Assign 5 Sprites in Order: 1-Pip to 5-Pips)")]
    public Sprite[] pipSprites = new Sprite[5]; 

    [Header("UI Bar Images")]
    public Image damageBarImage;
    public Image knockbackBarImage;
    public Image rangeBarImage;

    [Header("Player Dummy References")]
    public Animator dummyAnimator;
    public Transform dummySpawnPoint;

    [Header("Enemy Dummy References")]
    public Transform enemyDummy;
    public Transform enemySpawnPoint;
    public Animator enemyAnimator;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void OnEnable()
    {
        // 1. Disable Root Motion so the animation itself cannot move the transform
        if (dummyAnimator != null)
        {
            dummyAnimator.applyRootMotion = false;
        }

        // 2. Snap the player dummy initially when the shop UI is opened
        if (dummyAnimator != null && dummySpawnPoint != null)
        {
            TeleportCharacter(dummyAnimator.transform, dummySpawnPoint);
        }

        // 3. Snap the enemy dummy
        ResetEnemyTransform();
    }

    // NEW: Hard-lock the dummy to the spawn point every frame
    private void LateUpdate()
    {
        if (dummyAnimator != null && dummySpawnPoint != null)
        {
            // This completely overrides any Physics, CharacterController, or Input 
            // that tries to move the dummy during the frame.
            dummyAnimator.transform.position = dummySpawnPoint.position;
            dummyAnimator.transform.rotation = dummySpawnPoint.rotation;
        }
    }

    public void ShowPreview(AttackData attack)
    {
        if (attack == null) return;

        if (titleText != null) 
            titleText.text = string.IsNullOrEmpty(attack.attackName) ? attack.name : attack.attackName;
            
        if (descriptionText != null) 
            descriptionText.text = attack.description;

        UpdateStatSprite(damageBarImage, attack.damageLevel);
        UpdateStatSprite(knockbackBarImage, attack.knockbackLevel);
        UpdateStatSprite(rangeBarImage, attack.rangeLevel);

        // Reset the Enemy ONLY so it doesn't get pushed out of bounds permanently
        ResetEnemyTransform();

        if (dummyAnimator != null)
        {
            if (dummyAnimator.runtimeAnimatorController == null) return;
            
            dummyAnimator.enabled = true;

            if (!string.IsNullOrEmpty(attack.animationTriggerName))
            {
                int stateHash = Animator.StringToHash(attack.animationTriggerName);

                if (dummyAnimator.HasState(0, stateHash))
                {
                    dummyAnimator.Play(stateHash, 0, 0f);
                    dummyAnimator.Update(0f);
                }
            }
        }
    }

    private void ResetEnemyTransform()
    {
        if (enemyDummy != null && enemySpawnPoint != null)
        {
            TeleportCharacter(enemyDummy, enemySpawnPoint);

            if (enemyAnimator != null)
            {
                enemyAnimator.Play(0, 0, 0f);
                enemyAnimator.Update(0f);
            }
        }
    }

    private void TeleportCharacter(Transform target, Transform destination)
    {
        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Rigidbody rb = target.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        target.position = destination.position;
        target.rotation = destination.rotation;

        if (cc != null) cc.enabled = true;
    }

    private void UpdateStatSprite(Image targetImage, int statLevel)
    {
        if (targetImage == null || pipSprites == null || pipSprites.Length < 5) return;

        int spriteIndex = Mathf.Clamp(statLevel - 1, 0, pipSprites.Length - 1);
        if (pipSprites[spriteIndex] != null)
        {
            targetImage.sprite = pipSprites[spriteIndex];
        }
    }
}