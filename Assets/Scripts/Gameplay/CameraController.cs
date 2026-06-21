using System;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Frames the table. A smoothed rig holds the resting framing (an authored anchor for the current
    /// <see cref="CameraState"/> if one exists, otherwise the scene camera's own pose). On top of that
    /// the camera snaps to a close-up of the glass while a drop resolves, always carries a gentle
    /// handheld sway, and shakes when the glass spills. (The between-rounds shop no longer moves the
    /// camera — the book lifts itself into a held pose in front of whatever the camera is framing.)
    ///
    /// Setting angles is meant to be easy: drop an empty GameObject where you want a shot and assign it
    /// to <see cref="glassCloseUpAnchor"/> (or to the
    /// <see cref="viewpoints"/> list for the resting shots). With no anchor, the close-up is generated
    /// procedurally from the side via <see cref="sideAngle"/> / <see cref="pitchAngle"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Serializable]
        public struct CameraViewpoint
        {
            public CameraState state;
            public Transform anchor;
        }

        [SerializeField] Camera targetCamera;
        [Tooltip("Resting framings. Add a row, pick a state, and assign an empty GameObject as the anchor.")]
        [SerializeField] CameraViewpoint[] viewpoints = Array.Empty<CameraViewpoint>();
        [SerializeField, Min(0f)] float blendSpeed = 5f;
        [SerializeField] CameraState currentState = CameraState.TableOverview;

        [Header("Idle Sway")]
        [SerializeField, Min(0f)] float swayPositionAmplitude = 0.05f;
        [SerializeField, Min(0f)] float swayRotationAmplitude = 0.6f;
        [SerializeField, Min(0f)] float swayFrequency = 0.18f;

        [Header("Glass Close-Up")]
        [SerializeField] Transform glassTarget;
        [SerializeField] GlassManager glassManager;
        [Tooltip("Optional: an empty GameObject posed exactly where you want the close-up camera. " +
                 "Overrides the procedural side angle below.")]
        [SerializeField] Transform glassCloseUpAnchor;
        [Tooltip("How high up the glass the close-up aims. 0 = base, 1 = top / rim.")]
        [SerializeField, Range(0f, 1.2f)] float rimHeightFraction = 0.72f;
        [Tooltip("Degrees around the glass the procedural close-up swings to. 0 = straight on, 90 = full side.")]
        [SerializeField, Range(-180f, 180f)] float sideAngle = 60f;
        [Tooltip("Degrees the procedural close-up rides above the rim, looking down at the meniscus.")]
        [SerializeField, Range(-30f, 80f)] float pitchAngle = 12f;
        [Tooltip("Safe-drop framing distance as a multiple of the glass's size. Bigger = further back.")]
        [SerializeField, Min(0.5f)] float inspectFraming = 3.6f;
        [Tooltip("Spill-likely framing distance as a multiple of the glass's size. Tighter than safe.")]
        [SerializeField, Min(0.5f)] float rimFraming = 2.6f;
        [Tooltip("Fallback glass radius (metres) used only if no renderer bounds can be measured.")]
        [SerializeField, Min(0.05f)] float fallbackSubjectRadius = 0.45f;

        [Header("Snappiness")]
        [Tooltip("How fast the camera snaps into / out of a close-up. Higher = snappier.")]
        [SerializeField, Min(0.5f)] float focusSnapSpeed = 9f;

        [Header("Overflow Shake")]
        [SerializeField, Min(0f)] float shakeAmplitude = 0.12f;
        [SerializeField, Min(0f)] float shakeDuration = 0.5f;

        Transform activeAnchor;
        Vector3 rigPosition;
        Quaternion rigRotation = Quaternion.identity;
        bool rigInitialized;
        float currentPush;
        float shakeTimer;

        Vector3 lastFocusPosition;
        Quaternion lastFocusRotation = Quaternion.identity;
        bool hasLastFocus;

        bool subjectBoundsCached;
        Bounds subjectBounds;

        public CameraState CurrentState => currentState;

        void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            activeAnchor = FindAnchor(currentState);
            SnapToActiveAnchor();
        }

        void OnEnable()
        {
            ResolveReferences();

            if (glassManager != null)
                glassManager.DropResolved += OnDropResolved;
        }

        void OnDisable()
        {
            if (glassManager != null)
                glassManager.DropResolved -= OnDropResolved;
        }

        void LateUpdate()
        {
            if (targetCamera == null)
                return;

            var cameraTransform = targetCamera.transform;

            UpdateRig(cameraTransform);

            var finalPosition = rigPosition;
            var finalRotation = rigRotation;

            ApplyFocusFraming(ref finalPosition, ref finalRotation);
            ApplyIdleSway(ref finalPosition, ref finalRotation);
            ApplyShake(ref finalPosition);

            cameraTransform.SetPositionAndRotation(finalPosition, finalRotation);
        }

        public void SwitchCamera(CameraState newState)
        {
            currentState = newState;

            var anchor = FindAnchor(newState);

            // Keep the previous framing if this state has no authored anchor.
            if (anchor != null)
                activeAnchor = anchor;
        }

        void UpdateRig(Transform cameraTransform)
        {
            if (activeAnchor != null)
            {
                if (!rigInitialized)
                {
                    rigPosition = activeAnchor.position;
                    rigRotation = activeAnchor.rotation;
                    rigInitialized = true;
                }

                var t = blendSpeed <= 0f ? 1f : 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
                rigPosition = Vector3.Lerp(rigPosition, activeAnchor.position, t);
                rigRotation = Quaternion.Slerp(rigRotation, activeAnchor.rotation, t);
                return;
            }

            // No authored anchor: hold the rig at wherever the scene camera starts.
            if (!rigInitialized)
            {
                rigPosition = cameraTransform.position;
                rigRotation = cameraTransform.rotation;
                rigInitialized = true;
            }
        }

        void ApplyFocusFraming(ref Vector3 position, ref Quaternion rotation)
        {
            var haveFocus = TryGetFocusPose(position, out var focusPosition, out var focusRotation);

            if (haveFocus)
            {
                lastFocusPosition = focusPosition;
                lastFocusRotation = focusRotation;
                hasLastFocus = true;
            }

            var engage = haveFocus ? 1f : 0f;
            currentPush = Mathf.MoveTowards(currentPush, engage, focusSnapSpeed * Time.deltaTime);

            if (currentPush <= 0f || !hasLastFocus)
                return;

            // While engaging, blend toward the live focus pose; while pulling back, blend out of the
            // last one so the return to the resting framing stays smooth.
            var poseToward = haveFocus ? focusPosition : lastFocusPosition;
            var rotToward = haveFocus ? focusRotation : lastFocusRotation;

            position = Vector3.Lerp(position, poseToward, currentPush);
            rotation = Quaternion.Slerp(rotation, rotToward, currentPush);
        }

        bool TryGetFocusPose(Vector3 fromPosition, out Vector3 focusPosition, out Quaternion focusRotation)
        {
            focusPosition = Vector3.zero;
            focusRotation = Quaternion.identity;

            if (currentState == CameraState.GlassZoom || currentState == CameraState.GlassInspect)
            {
                if (glassCloseUpAnchor != null)
                {
                    focusPosition = glassCloseUpAnchor.position;
                    focusRotation = glassCloseUpAnchor.rotation;
                    return true;
                }

                if (glassTarget == null)
                    return false;

                Vector3 focusPoint;
                float subjectRadius;

                if (TryGetSubjectBounds(out var bounds))
                {
                    // Aim partway up the measured glass and frame relative to its real size, so the
                    // shot reads the same whatever scale the glass is authored at.
                    focusPoint = new Vector3(
                        bounds.center.x,
                        bounds.min.y + bounds.size.y * rimHeightFraction,
                        bounds.center.z);
                    subjectRadius = Mathf.Max(0.05f, bounds.extents.magnitude);
                }
                else
                {
                    focusPoint = glassTarget.position + Vector3.up * (fallbackSubjectRadius * 1.3f);
                    subjectRadius = fallbackSubjectRadius;
                }

                var danger = glassManager != null
                    ? Mathf.Clamp01(glassManager.CurrentTrueSpillChance / GameConstants.MaxOverflowProbability)
                    : 0f;
                var standoff = subjectRadius * Mathf.Lerp(inspectFraming, rimFraming, danger);
                var direction = ComputeOrbitDirection(focusPoint, fromPosition, sideAngle, pitchAngle);

                focusPosition = focusPoint + direction * standoff;
                focusRotation = Quaternion.LookRotation((focusPoint - focusPosition).normalized, Vector3.up);
                return true;
            }

            return false;
        }

        bool TryGetSubjectBounds(out Bounds bounds)
        {
            if (subjectBoundsCached)
            {
                bounds = subjectBounds;
                return true;
            }

            bounds = default;

            if (glassTarget == null)
                return false;

            var renderers = glassTarget.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return false;

            var combined = renderers[0].bounds;

            for (var i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            subjectBounds = combined;
            subjectBoundsCached = true;
            bounds = combined;
            return true;
        }

        static Vector3 ComputeOrbitDirection(Vector3 focusPoint, Vector3 fromPosition, float yawDegrees, float pitchDegrees)
        {
            // Start from the horizontal heading the resting camera already has on the subject, swing
            // around it by yaw, then lift by pitch. The result points from the subject to the camera.
            var horizontal = fromPosition - focusPoint;
            horizontal.y = 0f;

            if (horizontal.sqrMagnitude < 0.0001f)
                horizontal = Vector3.back;

            horizontal.Normalize();

            var yawed = Quaternion.AngleAxis(yawDegrees, Vector3.up) * horizontal;
            var pitch = pitchDegrees * Mathf.Deg2Rad;

            return (yawed * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
        }

        void ApplyIdleSway(ref Vector3 position, ref Quaternion rotation)
        {
            var swayMultiplier = StateSwayMultiplier(currentState);

            if (swayMultiplier <= 0f)
                return;

            var time = Time.time * swayFrequency;

            var swayLocal = new Vector3(
                Mathf.PerlinNoise(time, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, time) - 0.5f,
                Mathf.PerlinNoise(time, time) - 0.5f) * (swayPositionAmplitude * swayMultiplier);

            position += rotation * swayLocal;

            var swayEuler = new Vector3(
                Mathf.PerlinNoise(time + 11f, 3f) - 0.5f,
                Mathf.PerlinNoise(3f, time + 11f) - 0.5f,
                0f) * (swayRotationAmplitude * swayMultiplier);

            rotation *= Quaternion.Euler(swayEuler);
        }

        void ApplyShake(ref Vector3 position)
        {
            if (shakeTimer <= 0f)
                return;

            shakeTimer = Mathf.Max(0f, shakeTimer - Time.deltaTime);

            var envelope = shakeDuration > 0f ? shakeTimer / shakeDuration : 0f;
            position += UnityEngine.Random.insideUnitSphere * (shakeAmplitude * envelope * envelope);
        }

        void OnDropResolved(GlassDropResult result)
        {
            if (result.Overflowed)
                shakeTimer = shakeDuration;
        }

        void SnapToActiveAnchor()
        {
            if (targetCamera == null || activeAnchor == null)
                return;

            targetCamera.transform.SetPositionAndRotation(activeAnchor.position, activeAnchor.rotation);
            rigPosition = activeAnchor.position;
            rigRotation = activeAnchor.rotation;
            rigInitialized = true;
        }

        Transform FindAnchor(CameraState state)
        {
            for (var i = 0; i < viewpoints.Length; i++)
            {
                if (viewpoints[i].state == state && viewpoints[i].anchor != null)
                    return viewpoints[i].anchor;
            }

            return null;
        }

        void ResolveReferences()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            if (glassTarget == null)
            {
                var glassObject = GameObject.FindGameObjectWithTag("Glass");

                if (glassObject != null)
                    glassTarget = glassObject.transform;
            }
        }

        static float StateSwayMultiplier(CameraState state) =>
            state switch
            {
                CameraState.TableOverview => 1f,
                CameraState.GlassZoom => 0.18f,
                CameraState.GlassInspect => 0.15f,
                CameraState.ShopFocus => 0.3f,
                _ => 0.7f
            };
    }
}
