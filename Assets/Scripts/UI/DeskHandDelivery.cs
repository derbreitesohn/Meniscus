using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// Tosses a freshly ordered item onto the desk: an arm flicks in from the side to a fixed toss point
    /// near the desk edge, releases the item, and the item arcs to its slot on its own while the arm
    /// swings back off-screen. Because the arm never reaches across to the slot itself, its length can't
    /// clip into the desk/glass when a slot is far away. Deliveries are queued and played one at a time.
    ///
    /// Everything is authored in the inspector: leave <see cref="arm"/> empty and a low-poly placeholder
    /// arm (a forearm box plus a hand block) is built in code (swap it for a modelled / rigged arm later
    /// by dropping that arm into the field). All poses are expressed in this component's LOCAL space,
    /// which it shares with the <see cref="DeskItemTray"/> it lives on — local X runs along the desk's
    /// near edge, +Z faces the camera, +Y is up — so the same coordinates line up with the tray's item
    /// slots. The arm enters from whichever side <see cref="restLocalPosition"/> sits on (its X sign), and
    /// the forearm trails off-screen toward that side; flip that X to bring it in from the other side.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskHandDelivery : MonoBehaviour
    {
        [Header("Arm Visual")]
        [Tooltip("The arm that flicks in. Leave empty to auto-build a low-poly placeholder arm (forearm " +
                 "+ hand) at runtime; drop a modelled/rigged arm here later to replace it without code " +
                 "changes. Its origin is treated as the wrist (where the item is gripped).")]
        [SerializeField] Transform arm;
        [Tooltip("Size of the placeholder hand block at the wrist (ignored when an Arm is assigned).")]
        [SerializeField] Vector3 placeholderHandSize = new(0.2f, 0.12f, 0.3f);
        [Tooltip("How far the placeholder forearm extends back off-screen from the wrist, toward the rest " +
                 "side. Make it long enough to leave the frame.")]
        [SerializeField, Min(0f)] float placeholderArmLength = 1.8f;
        [Tooltip("Cross-section thickness of the placeholder forearm (ignored when an Arm is assigned).")]
        [SerializeField, Min(0.01f)] float placeholderArmThickness = 0.14f;
        [Tooltip("Tint of the auto-built placeholder arm (ignored when an Arm is assigned).")]
        [SerializeField] Color placeholderColor = new(0.85f, 0.68f, 0.52f);

        [Header("Approach (tray-local space)")]
        [Tooltip("Where the wrist waits between deliveries, off the desk and ideally out of frame. Its X " +
                 "sign picks the side the arm enters from (and which way the forearm trails off-screen). " +
                 "Flip X to bring the arm in from the opposite side.")]
        [SerializeField] Vector3 restLocalPosition = new(-1.9f, 0.12f, -0.05f);
        [Tooltip("Where the carried item sits relative to the wrist (its 'grip') — point it toward the " +
                 "desk centre (opposite the rest side).")]
        [SerializeField] Vector3 itemGripLocalOffset = new(0.11f, 0.03f, 0f);

        [Header("Toss")]
        [Tooltip("How far in from the rest side the arm swings before flicking the item (0 = stays at the " +
                 "rest X, 1 = reaches the desk centre line). Keep it small so the arm stays near the edge " +
                 "and never crosses the desk.")]
        [SerializeField, Range(0f, 1f)] float tossReachFraction = 0.55f;
        [Tooltip("Wrist height at the toss point — raise it so the throw arcs up and over onto the desk.")]
        [SerializeField] float tossHeight = 0.22f;
        [Tooltip("Small vertical arc on the arm's own swing in/out (purely the arm's gesture, not the " +
                 "thrown item).")]
        [SerializeField, Min(0f)] float armSwingArcHeight = 0.08f;

        [Header("Thrown Item")]
        [Tooltip("Seconds for the tossed item to fly from the hand to its slot.")]
        [SerializeField, Min(0f)] float itemFlightSeconds = 0.45f;
        [Tooltip("Peak height of the item's throw arc above the straight line to its slot.")]
        [SerializeField, Min(0f)] float itemArcHeight = 0.3f;

        [Header("Timing")]
        [Tooltip("Pause after an order is placed before the arm starts tossing the item(s) in, so the " +
                 "throw reads as a beat after the purchase rather than landing instantly. Applied once at " +
                 "the start of a delivery run; the item waits off-screen until it elapses.")]
        [SerializeField, Min(0f)] float preThrowDelaySeconds = 0.4f;
        [Tooltip("Seconds for the arm to swing in from rest to the toss point.")]
        [SerializeField, Min(0f)] float tossSeconds = 0.28f;
        [Tooltip("Seconds for the arm to swing back off-screen after releasing the item (runs while the " +
                 "item is still in flight).")]
        [SerializeField, Min(0f)] float retractSeconds = 0.34f;
        [Tooltip("Use unscaled time so the arm/throw keep moving during the slow-motion verdict (matches " +
                 "the desk's selection lift and glow).")]
        [SerializeField] bool useUnscaledTime = true;

        struct Delivery
        {
            public DeskItemBox Box;
            public Vector3 Slot;
        }

        readonly Queue<Delivery> queue = new();
        Delivery current;
        Coroutine pump;

        /// <summary>
        /// Hand the given box in to <paramref name="slotLocalPosition"/> (the box's resting slot in tray
        /// space). The box must already be parented to this component's transform. Queued behind any
        /// delivery already in flight.
        /// </summary>
        public void Deliver(DeskItemBox box, Vector3 slotLocalPosition)
        {
            if (box == null)
                return;

            // No coroutines while inactive — set the box down immediately so it never gets stranded
            // off-desk (e.g. the tray was disabled the same frame an order came in).
            if (!isActiveAndEnabled)
            {
                box.IsBeingDelivered = false;
                box.SetRestPosition(slotLocalPosition);
                return;
            }

            box.IsBeingDelivered = true;
            // Park the box where the hand waits so it doesn't flash at the tray centre before pickup.
            box.transform.localPosition = restLocalPosition + itemGripLocalOffset;

            queue.Enqueue(new Delivery { Box = box, Slot = slotLocalPosition });

            if (pump == null)
                pump = StartCoroutine(Pump());
        }

        void OnDisable()
        {
            // Finalise anything in flight or queued so no box is left mid-air with IsBeingDelivered set
            // (which would keep the tray's Layout from snapping it to its slot on re-enable).
            if (pump != null)
            {
                StopCoroutine(pump);
                pump = null;
            }

            Settle(current);
            current = default;

            while (queue.Count > 0)
                Settle(queue.Dequeue());
        }

        static void Settle(Delivery delivery)
        {
            if (delivery.Box == null)
                return;

            delivery.Box.IsBeingDelivered = false;
            delivery.Box.SetRestPosition(delivery.Slot);
        }

        IEnumerator Pump()
        {
            EnsureArm();
            arm.gameObject.SetActive(true);
            arm.localPosition = restLocalPosition;

            // A beat after the purchase before the first toss — the item waits off-screen in hand.
            if (preThrowDelaySeconds > 0f)
                yield return Wait(preThrowDelaySeconds);

            while (queue.Count > 0)
            {
                current = queue.Dequeue();
                var box = current.Box;

                if (box == null)
                {
                    current = default;
                    continue;
                }

                box.IsBeingDelivered = true;

                var tossWrist = TossWristPose();
                arm.localPosition = restLocalPosition;

                // Swing the arm in from the side, carrying the item to the toss point.
                yield return Move(restLocalPosition, tossWrist, tossSeconds, armSwingArcHeight, box);

                // Flick: the item flies to its slot on its own while the arm swings back off-screen. The
                // arm never reaches across the desk, so its length can't clip into far slots.
                yield return ThrowAndRetract(box, current.Slot, tossWrist);

                current = default;
            }

            arm.gameObject.SetActive(false);
            pump = null;
        }

        // The wrist pose at the moment of release: a fraction of the way in from the rest side, raised to
        // tossHeight. Derived from restLocalPosition so it follows the entry side automatically.
        Vector3 TossWristPose() =>
            new(restLocalPosition.x * (1f - tossReachFraction), tossHeight, restLocalPosition.z);

        // Throw the item to its slot and swing the arm back to rest at the same time.
        IEnumerator ThrowAndRetract(DeskItemBox box, Vector3 slot, Vector3 tossWrist)
        {
            var throwFrom = box.transform.localPosition;   // == tossWrist + grip
            var duration = Mathf.Max(itemFlightSeconds, retractSeconds);
            var landed = false;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

                // Item: parabolic arc toward the slot, then hand off to the tray's own easing on landing.
                if (!landed)
                {
                    var f = itemFlightSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / itemFlightSeconds);
                    var p = Vector3.Lerp(throwFrom, slot, f);
                    p.y += itemArcHeight * Mathf.Sin(Mathf.PI * f);
                    box.transform.localPosition = p;

                    if (f >= 1f)
                    {
                        box.IsBeingDelivered = false;
                        box.SetRestPosition(slot);
                        landed = true;
                    }
                }

                // Arm: swing back to rest concurrently with the throw.
                var s = Mathf.SmoothStep(0f, 1f, retractSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / retractSeconds));
                var wrist = Vector3.Lerp(tossWrist, restLocalPosition, s);
                wrist.y += armSwingArcHeight * 0.5f * Mathf.Sin(Mathf.PI * s);
                arm.localPosition = wrist;

                yield return null;
            }

            if (!landed)
            {
                box.IsBeingDelivered = false;
                box.SetRestPosition(slot);
            }

            arm.localPosition = restLocalPosition;
        }

        // Waits the given seconds on the same clock the motions use (unscaled by default, so it still
        // ticks during the slow-motion verdict).
        IEnumerator Wait(float seconds)
        {
            var elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        // Eases the arm's wrist from -> to over seconds with an optional vertical arc, carrying the box
        // (offset by the grip) when one is given.
        IEnumerator Move(Vector3 from, Vector3 to, float seconds, float arc, DeskItemBox carry)
        {
            if (seconds <= 0f)
            {
                Place(to, arc, 1f, carry);
                yield break;
            }

            var elapsed = 0f;

            while (elapsed < seconds)
            {
                elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                var s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                Place(Vector3.Lerp(from, to, s), arc, s, carry);
                yield return null;
            }

            Place(to, arc, 1f, carry);
        }

        void Place(Vector3 wristPos, float arc, float s, DeskItemBox carry)
        {
            wristPos.y += arc * Mathf.Sin(Mathf.PI * s);
            arm.localPosition = wristPos;

            if (carry != null)
                carry.transform.localPosition = wristPos + itemGripLocalOffset;
        }

        void EnsureArm()
        {
            if (arm != null)
                return;

            var root = new GameObject("Delivery Arm (Placeholder)");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = restLocalPosition;

            // Hand block sits at the wrist (the root origin), where the item is gripped.
            BuildArmPart(root.transform, "Hand", placeholderHandSize, Vector3.zero);

            // Forearm extends back toward the rest side (off-screen) so the placeholder reads as a whole
            // arm coming in from that side, not a floating hand. Direction follows the rest X sign.
            var outward = restLocalPosition.x < 0f ? -1f : 1f;
            var forearmSize = new Vector3(placeholderArmLength, placeholderArmThickness, placeholderArmThickness);
            var forearmPos = new Vector3(outward * (placeholderHandSize.x * 0.5f + placeholderArmLength * 0.5f), 0f, 0f);
            BuildArmPart(root.transform, "Forearm", forearmSize, forearmPos);

            arm = root.transform;
        }

        void BuildArmPart(Transform parent, string partName, Vector3 size, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = localPos;

            // Drop the auto-collider so the placeholder never intercepts the tray's click raycast.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                    Destroy(collider);
                else
                    DestroyImmediate(collider);
            }

            DeskItemTrayBuilder.ApplyColor(go.GetComponent<Renderer>(), placeholderColor);
        }
    }
}
