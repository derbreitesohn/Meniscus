using Meniscus.Core;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>
    /// The single shared "USE" control on the desk — a piece of 2D text the player clicks to commit
    /// the current selection (use the selected item, then pour the selected coins). It replaces the old
    /// per-item Use/Cancel tiles. This component is just the marker + visual state on the clickable text;
    /// the click is picked up by <see cref="DeskItemTray"/>'s raycast, which owns the commit decision and
    /// calls <see cref="PlayPress"/> for the tactile feedback.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeskUseButton : MonoBehaviour
    {
        static readonly Color Idle = new(0.62f, 0.48f, 0.2f);   // dim brass when nothing is selected
        static readonly Color Ready = new(1f, 0.86f, 0.36f);    // bright gold when there's something to commit

        const float PressScale = 0.86f;   // how far the text sinks on press before springing back
        const float Stiffness = 480f;
        const float Damping = 24f;        // under-critical → a small overshoot on the pop-back

        [SerializeField] TextMesh label;

        Spring pressSpring = new Spring(1f, Stiffness, Damping);
        Vector3 baseScale = Vector3.one;
        bool capturedBaseScale;
        bool animating;

        public void Initialize(TextMesh useText)
        {
            label = useText;
            SetReady(false);
        }

        /// <summary>Brightens the text when the player has a coin or item selected to commit.</summary>
        public void SetReady(bool ready)
        {
            if (label != null)
                label.color = ready ? Ready : Idle;
        }

        /// <summary>Springy "sink and pop back" when the shared USE control is clicked.</summary>
        public void PlayPress()
        {
            if (!Application.isPlaying)
                return;

            CaptureBaseScale();
            pressSpring.Snap(PressScale);   // sink immediately, then Update springs it back to rest
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

            pressSpring.Step(1f, Time.unscaledDeltaTime);
            transform.localScale = baseScale * pressSpring.Value;

            if (Mathf.Approximately(pressSpring.Value, 1f) && Mathf.Abs(pressSpring.Velocity) < 0.001f)
            {
                pressSpring.Snap(1f);
                transform.localScale = baseScale;
                animating = false;
            }
        }
    }
}
