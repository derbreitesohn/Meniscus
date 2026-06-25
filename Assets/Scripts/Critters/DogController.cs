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

    [Header("Audio")]
    public AK.Wwise.Event playBrummen;
    public AK.Wwise.Event playBite;

    private Animator animator;
    public Vector3 startPosition;
    private bool isWalking = false;
    private Coroutine wanderRoutine;
    private bool isSummoned;

    /// <summary>True while the dog has been called over to harass the dealer (its free wandering is paused).</summary>
    public bool IsSummoned => isSummoned;

    void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (startPosition == Vector3.zero)
            startPosition = transform.position;
        wanderRoutine = StartCoroutine(WanderRoutine());

        // The dog growls/brummt when it arrives (item selected).
        playBrummen?.Post(gameObject);
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
            playBite?.Post(gameObject);
        }
    }

    /// <summary>
    /// Calls the dog over to harass the dealer: pauses wandering, walks to a point a little back from
    /// <paramref name="targetWorldPos"/> (the dealer's side of the table), turns to face it, and plays its
    /// aggressive clip, holds for <paramref name="holdSeconds"/>, then resumes wandering.
    /// <paramref name="onArrive"/> fires the moment the clip is triggered (so the caller can time the text /
    /// camera shake to it). NOTE: the Dog_Animator has no bark clip, so this fires the `steal` trigger
    /// (Dog_Steal) as the stand-in for the bark — swap to a `bark` trigger here once a bark clip is added.
    /// Driven by the whistle (Pfeifi) item's use performance (see ItemUsePresentationController).
    /// </summary>
    public void SummonToHarass(Vector3 targetWorldPos, float standoff, float holdSeconds, System.Action onArrive = null)
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        // Halt the wander (and any in-flight walk) outright so two coroutines never fight over the transform.
        StopAllCoroutines();
        isWalking = false;
        StartCoroutine(HarassRoutine(targetWorldPos, standoff, holdSeconds, onArrive));
    }

    IEnumerator HarassRoutine(Vector3 targetWorldPos, float standoff, float holdSeconds, System.Action onArrive)
    {
        isSummoned = true;
        isWalking = true;

        // Stand a little back from the dealer's side on whichever side the dog is already on, at its (floor) height.
        Vector3 fromTarget = transform.position - targetWorldPos;
        fromTarget.y = 0f;
        if (fromTarget.sqrMagnitude < 1e-4f)
            fromTarget = -transform.forward;

        Vector3 stand = targetWorldPos + fromTarget.normalized * Mathf.Max(0.01f, standoff);
        stand.y = transform.position.y;

        yield return RotateTowards(stand - transform.position);

        animator.SetBool("isWalkingDog", true);
        while (Vector3.Distance(transform.position, stand) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, stand, walkSpeed * Time.deltaTime);
            yield return null;
        }
        transform.position = stand;
        animator.SetBool("isWalkingDog", false);

        yield return RotateTowards(targetWorldPos - transform.position);

        // No bark clip exists — the steal trigger (Dog_Steal) stands in for the bark; it only transitions
        // from Dog_Idle, so clearing isWalkingDog above lets it fire, and the trigger is sticky.
        animator.SetTrigger("steal");
        onArrive?.Invoke();

        yield return new WaitForSeconds(holdSeconds);

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