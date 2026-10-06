using System.Collections;
using UnityEngine;
using CombatSystem.Animation;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(EnemyAnimationEngine))]
public class EnemyShipPassenger : MonoBehaviour
{
    [Header("Flight & Landing Animations")]
    public AnimationClip hangingClip;
    public AnimationClip landingClip;
    
    public float gravity = 25f;
    public float maxFallSpeed = 40f;

    public LayerMask groundMask = ~0;
    
    public float landingDuration = 1.0f;
    public MonoBehaviour enemyBrain;

    private CharacterController charController;
    private EnemyAnimationEngine animEngine;
    private Coroutine landRoutine;
    private Vector3 originalScale = Vector3.one;
    private PlayerController cachedPlayer;

    private void Awake()
    {
        charController = GetComponent<CharacterController>();
        animEngine = GetComponent<EnemyAnimationEngine>();
        
        // Cache the pristine prefab scale to prevent tiny spawns from ship parent distortion
        if (transform.localScale.sqrMagnitude > 0.01f)
        {
            originalScale = transform.localScale;
        }
    }

    private void OnDisable()
    {
        if (landRoutine != null) { StopCoroutine(landRoutine); landRoutine = null; }
        transform.SetParent(null);
    }

    public void MountToShip(Transform mountPoint)
    {
        if (landRoutine != null) { StopCoroutine(landRoutine); landRoutine = null; }

        if (enemyBrain != null) enemyBrain.enabled = false;
        if (charController != null) charController.enabled = false;

        transform.SetParent(mountPoint);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = originalScale; // Force correct scale while hanging

        if (animEngine != null)
        {
            animEngine.enabled = true; // Keep active to prevent frozen poses
            if (hangingClip != null) animEngine.PlayAnimation(hangingClip, 0.1f, 1.0f);
        }
    }

    public void DetachAndLand()
    {
        if (landRoutine != null) return;
        landRoutine = StartCoroutine(LandRoutine());
    }

   private IEnumerator LandRoutine()
{
    transform.SetParent(null);
    transform.localScale = originalScale;

    if (cachedPlayer == null) cachedPlayer = FindFirstObjectByType<PlayerController>();
    if (cachedPlayer != null)
    {
        Vector3 dir = cachedPlayer.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir.normalized);
    }

    // Controller stays OFF during the fall: only the ground can stop it
    if (charController != null) charController.enabled = false;

    float feetOffset = GetFeetOffset();
    float velocity = 0f;
    float timer = 0f;
    bool landed = false;

    while (timer < 6f)
    {
        float dt = Time.deltaTime;
        timer += dt;
        velocity = Mathf.Min(velocity + gravity * dt, maxFallSpeed);
        float step = velocity * dt;

        Vector3 pos = transform.position;
        Vector3 origin = new Vector3(pos.x, pos.y - feetOffset + 0.3f, pos.z);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.3f + step, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = hit.point.y + feetOffset;
            transform.position = pos;
            landed = true;
            break;
        }

        pos.y -= step;
        transform.position = pos;
        yield return null;
    }

    if (!landed) SnapToGroundFromAbove(feetOffset);

    if (charController != null)
    {
        charController.enabled = true;
        charController.detectCollisions = true;
    }

    if (animEngine != null && landingClip != null) animEngine.PlayAnimation(landingClip, 0.05f, 1.0f);

    yield return new WaitForSeconds(landingDuration);

    if (enemyBrain != null)
    {
        enemyBrain.SendMessage("AuthorizeGroundContactAndEnable", SendMessageOptions.DontRequireReceiver);
        enemyBrain.enabled = true;
    }
    landRoutine = null;
}

private float GetFeetOffset()
{
    if (charController == null) return 0f;
    return (charController.height * 0.5f - charController.center.y) * Mathf.Abs(transform.lossyScale.y);
}

private void SnapToGroundFromAbove(float feetOffset)
{
    Vector3 pos = transform.position;
    if (Physics.Raycast(pos + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 300f, groundMask, QueryTriggerInteraction.Ignore))
    {
        pos.y = hit.point.y + feetOffset;
        transform.position = pos;
    }
}
}