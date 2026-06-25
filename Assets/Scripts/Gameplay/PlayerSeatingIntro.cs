using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// The start-of-match "take your seat" beat. Before the first coin toss the camera holds a standing shot
    /// pulled back from the desk; clicking the desk walks the camera in (footstep bob) and sits the player
    /// down at the table, then hands control back so the round / coin toss begins. There is no UI — the desk
    /// itself is the prompt. Built and owned at runtime, play-mode only: EditMode tests never spawn it and the
    /// GameManager seats instantly. Mirrors <see cref="LossSequence"/> — it owns the beat and delegates the
    /// camera motion to <see cref="CameraController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerSeatingIntro : MonoBehaviour
    {
        [Tooltip("The clickable desk. If left empty, the 'Saloon Table' (or 'Table') object is found at " +
                 "runtime; failing that, a click anywhere in the world seats the player.")]
        [SerializeField] Transform deskTarget;
        [SerializeField] float maxClickDistance = 100f;
        [SerializeField] LayerMask clickMask = ~0;

        CameraController cameraController;
        Camera clickCamera;
        Action onSeated;
        bool waitingForClick;
        bool running;

        public bool IsRunning => running;

        public static PlayerSeatingIntro CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Seating Intro");
            return root.AddComponent<PlayerSeatingIntro>();
        }

        /// <summary>
        /// Begin the seating intro: hold the standing shot until the player clicks the desk, then walk the
        /// camera in and sit, invoking <paramref name="seatedCallback"/> once seated. In edit mode (or with
        /// no camera) it seats instantly so the round still starts.
        /// </summary>
        public void Play(CameraController camera, Action seatedCallback)
        {
            if (running)
                return;

            cameraController = camera;
            onSeated = seatedCallback;

            if (!Application.isPlaying || cameraController == null)
            {
                Finish();
                return;
            }

            running = true;
            ResolveDeskTarget();
            clickCamera = Camera.main;
            cameraController.BeginSeatingIntro();
            waitingForClick = true;
        }

        void Update()
        {
            if (!waitingForClick)
                return;

            var mouse = Mouse.current;

            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            if (clickCamera == null)
                clickCamera = Camera.main;

            if (clickCamera == null)
                return;

            var ray = clickCamera.ScreenPointToRay(mouse.position.ReadValue());

            if (!Physics.Raycast(ray, out var hit, maxClickDistance, clickMask))
                return;

            if (!IsDeskHit(hit))
                return;

            SitDown();
        }

        void SitDown()
        {
            // Stop listening so a second click during the walk can't re-trigger, then walk in and sit.
            waitingForClick = false;
            cameraController.PlaySeatingApproach(Finish);
        }

        // True when the ray hit the desk (or any part of it). With no desk wired, any world hit counts so the
        // intro still resolves rather than trapping the player at the standing shot.
        bool IsDeskHit(RaycastHit hit)
        {
            if (deskTarget == null)
                return true;

            var hitTransform = hit.collider.transform;
            return hitTransform == deskTarget || hitTransform.IsChildOf(deskTarget);
        }

        void ResolveDeskTarget()
        {
            if (deskTarget != null)
                return;

            // Prefer the whole table group so any part of the desk (top, rail, stains) counts as a click.
            var group = GameObject.Find("Saloon Table");
            if (group != null)
            {
                deskTarget = group.transform;
                return;
            }

            var table = GameObject.Find("Table");
            if (table != null)
                deskTarget = table.transform;
        }

        void Finish()
        {
            running = false;
            waitingForClick = false;

            var callback = onSeated;
            onSeated = null;
            callback?.Invoke();
        }
    }
}
