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

        [Header("Seating Intro")]
        [Tooltip("Optional pixel-exact standing/far pose the seating intro starts from. If set, the back/up " +
                 "offsets below are ignored.")]
        [SerializeField] Transform seatingStartAnchor;
        [Tooltip("How far back (metres) along the floor the intro starts — how far you 'walk in' to the desk.")]
        [SerializeField, Min(0f)] float seatingStandBack = 6f;
        [Tooltip("Standing eye height above the seated pose (metres) — i.e. how far the camera drops when it " +
                 "sits down. Kept small so the walk-in stays around the seated head height.")]
        [SerializeField, Min(0f)] float seatingStandUp = 0.5f;
        [Tooltip("Downward look (degrees) held while walking in. Small = looking ahead into the room rather " +
                 "than down at the table; the view tips down to the seated framing as you sit.")]
        [SerializeField, Range(0f, 45f)] float seatingStandPitch = 12f;
        [Tooltip("Seconds the walk-in to the desk takes.")]
        [SerializeField, Min(0.1f)] float seatingApproachSeconds = 3f;
        [Tooltip("Number of footstep bobs over the whole walk.")]
        [SerializeField, Min(0f)] float seatingStepCount = 7f;
        [Tooltip("Vertical footstep bob height (metres).")]
        [SerializeField, Min(0f)] float seatingBobAmplitude = 0.06f;
        [Tooltip("Side-to-side walk sway (metres).")]
        [SerializeField, Min(0f)] float seatingSwayAmplitude = 0.03f;
        [Tooltip("How far into the walk (0..1) the player reaches the seat and the sit-down begins.")]
        [SerializeField, Range(0.5f, 1f)] float seatingSitStart = 0.78f;
        [Tooltip("How far the camera dips below the seat and settles back as it sits (metres) — a cushion bounce.")]
        [SerializeField, Min(0f)] float seatingSitDip = 0.06f;

        [Header("Wake-Up Intro")]
        [Tooltip("Optional pixel-exact 'head resting on the table' pose the wake-up starts from. If set, the " +
                 "drop / lean / pitch / roll below are ignored.")]
        [SerializeField] Transform wakeStartAnchor;
        [Tooltip("How far the head is lowered toward the table at the start of the wake-up (metres below the " +
                 "seated eye line).")]
        [SerializeField, Min(0f)] float wakeHeadDrop = 0.45f;
        [Tooltip("How far the head leans forward onto the table while it rests (metres toward the table).")]
        [SerializeField] float wakeHeadForward = 0.18f;
        [Tooltip("Extra downward pitch while the head rests on the table (degrees added to the seated framing).")]
        [SerializeField, Range(0f, 90f)] float wakeHeadPitch = 55f;
        [Tooltip("Sideways tilt (roll) of the head resting on the table (degrees). 0 = face straight down.")]
        [SerializeField, Range(-90f, 90f)] float wakeHeadRoll = 16f;
        [Tooltip("Seconds the head-lift (waking up) takes.")]
        [SerializeField, Min(0.1f)] float wakeRiseSeconds = 1.6f;

        [Header("Camera Nod")]
        [Tooltip("Pitch swing (degrees) of a gentle nod.")]
        [SerializeField, Min(0f)] float nodAmplitude = 5f;
        [Tooltip("Pitch swing (degrees) of a strong / emphatic nod.")]
        [SerializeField, Min(0f)] float nodStrongAmplitude = 9f;
        [Tooltip("Seconds one down-up nod swing takes.")]
        [SerializeField, Min(0.05f)] float nodSwingSeconds = 0.55f;
        [Tooltip("Number of down-up swings in a strong nod.")]
        [SerializeField, Min(1)] int nodStrongSwings = 2;

        [Header("Dialogue Shots")]
        [Tooltip("The dealer's face/head to frame in close-up while he talks. If empty, the " +
                 "'Main_character-textured' model's 'Eye' anchor is found at runtime.")]
        [SerializeField] Transform dialogueSubject;
        [Tooltip("Scene name of the dealer model, used to find the face when no subject is wired above.")]
        [SerializeField] string dialogueSubjectName = "Main_character-textured";
        [Tooltip("Extra height added to the framed face (metres). 0 if the wired / found anchor is already the face.")]
        [SerializeField] float dialogueFaceHeight = 0f;
        [Tooltip("How close the camera sits to the face (metres). Small = an extreme close-up.")]
        [SerializeField, Min(0.1f)] float dialogueFaceDistance = 0.5f;
        [Tooltip("How far to either side of the face each shot sits (degrees). It jumps left/right each line.")]
        [SerializeField, Range(0f, 80f)] float dialogueFaceYaw = 24f;
        [Tooltip("How high the close-up rides relative to the face (degrees). ~0 = level with his eyes.")]
        [SerializeField, Range(-30f, 40f)] float dialogueFacePitch = 3f;
        [Tooltip("Fallback only: distance ahead of the seated eye to aim if no dealer face can be found.")]
        [SerializeField, Min(0.1f)] float dialogueSubjectDistance = 1.5f;

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

        bool seatingIntroActive;   // the seating intro fully owns the camera (stand → walk → sit)
        bool seatingWalking;       // the walk-in has begun (false while holding the standing shot)
        float seatingT;            // walk progress, 0..1
        Vector3 seatStartPos;      // far standing pose the walk begins at (back along the floor, head height)
        Quaternion seatStartRot = Quaternion.identity;
        Vector3 seatStandPos;      // standing over the seat, just before sitting (same head height)
        Quaternion seatStandRot = Quaternion.identity;   // the level walking look
        Vector3 seatEndPos;        // seated pose the walk ends at (the captured base/overview pose)
        Quaternion seatEndRot = Quaternion.identity;
        Action seatingArrived;     // fired once seated, handing control back to the GameManager
        Vector3 rigPosition;
        Quaternion rigRotation = Quaternion.identity;
        Vector3 basePosition;
        Quaternion baseRotation = Quaternion.identity;
        bool baseCaptured;
        float currentPush;
        float shakeTimer;

        bool wakeIntroActive;      // the wake-up intro owns the camera (slumped hold → head lift)
        bool wakeRising;           // the head lift has begun (false while slumped on the table)
        float wakeT;               // lift progress, 0..1
        Vector3 wakeStartPos;      // slumped pose: head dropped toward / onto the table
        Quaternion wakeStartRot = Quaternion.identity;
        Vector3 wakeEndPos;        // the woken (seated base) pose the lift ends at
        Quaternion wakeEndRot = Quaternion.identity;
        Action wakeArrived;        // fired once awake, handing control back

        float nodTimer;            // counts a nod down to zero
        float nodTotal;            // full duration of the active nod
        float nodAmp;              // pitch amplitude of the active nod (degrees)
        int nodSwings;             // down-up swings in the active nod

        bool dialogueShotActive;   // dialogue framing owns the rig: an extreme close-up on the dealer's face
        int dialogueShotIndex;     // increments per line; parity picks the left / right side of his face
        float dialogueShotYaw;     // current side of the face (jumps between -dialogueFaceYaw and +dialogueFaceYaw)
        Transform resolvedSubject; // the dealer face anchor resolved at BeginDialogueShots

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

            // The start-of-match seating intro likewise owns the camera: it holds the standing shot, then
            // walks in and sits, before handing the rig back for normal play.
            if (seatingIntroActive)
            {
                ApplySeatingIntro(cameraTransform);
                return;
            }

            // The wake-up intro owns the camera the same way: hold the head slumped on the table, then lift it
            // to the seated pose before handing the rig back so the dealer's monologue can play.
            if (wakeIntroActive)
            {
                ApplyWakeUp(cameraTransform);
                return;
            }

            UpdateRig(cameraTransform);

            var finalPosition = rigPosition;
            var finalRotation = rigRotation;

            ApplyFocusFraming(ref finalPosition, ref finalRotation);
            ApplyIdleSway(ref finalPosition, ref finalRotation);
            ApplyNod(ref finalRotation);
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
            EnsureBaseCaptured(cameraTransform);

            if (dialogueShotActive)
            {
                // Snap (jump) to the current extreme close-up on the dealer's face — no blend, so each line is
                // a hard cut between the left and right side of his face. Idle sway still adds handheld life.
                DialogueFaceTarget(out rigPosition, out rigRotation);
                return;
            }

            if (!TryGetRestingPose(out var targetPosition, out var targetRotation))
                return; // Hold the current rig pose (glass close-ups blend on top of wherever we are).

            var t = blendSpeed <= 0f ? 1f : 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
            rigPosition = Vector3.Lerp(rigPosition, targetPosition, t);
            rigRotation = Quaternion.Slerp(rigRotation, targetRotation, t);
        }

        void EnsureBaseCaptured(Transform cameraTransform)
        {
            if (baseCaptured)
                return;

            // The resting reference pose: an authored TableOverview anchor if present, else wherever the
            // scene camera starts. Every offset-based viewpoint (and the seated end of the intro) is measured
            // from here.
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

        public bool IsSeatingIntroActive => seatingIntroActive;

        /// <summary>
        /// Take over the camera for the start-of-match seating intro: capture the seated framing, place the
        /// camera at a standing pose pulled back from the desk, and hold there. Call
        /// <see cref="PlaySeatingApproach"/> once the player chooses to sit to walk it in.
        /// </summary>
        public void BeginSeatingIntro()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            ResolveReferences();

            var cameraTransform = targetCamera != null ? targetCamera.transform : null;
            if (cameraTransform == null)
                return;

            EnsureBaseCaptured(cameraTransform);

            seatEndPos = basePosition;
            seatEndRot = baseRotation;

            // Standing over the seat: the seated eye position raised by standUp. This is the head height held
            // the whole walk; sitting just drops world-Y back down to the seated pose.
            seatStandPos = basePosition + Vector3.up * seatingStandUp;

            // The walking look: the seated heading flattened onto the floor (so we don't inherit its downward
            // tilt) plus a small look-down. The view tips the rest of the way down as the player sits.
            var flatForward = baseRotation * Vector3.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 1e-5f ? flatForward.normalized : Vector3.forward;
            seatStandRot = Quaternion.LookRotation(flatForward, Vector3.up) * Quaternion.Euler(seatingStandPitch, 0f, 0f);

            if (seatingStartAnchor != null)
            {
                seatStartPos = seatingStartAnchor.position;
                seatStartRot = seatingStartAnchor.rotation;
            }
            else
            {
                // Back along the floor at the same head height — a level approach, no descent from above.
                seatStartPos = seatStandPos - flatForward * seatingStandBack;
                seatStartRot = seatStandRot;
            }

            seatingIntroActive = true;
            seatingWalking = false;
            seatingT = 0f;

            cameraTransform.SetPositionAndRotation(seatStartPos, seatStartRot);
        }

        /// <summary>
        /// Walk the held standing shot in to the seated pose — footstep bob plus a sit-down settle — then
        /// release the camera and invoke <paramref name="onArrived"/>. If the intro isn't active it simply
        /// fires the callback so the round can still start.
        /// </summary>
        public void PlaySeatingApproach(Action onArrived)
        {
            if (!seatingIntroActive)
            {
                onArrived?.Invoke();
                return;
            }

            seatingArrived = onArrived;
            seatingWalking = true;
            seatingT = 0f;
        }

        /// <summary>Abort the intro immediately, snapping to the seated pose and firing any pending callback.</summary>
        public void CancelSeatingIntro()
        {
            if (!seatingIntroActive)
                return;

            if (targetCamera != null)
                targetCamera.transform.SetPositionAndRotation(seatEndPos, seatEndRot);

            FinishSeating();
        }

        void ApplySeatingIntro(Transform cameraTransform)
        {
            if (!seatingWalking)
            {
                // Holding the standing shot until the player chooses to sit; keep a gentle handheld sway so
                // it reads as a live view rather than a frozen frame.
                var holdPosition = seatStartPos;
                var holdRotation = seatStartRot;
                ApplyIdleSway(ref holdPosition, ref holdRotation);
                cameraTransform.SetPositionAndRotation(holdPosition, holdRotation);
                return;
            }

            seatingT += Time.unscaledDeltaTime / Mathf.Max(0.01f, seatingApproachSeconds);
            var finished = seatingT >= 1f;
            var t = Mathf.Clamp01(seatingT);

            Vector3 position;
            Quaternion rotation;

            if (t < seatingSitStart)
            {
                // Walk in at head height: travel horizontally to the seat with a footstep bob — no descent.
                var walk = Smootherstep(t / seatingSitStart);
                position = Vector3.Lerp(seatStartPos, seatStandPos, walk);
                rotation = Quaternion.Slerp(seatStartRot, seatStandRot, walk);

                var window = Mathf.Sin((t / seatingSitStart) * Mathf.PI);   // fade the bob in and out
                var stepPhase = (t / seatingSitStart) * seatingStepCount * Mathf.PI * 2f;
                var bob = Mathf.Sin(stepPhase) * (seatingBobAmplitude * window);
                var sway = Mathf.Cos(stepPhase * 0.5f) * (seatingSwayAmplitude * window);
                position += Vector3.up * bob + rotation * new Vector3(sway, 0f, 0f);
            }
            else
            {
                // Sit down: drop world-Y from standing to the seated pose and tip into the seated framing,
                // with a small cushion dip that settles back to rest.
                var s = Smootherstep(Mathf.InverseLerp(seatingSitStart, 1f, t));
                position = Vector3.Lerp(seatStandPos, seatEndPos, s);
                rotation = Quaternion.Slerp(seatStandRot, seatEndRot, s);
                position += Vector3.up * (-Mathf.Sin(s * Mathf.PI) * seatingSitDip);
            }

            cameraTransform.SetPositionAndRotation(position, rotation);

            if (finished)
                FinishSeating();
        }

        void FinishSeating()
        {
            seatingIntroActive = false;
            seatingWalking = false;

            // Hand the rig back exactly where the walk ended so the resting framing doesn't jump when the
            // GameManager switches to TableOverview for the round.
            if (targetCamera != null)
            {
                rigPosition = targetCamera.transform.position;
                rigRotation = targetCamera.transform.rotation;
            }

            var callback = seatingArrived;
            seatingArrived = null;
            callback?.Invoke();
        }

        public bool IsWakeUpIntroActive => wakeIntroActive;

        /// <summary>
        /// Take over the camera for the start-of-match wake-up: capture the seated framing, then drop the
        /// camera onto the table (head resting, pitched and tilted down) and hold there. Call
        /// <see cref="PlayWakeUp"/> to lift the head to the seated pose. Replaces the walk-in seating intro.
        /// </summary>
        public void BeginWakeUp()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            ResolveReferences();

            var cameraTransform = targetCamera != null ? targetCamera.transform : null;
            if (cameraTransform == null)
                return;

            EnsureBaseCaptured(cameraTransform);

            wakeEndPos = basePosition;
            wakeEndRot = baseRotation;

            if (wakeStartAnchor != null)
            {
                wakeStartPos = wakeStartAnchor.position;
                wakeStartRot = wakeStartAnchor.rotation;
            }
            else
            {
                // Lower the seated eye toward the table and lean forward onto it, then pitch / roll the head
                // down so it reads as resting face-down on the desk.
                var flatForward = baseRotation * Vector3.forward;
                flatForward.y = 0f;
                flatForward = flatForward.sqrMagnitude > 1e-5f ? flatForward.normalized : Vector3.forward;

                wakeStartPos = basePosition - Vector3.up * wakeHeadDrop + flatForward * wakeHeadForward;
                wakeStartRot = baseRotation * Quaternion.Euler(wakeHeadPitch, 0f, wakeHeadRoll);
            }

            wakeIntroActive = true;
            wakeRising = false;
            wakeT = 0f;

            cameraTransform.SetPositionAndRotation(wakeStartPos, wakeStartRot);
        }

        /// <summary>
        /// Lift the held head from the table to the seated pose, then release the camera and invoke
        /// <paramref name="onAwake"/>. If the intro isn't active it simply fires the callback.
        /// </summary>
        public void PlayWakeUp(Action onAwake)
        {
            if (!wakeIntroActive)
            {
                onAwake?.Invoke();
                return;
            }

            wakeArrived = onAwake;
            wakeRising = true;
            wakeT = 0f;
        }

        /// <summary>Abort the wake-up immediately, snapping to the seated pose and firing any pending callback.</summary>
        public void CancelWakeUp()
        {
            if (!wakeIntroActive)
                return;

            if (targetCamera != null)
                targetCamera.transform.SetPositionAndRotation(wakeEndPos, wakeEndRot);

            FinishWake();
        }

        void ApplyWakeUp(Transform cameraTransform)
        {
            if (!wakeRising)
            {
                // Slumped on the table; keep a gentle handheld sway so it reads as a live view, not a freeze.
                var holdPosition = wakeStartPos;
                var holdRotation = wakeStartRot;
                ApplyIdleSway(ref holdPosition, ref holdRotation);
                cameraTransform.SetPositionAndRotation(holdPosition, holdRotation);
                return;
            }

            wakeT += Time.unscaledDeltaTime / Mathf.Max(0.01f, wakeRiseSeconds);
            var finished = wakeT >= 1f;
            var s = Smootherstep(Mathf.Clamp01(wakeT));

            var position = Vector3.Lerp(wakeStartPos, wakeEndPos, s);
            var rotation = Quaternion.Slerp(wakeStartRot, wakeEndRot, s);
            cameraTransform.SetPositionAndRotation(position, rotation);

            if (finished)
                FinishWake();
        }

        void FinishWake()
        {
            wakeIntroActive = false;
            wakeRising = false;

            // Hand the rig back where the lift ended so the resting framing doesn't jump.
            if (targetCamera != null)
            {
                rigPosition = targetCamera.transform.position;
                rigRotation = targetCamera.transform.rotation;
            }

            var callback = wakeArrived;
            wakeArrived = null;
            callback?.Invoke();
        }

        /// <summary>
        /// Kick a camera nod — a brief down-up pitch swing layered on top of the current framing, as if the
        /// player nods. <paramref name="strong"/> swings further and twice. Used by the dealer's monologue.
        /// </summary>
        public void NodCamera(bool strong)
        {
            nodAmp = strong ? nodStrongAmplitude : nodAmplitude;
            nodSwings = strong ? Mathf.Max(1, nodStrongSwings) : 1;
            nodTotal = Mathf.Max(0.05f, nodSwingSeconds) * nodSwings;
            nodTimer = nodTotal;
        }

        void ApplyNod(ref Quaternion rotation)
        {
            if (nodTimer <= 0f)
                return;

            nodTimer = Mathf.Max(0f, nodTimer - Time.unscaledDeltaTime);

            var progress = nodTotal > 0f ? 1f - (nodTimer / nodTotal) : 1f;   // 0..1 across the whole nod
            var phase = progress * nodSwings * Mathf.PI * 2f;                 // sin starts at 0 → dips down first
            var envelope = Mathf.Sin(progress * Mathf.PI);                    // 0 at both ends, so it settles to neutral
            var pitch = Mathf.Sin(phase) * nodAmp * envelope;                 // +pitch tips the view down

            rotation *= Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>
        /// Hand the rig over to dialogue framing: an extreme close-up on the dealer's face, jumping between
        /// the left and right side of it via <see cref="NextDialogueShot"/> on each line. Call
        /// <see cref="EndDialogueShots"/> when the talk ends.
        /// </summary>
        public void BeginDialogueShots()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            if (targetCamera != null)
                EnsureBaseCaptured(targetCamera.transform);

            resolvedSubject = ResolveDialogueSubject();
            dialogueShotActive = true;
            dialogueShotIndex = 0;
            dialogueShotYaw = -dialogueFaceYaw;   // a default side until the first NextDialogueShot
        }

        /// <summary>Jump to the other side of his face. Called when the player clicks to continue.</summary>
        public void NextDialogueShot()
        {
            if (!dialogueShotActive)
                return;

            dialogueShotIndex++;
            dialogueShotYaw = (dialogueShotIndex % 2 == 0 ? -1f : 1f) * dialogueFaceYaw;
        }

        /// <summary>Release the rig back to its resting framing.</summary>
        public void EndDialogueShots()
        {
            dialogueShotActive = false;
            resolvedSubject = null;
        }

        public bool IsDialogueShotActive => dialogueShotActive;

        // The dealer's face: the wired subject, else the named model's 'Eye' / head anchor, else null.
        Transform ResolveDialogueSubject()
        {
            if (dialogueSubject != null)
                return dialogueSubject;

            if (string.IsNullOrEmpty(dialogueSubjectName))
                return null;

            var root = GameObject.Find(dialogueSubjectName);
            if (root == null)
                return null;

            Transform head = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("eye"))
                    return t;                       // the dealer's eye mesh — right on the face
                if (head == null && n.Contains("head"))
                    head = t;
            }

            return head != null ? head : root.transform;
        }

        // An extreme close-up looking at the dealer's face from the current side (dialogueShotYaw).
        void DialogueFaceTarget(out Vector3 position, out Quaternion rotation)
        {
            var flatForward = baseRotation * Vector3.forward;
            flatForward.y = 0f;
            flatForward = flatForward.sqrMagnitude > 1e-5f ? flatForward.normalized : Vector3.forward;

            var focus = resolvedSubject != null
                ? resolvedSubject.position + Vector3.up * dialogueFaceHeight
                : basePosition + flatForward * dialogueSubjectDistance + Vector3.up * dialogueFaceHeight;

            // Sit on the player's side of the face (back toward the seat), offset left / right by the yaw.
            var back = -flatForward;
            var horizontal = (Quaternion.AngleAxis(dialogueShotYaw, Vector3.up) * back).normalized;
            var pitchRad = dialogueFacePitch * Mathf.Deg2Rad;
            var camDir = (horizontal * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad)).normalized;

            position = focus + camDir * Mathf.Max(0.1f, dialogueFaceDistance);
            var look = focus - position;
            rotation = look.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(look.normalized, Vector3.up)
                : baseRotation;
        }

        static float Smootherstep(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (6f * x - 15f) + 10f);
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
