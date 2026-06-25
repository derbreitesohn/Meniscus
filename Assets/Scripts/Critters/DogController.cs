using System.Collections;
using UnityEngine;

public class DogController : MonoBehaviour
{
    [Header("Wandering Settings")]
    [Tooltip("How far the dog is allowed to stray from its starting position")]
    public float maxWanderRadius = 6f;
    [Tooltip("How far the dog moves per walk")]
    public float walkDistance = 2f;
    [Tooltip("How fast the dog walks")]
    public float walkSpeed = 1.2f;
    [Tooltip("How fast the dog turns before walking, in degrees per second")]
    public float turnSpeed = 150f;

    [Header("Timing Settings")]
    [Tooltip("Minimum seconds between walks")]
    public float minIdleTime = 2f;
    [Tooltip("Maximum seconds between walks")]
    public float maxIdleTime = 5f;

    [Header("Wander Area")]
    public Vector3 areaCenterOffset = Vector3.zero;
    public Vector3 areaSize = new Vector3(2f, 0f, 2f);

    private Animator animator;
    public Vector3 startPosition;
    private bool isWalking = false;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (startPosition == Vector3.zero)
            startPosition = transform.position;
        StartCoroutine(WanderRoutine());
    }

    IEnumerator WanderRoutine()
    {
        while (true)
        {
            // Wait a random idle duration before walking
            float idleTime = Random.Range(minIdleTime, maxIdleTime);
            yield return new WaitForSeconds(idleTime);

            // Pick a random direction and compute a candidate target
            Vector3 randomDirection = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            Vector3 targetPosition = transform.position + randomDirection * walkDistance;

            // Clamp the target within the bounding box
            Vector3 center = startPosition + areaCenterOffset;
            targetPosition.x = Mathf.Clamp(targetPosition.x, center.x - areaSize.x / 2f, center.x + areaSize.x / 2f);
            targetPosition.z = Mathf.Clamp(targetPosition.z, center.z - areaSize.z / 2f, center.z + areaSize.z / 2f);

            yield return StartCoroutine(WalkTo(targetPosition));
        }
    }

    IEnumerator WalkTo(Vector3 target)
    {
        isWalking = true;

        // Phase 1: rotate to face the target before walking
        Vector3 direction = (target - transform.position).normalized;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            while (Quaternion.Angle(transform.rotation, targetRotation) > 0.5f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        // Phase 2: walk forward toward the target
        animator.SetBool("isWalkingDog", true);
        while (Vector3.Distance(transform.position, target) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = target;
        isWalking = false;
        animator.SetBool("isWalkingDog", false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = (Application.isPlaying ? startPosition : transform.position) + areaCenterOffset;
        Gizmos.DrawWireCube(center, new Vector3(areaSize.x, 0.1f, areaSize.z));
    }

    // Call this from anywhere to trigger the steal animation
    public void TriggerSteal()
    {
        if (!isWalking)
        {
            animator.SetTrigger("steal");
        }
    }
}