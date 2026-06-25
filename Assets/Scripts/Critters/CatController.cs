using System.Collections;
using UnityEngine;

public class CatController : MonoBehaviour
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
    private Coroutine wanderRoutine;
    private bool isSummoned;

    /// <summary>True while Taro has been called over to drink (his free wandering is paused).</summary>
    public bool IsSummoned => isSummoned;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (startPosition == Vector3.zero)
            startPosition = transform.position;
        wanderRoutine = StartCoroutine(WanderRoutine());
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
        animator.SetBool("isWalkingCat", true);
        while (Vector3.Distance(transform.position, target) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = target;
        isWalking = false;
        animator.SetBool("isWalkingCat", false);
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

    // Call this from anywhere to trigger the drink animation
    public void TriggerDrink()
    {
        if (!isWalking)
        {
            animator.SetTrigger("drink");
        }
    }

    /// <summary>
    /// Calls Taro over to the glass to drink: pauses his wandering, walks him to a point a little back from
    /// <paramref name="glassWorldPos"/>, turns him to face it, plays the drink clip, holds for
    /// <paramref name="drinkHoldSeconds"/>, then resumes his free wander. <paramref name="onDrinkStart"/>
    /// fires the moment the drink is triggered (so the caller can time the glass settling / effect text to it).
    /// Driven by the Taro item's use performance (see ItemUsePresentationController).
    /// </summary>
    public void SummonToDrink(Vector3 glassWorldPos, float standoff, float drinkHoldSeconds, System.Action onDrinkStart = null)
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Halt the wander (and any in-flight walk) outright so two coroutines never fight over the transform.
        StopAllCoroutines();
        isWalking = false;
        StartCoroutine(SummonRoutine(glassWorldPos, standoff, drinkHoldSeconds, onDrinkStart));
    }

    IEnumerator SummonRoutine(Vector3 glassWorldPos, float standoff, float drinkHoldSeconds, System.Action onDrinkStart)
    {
        isSummoned = true;
        isWalking = true;

        // Stand a little back from the glass on whichever side Taro is already on, at his own (floor) height.
        Vector3 fromGlass = transform.position - glassWorldPos;
        fromGlass.y = 0f;
        if (fromGlass.sqrMagnitude < 1e-4f)
            fromGlass = -transform.forward;

        Vector3 stand = glassWorldPos + fromGlass.normalized * Mathf.Max(0.01f, standoff);
        stand.y = transform.position.y;

        // Turn toward the spot, walk there, then face the glass to drink.
        yield return RotateTowards(stand - transform.position);

        animator.SetBool("isWalkingCat", true);
        while (Vector3.Distance(transform.position, stand) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, stand, walkSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = stand;
        animator.SetBool("isWalkingCat", false);

        yield return RotateTowards(glassWorldPos - transform.position);

        // The drink trigger only transitions from Cat_Idle, so clearing isWalkingCat above lets it fire; the
        // trigger is sticky, so it still plays even if Walk→Idle takes a frame.
        animator.SetTrigger("drink");
        onDrinkStart?.Invoke();

        yield return new WaitForSeconds(drinkHoldSeconds);

        isWalking = false;
        isSummoned = false;
        wanderRoutine = StartCoroutine(WanderRoutine());
    }

    IEnumerator RotateTowards(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 1e-5f)
            yield break;

        Quaternion target = Quaternion.LookRotation(direction.normalized);
        while (Quaternion.Angle(transform.rotation, target) > 0.5f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            yield return null;
        }
        transform.rotation = target;
    }
}
