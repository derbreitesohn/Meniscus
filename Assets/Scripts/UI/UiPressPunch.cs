using Meniscus.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Meniscus.UI
{
    /// <summary>
    /// Springy press feedback for a uGUI button: it sinks while held and pops back with a slight overshoot
    /// on release, and exposes <see cref="Punch"/> for a programmatic "confirm" flourish (e.g. a successful
    /// purchase). RuntimeUiFactory attaches one to every procedurally-built button, so all the fallback UI
    /// shares a consistent tactile feel. Animates in play mode only; it is dormant (no per-frame work) once
    /// the scale has settled.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiPressPunch : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        const float PressedScale = 0.9f;   // how far the button sinks while held
        const float PunchScale = 1.18f;    // momentary overshoot for a confirm pop
        const float Stiffness = 520f;      // snappy
        const float Damping = 26f;         // under-critical → a small, quick overshoot

        Spring scaleSpring = new Spring(1f, Stiffness, Damping);
        Vector3 baseScale = Vector3.one;
        bool capturedBaseScale;
        float target = 1f;
        bool pressed;
        bool animating;

        public void OnPointerDown(PointerEventData eventData)
        {
            CaptureBaseScale();
            pressed = true;
            target = PressedScale;
            animating = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            target = 1f;
            animating = true;
        }

        /// <summary>Kicks a one-shot pop past full size that springs back to rest — a confirm flourish.</summary>
        public void Punch()
        {
            if (!Application.isPlaying)
                return;

            CaptureBaseScale();
            scaleSpring.Snap(PunchScale);
            target = pressed ? PressedScale : 1f;
            animating = true;
        }

        void CaptureBaseScale()
        {
            if (capturedBaseScale)
                return;

            baseScale = transform.localScale;
            capturedBaseScale = true;
        }

        void Update()
        {
            if (!animating)
                return;

            // Unscaled so the press still reads during the slow-motion verdict.
            scaleSpring.Step(target, Time.unscaledDeltaTime);
            transform.localScale = baseScale * scaleSpring.Value;

            // Settle exactly on target and stop ticking; a held button stays depressed without per-frame work.
            if (Mathf.Approximately(scaleSpring.Value, target) && Mathf.Abs(scaleSpring.Velocity) < 0.001f)
            {
                scaleSpring.Snap(target);
                transform.localScale = baseScale * target;
                animating = false;
            }
        }
    }
}
