using UnityEngine;
using UnityEngine.InputSystem;

namespace Meniscus.UI
{
    /// <summary>
    /// Decides when the UI should switch to its touch layout.
    ///
    /// The canvases are authored against a 1920x1080 landscape reference. A phone is
    /// both much narrower and driven by a fingertip rather than a cursor, so controls
    /// sized for a mouse end up too small to hit and a box parked a fixed distance
    /// from the screen centre no longer sits where it was meant to.
    /// </summary>
    public static class UiScale
    {
        /// Reference canvas height; UI positions are authored against it.
        public const float ReferenceHeight = 1080f;

        /// Half the reference height, i.e. the distance from centre to the bottom edge.
        public const float ReferenceHalfHeight = ReferenceHeight * 0.5f;

        /// True when a fingertip is the pointer, or the viewport is portrait/very narrow.
        /// The aspect check also catches a desktop browser window dragged narrow, where the
        /// same larger controls are the better fit.
        public static bool Touch
        {
            get
            {
                if (Touchscreen.current != null)
                    return true;

                return Screen.height > 0 && (float)Screen.width / Screen.height < 1.2f;
            }
        }

        /// Multiplier for control sizes and label text in the touch layout. Sized so a
        /// button clears the ~9mm minimum touch target on a typical phone once the
        /// canvas scaler has shrunk the reference resolution to fit.
        public static float ControlScale => Touch ? 1.6f : 1f;
    }
}
