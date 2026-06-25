using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// A short line of effect text that floats above a world point (the glass) during an item-use
    /// performance — "NEXT POUR ×2", "DEALER POURS 2", "DEALER DRINKS" — then fades out. Built procedurally
    /// and positioned by projecting its world anchor to screen each frame, like <see cref="CoinSelectionPreview"/>.
    /// A one-shot: <see cref="Show"/> while the beat plays, <see cref="SetAlpha"/> to fade, then <see cref="Hide"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class ItemEffectBanner : MonoBehaviour
    {
        public static ItemEffectBanner Create() =>
            new GameObject("Item Effect Banner").AddComponent<ItemEffectBanner>();

        static readonly Color TextColor = new(1f, 0.86f, 0.4f, 1f);   // warm gold
        const float VerticalOffsetPixels = 150f;

        Camera viewCamera;
        Transform anchor;
        Vector3 worldOffset;

        Canvas canvas;
        RectTransform canvasRect;
        RectTransform panel;
        Text text;

        void OnEnable() => BuildUi();

        public void Show(string message, Transform worldAnchor, Vector3 anchorWorldOffset)
        {
            anchor = worldAnchor;
            worldOffset = anchorWorldOffset;

            if (text != null)
                text.text = message;

            SetAlpha(0f);
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        public void SetAlpha(float alpha)
        {
            if (text == null)
                return;

            var c = text.color;
            c.a = Mathf.Clamp01(alpha);
            text.color = c;
        }

        // Track the world anchor each frame so the text floats over the glass through the camera push-in.
        void LateUpdate()
        {
            if (canvas == null || !canvas.enabled)
                return;

            if (viewCamera == null)
                viewCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

            if (viewCamera == null || anchor == null)
                return;

            var screenPoint = viewCamera.WorldToScreenPoint(anchor.position + worldOffset);

            if (screenPoint.z <= 0f)
                return;   // behind the camera — leave the text where it was

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out var local);
            panel.anchoredPosition = local + new Vector2(0f, VerticalOffsetPixels);
        }

        void SetVisible(bool visible)
        {
            if (canvas != null && canvas.enabled != visible)
                canvas.enabled = visible;
        }

        void BuildUi()
        {
            if (canvas != null)
                return;

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Item Effect Banner Canvas");
            canvas.sortingOrder = 205;   // above the table HUD; alongside the scope overlay
            canvasRect = canvas.GetComponent<RectTransform>();

            var root = new GameObject("Banner", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            panel = root.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(900f, 140f);

            text = RuntimeUiFactory.CreateText(
                panel, "Text", "", Vector2.zero, new Vector2(900f, 140f),
                60, TextColor, TextAnchor.MiddleCenter, true);
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);

            canvas.enabled = false;
        }
    }
}
