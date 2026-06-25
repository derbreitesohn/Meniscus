using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// A full-screen black overlay used to black the camera out for a beat — e.g. the Bandana pulled up over
    /// the player's face. Built procedurally on a high-sorting overlay canvas (above the scope / effect text);
    /// drive its opacity with <see cref="SetAlpha"/> (0 = clear, 1 = fully black). Never a raycast target, so
    /// it doesn't eat clicks. Reusable by any beat that needs a quick blackout / fade.
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenFadeOverlay : MonoBehaviour
    {
        public static ScreenFadeOverlay Create() =>
            new GameObject("Screen Fade Overlay").AddComponent<ScreenFadeOverlay>();

        Canvas canvas;
        Image black;

        void OnEnable() => BuildUi();

        public void SetAlpha(float alpha)
        {
            if (black == null)
                return;

            var a = Mathf.Clamp01(alpha);
            var c = black.color;
            c.a = a;
            black.color = c;

            // Only render while it actually tints anything.
            if (canvas != null)
                canvas.enabled = a > 0.001f;
        }

        void BuildUi()
        {
            if (canvas != null)
                return;

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Screen Fade Canvas");
            canvas.sortingOrder = 250;   // above everything (scope overlay, effect banner)

            var imageObject = new GameObject("Black", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvas.transform, false);

            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            black = imageObject.GetComponent<Image>();
            black.color = new Color(0f, 0f, 0f, 0f);
            black.raycastTarget = false;

            canvas.enabled = false;
        }
    }
}
