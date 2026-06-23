using System;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Frames the table. A smoothed rig holds the resting framing for the current <see cref="CameraState"/>.
    /// On top of that the camera snaps to a close-up of the glass while a drop resolves, always carries a
    /// gentle handheld sway, and shakes when the glass spills. (The between-rounds shop no longer moves the
    /// camera — the book lifts itself into a held pose in front of whatever the camera is framing.)
    ///
    /// Tuning each angle is meant to be easy and live: add a row to <see cref="viewpoints"/>, pick a state,
    /// and dial in a position / rotation offset from the resting (overview) pose right in the inspector —
    /// e.g. give <see cref="CameraState.PlayerFocus"/> a downward pitch so the player's turn tips toward the
    /// table. For pixel-exact shots assign an empty GameObject as the row's anchor instead; that overrides
    /// the offsets. The base/overview pose is the <see cref="CameraState.TableOverview"/> anchor if one is
    /// assigned, otherwise the scene camera's own starting pose. The glass close-up is generated procedurally
    /// from <see cref="sideAngle"/> / <see cref="pitchAngle"/> unless <see cref="glassCloseUpAnchor"/> is set.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraController : MonoBehaviour
    {
        [Serializable]
        public struct CameraViewpoint
        {
            public CameraState state;

            [Tooltip("Optional pixel-exact shot: assign an empty GameObject. If set it fully defines this " +
                     "angle and the offsets below are ignored.")]
            public Transform anchor;

            [Tooltip("Position offset from the resting/overview pose, in that pose's local space: " +
                     "+x right, +y up, +z forward (toward the table).")]
            public Vector3 positionOffset;

            [Tooltip("Rotation offset from the resting pose, in degrees, in the camera's own space: " +
                     "+x pitches the view DOWN toward the table, +y yaws right, +z rolls.")]
            public Vector3 rotationOffset;
        }

        [SerializeField] Camera targetCamera;
        [Tooltip("Per-state resting framings. Add a row, pick a state, and dial in a position / rotation " +
                 "offset from the overview pose (or assign an anchor for a pixel-exact shot).")]
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
        [Tooltip("Default drop framing: degrees around the glass for the straight-on front close-up. 0 = dead front.")]
        [SerializeField, Range(-180f, 180f)] float frontAngle = 0f;
        [Tooltip("Bait / overflow framing: degrees around the glass the close-up swings to. 0 = straight on, 90 = full side.")]
        [SerializeField, Range(-180f, 180f)] float sideAngle = 60f;
        [Tooltip("Degrees the procedural close-up rides above the rim, looking down at the meniscus.")]
        [SerializeField, Range(-30f, 80f)] float pitchAngle = 12f;
        [Tooltip("Safe-drop framing distance as a multiple of the glass's size. Bigger = further back.")]
        [SerializeField, Min(0.5f)] float inspectFraming = 3.6f;
        [Tooltip("Spill-likely framing distance as a multiple of the glass's size. Tighter than safe.")]
        [SerializeField, Min(0.5f)] float rimFraming = 2.6f;
        [Tooltip("Fallback glass radius (metres) used only if no renderer bounds can be measured.")]
        [SerializeField, Min(0.05f)] float fallbackSubjectRadius = 0.45f;

        [Header("Overflow Close-Up")]
        [Tooltip("Degrees around the glass the overflow reveal swings to (dramatic side-on). 0 = straight on.")]
        [SerializeField, Range(-180f, 180f)] float overflowYaw = 80f;
        [Tooltip("Degrees the overflow shot rides above the rim. Negative looks UP so the cascade reads against the background.")]
        [SerializeField, Range(-40f, 60f)] float overflowPitch = -4f;
        [Tooltip("Overflow framing distance as a multiple of the glass size. Tight so the spill fills the frame.")]
        [SerializeField, Min(0.5f)] float overflowFraming = 2.1f;
        [Tooltip("How high up the glass the overflow shot aims (lower than the drop close-up so the run-down + base show). 0 = base, 1 = rim.")]
        [SerializeField, Range(0f, 1.2f)] float overflowRimHeightFraction = 0.55f;

        [Header("Snappiness")]
        [Tooltip("How fast the camera snaps into / out of a close-up. Higher = snappier.")]
        [SerializeField, Min(0.5f)] float focusSnapSpeed = 9f;

        [Header("Overflow Shake")]
        [SerializeField, Min(0f)] float shakeAmplitude = 0.12f;
        [SerializeField, Min(0f)] float shakeDuration = 0.5f;

        [Header("Death Orbit")]
        [Tooltip("Distance the loss orbit holds from the glass, as a multiple of the glass size.")]
        [SerializeField, Min(0.5f)] float orbitRadiusMultiplier = 3.2f;
        [Tooltip("Height of the loss orbit above the glass centre (metres). Small positive = a gentle look down.")]
        [SerializeField] float orbitHeight = 0.12f;
        [Tooltip("How fast the loss orbit circles the glass, in degrees per second.")]
        [SerializeField] float orbitSpeed = 20f;

        float activeGlassYaw;   // live yaw for the glass close-up: front by default, side to bait / on overflow
        bool framingOverflow;   // the close-up uses its dramatic overflow pose (set by FocusOverflow)
        bool orbiting;          // the loss "death orbit" owns the camera until the match restarts
        float orbitAngle;       // current orbit heading around the glass, in degrees
        Vector3 rigPosition;
        Quaternion rigRotation = Quaternion.identity;
        Vector3 basePosition;
        Quaternion baseRotation = Quaternion.identity;
        bool baseCaptured;
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
        }

        void OnEnable()
        {
            ResolveReferences();
            activeGlassYaw = frontAngle;
        }

        void LateUpdate()
        {
            if (targetCamera == null)
                return;

            var cameraTransform = targetCamera.transform;

            // The loss "death orbit" fully owns the camera (no rig / focus / sway) until the match restarts.
            if (orbiting)
            {
                ApplyDeathOrbit(cameraTransform);
                return;
            }

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
            // The overflow framing only applies to its own reveal; any other state change clears it.
            if (newState != CameraState.GlassZoom && newState != CameraState.GlassInspect)
                framingOverflow = false;

            currentState = newState;
        }

        void UpdateRig(Transform cameraTransform)
        {
            if (!baseCaptured)
            {
                // The resting reference pose: an authored TableOverview anchor if present, else wherever
                // the scene camera starts. Every offset-based viewpoint is measured from here.
                var overview = FindAnchor(CameraState.TableOverview);

                if (overview != null)
                {
                    basePosition = overview.position;
                    baseRotation = overview.rotation;
                }
                else
                {
                    basePosition = cameraTransform.position;
                    baseRotation = cameraTransform.rotation;
                }

                rigPosition = basePosition;
                rigRotation = baseRotation;
                baseCaptured = true;
            }

            if (!TryGetRestingPose(out var targetPosition, out var targetRotation))
                return; // Hold the current rig pose (glass close-ups blend on top of wherever we are).

            var t = blendSpeed <= 0f ? 1f : 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            rigPosition = Vector3.Lerp(rigPosition, targetPosition, t);
            rigRotation = Quaternion.Slerp(rigRotation, targetRotation, t);
        }

        bool TryGetRestingPose(out Vector3 position, out Quaternion rotation)
        {
            // An assigned anchor is a pixel-exact override and wins outright.
            var anchor = FindAnchor(currentState);

            if (anchor != null)
            {
                position = anchor.position;
                rotation = anchor.rotation;
                return true;
            }

            // Otherwise apply the per-state offset from the base pose. Rotation is post-multiplied so the
            // offset reads in the camera's own space (+x pitches the view down); position is offset along
            // the base orientation's axes (+z toward the table).
            if (TryGetViewpoint(currentState, out var viewpoint))
            {
                rotation = baseRotation * Quaternion.Euler(viewpoint.rotationOffset);
                position = basePosition + baseRotation * viewpoint.positionOffset;
                return true;
            }

            // Glass close-ups are framed procedurally on top of the rig (see ApplyFocusFraming); hold the
            // current resting pose so the push-in / pull-out blends smoothly from wherever we already are.
            if (currentState == CameraState.GlassZoom || currentState == CameraState.GlassInspect)
            {
                position = rigPosition;
                rotation = rigRotation;
                return false;
            }

            // Any unconfigured state simply rests at the base/overview pose.
            position = basePosition;
            rotation = baseRotation;
            return true;
        }

        bool TryGetViewpoint(CameraState state, out CameraViewpoint viewpoint)
        {
            for (var i = 0; i < viewpoints.Length; i++)
            {
                if (viewpoints[i].state == state)
                {
                    viewpoint = viewpoints[i];
                    return true;
                }
            }

            viewpoint = default;
            return false;
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
                    // shot reads the same whatever scale the glass is authored at. The overflow shot
                    // aims lower so the rim, the run-down and the base all stay in frame.
                    var rimFraction = framingOverflow ? overflowRimHeightFraction : rimHeightFraction;
                    focusPoint = new Vector3(
                        bounds.center.x,
                        bounds.min.y + bounds.size.y * rimFraction,
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

                // The overflow reveal gets its own tight, low side-on framing; otherwise the standoff and
                // pitch follow the danger-lerped drop close-up.
                var framingMultiplier = framingOverflow ? overflowFraming : Mathf.Lerp(inspectFraming, rimFraming, danger);
                var pitch = framingOverflow ? overflowPitch : pitchAngle;
                var standoff = subjectRadius * framingMultiplier;
                var direction = ComputeOrbitDirection(focusPoint, fromPosition, activeGlassYaw, pitch);

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

        /// <summary>
        /// Push into the glass close-up for a drop. <paramref name="fromSide"/> swings to the dramatic side
        /// angle (used to bait, and always on a real overflow); otherwise it frames the glass straight-on
        /// from the front. Driven by the drop presentation conductor.
        /// </summary>
        public void FocusGlass(bool fromSide)
        {
            framingOverflow = false;
            activeGlassYaw = fromSide ? sideAngle : frontAngle;
            SwitchCamera(CameraState.GlassZoom);
        }

        /// <summary>
        /// Snap to the dramatic overflow shot: a tight, low, side-on close-up that catches the liquid
        /// cresting the rim and running down the glass. Driven by the drop conductor at the spill reveal.
        /// </summary>
        public void FocusOverflow()
        {
            activeGlassYaw = overflowYaw;
            framingOverflow = true;
            SwitchCamera(CameraState.GlassZoom);
        }

        /// <summary>Kick the overflow camera shake. Called at the dramatic spill reveal.</summary>
        public void Shake() => shakeTimer = shakeDuration;

        /// <summary>
        /// Start the loss "death orbit": the camera circles the glass continuously, ignoring the rig, until
        /// <see cref="StopGlassOrbit"/> (called when the match restarts). The starting angle is taken from the
        /// camera's current heading on the glass so the orbit eases in without a jump.
        /// </summary>
        public void BeginGlassOrbit()
        {
            ResolveReferences();
            orbiting = true;

            var center = OrbitCenter(out _);
            var flat = (targetCamera != null ? targetCamera.transform.position : center + Vector3.back) - center;
            flat.y = 0f;
            orbitAngle = flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.z, flat.x) * Mathf.Rad2Deg : 0f;
        }

        /// <summary>Stop the loss orbit and hand the rig back where the orbit left off (no jump on restart).</summary>
        public void StopGlassOrbit()
        {
            orbiting = false;

            if (targetCamera != null)
            {
                rigPosition = targetCamera.transform.position;
                rigRotation = targetCamera.transform.rotation;
            }
        }

        public bool IsOrbiting => orbiting;

        void ApplyDeathOrbit(Transform cameraTransform)
        {
            var center = OrbitCenter(out var radius);
            orbitAngle += orbitSpeed * Time.unscaledDeltaTime;

            var position = OrbitPosition(center, radius * orbitRadiusMultiplier, orbitHeight, orbitAngle);
            var look = center - position;
            var rotation = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : cameraTransform.rotation;

            cameraTransform.SetPositionAndRotation(position, rotation);
        }

        // The glass centre + radius the orbit revolves around (measured bounds if available).
        Vector3 OrbitCenter(out float radius)
        {
            if (TryGetSubjectBounds(out var bounds))
            {
                radius = Mathf.Max(0.05f, bounds.extents.magnitude);
                return bounds.center;
            }

            radius = fallbackSubjectRadius;
            return glassTarget != null ? glassTarget.position : basePosition;
        }

        /// <summary>Camera position on a horizontal circle of <paramref name="radius"/> around
        /// <paramref name="center"/>, lifted by <paramref name="height"/>, at <paramref name="angleDeg"/> degrees.</summary>
        public static Vector3 OrbitPosition(Vector3 center, float radius, float height, float angleDeg)
        {
            var a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(
                center.x + Mathf.Cos(a) * radius,
                center.y + height,
                center.z + Mathf.Sin(a) * radius);
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
