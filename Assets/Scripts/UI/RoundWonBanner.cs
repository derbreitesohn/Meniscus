using System;
using System.Collections;
using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// The between-rounds "you won this round" interstitial. When the player wins a non-final round
    /// it lifts in front of everything and waits on a Continue click before the shop (the diegetic
    /// book) opens, so the win reads as its own beat instead of snapping straight into the menu.
    /// Built procedurally to match the end screen / fallback shop, so no scene authoring is required.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundWonBanner : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] Text titleText;
        [SerializeField] Text detailText;
        [SerializeField] Button continueButton;

        Action onContinue;
        CanvasGroup group;
        Coroutine animateRoutine;

        void Awake()
        {
            WireContinueButton();
            Hide();
        }

        public static RoundWonBanner CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Round Won Banner");
            var banner = root.AddComponent<RoundWonBanner>();

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(root.transform, "Runtime Round Won Canvas", enabled: false);
            // Sit above the HUD overlay (sortingOrder 0) so the dimming scrim and prompt are not painted under it.
            canvas.sortingOrder = 50;
            // A group lets Show() fade the whole banner (scrim + title + button) in together.
            canvas.gameObject.AddComponent<CanvasGroup>();

            var scrim = RuntimeUiFactory.CreateImage(
                canvas.transform,
                "Runtime Round Won Scrim",
                new Vector2(1920f, 1080f),
                Vector2.zero,
                new Color(0.02f, 0.012f, 0.008f, 0.82f));

            var title = RuntimeUiFactory.CreateText(
                scrim.transform,
                "Runtime Round Won Title",
                "ROUND WON",
                new Vector2(0f, 72f),
                new Vector2(900f, 120f),
                74,
                new Color(0.95f, 0.82f, 0.52f),
                TextAnchor.MiddleCenter,
                bold: true);

            var detail = RuntimeUiFactory.CreateText(
                scrim.transform,
                "Runtime Round Won Detail",
                string.Empty,
                new Vector2(0f, -28f),
                new Vector2(900f, 100f),
                26,
                new Color(0.82f, 0.72f, 0.58f));

            var continueButton = RuntimeUiFactory.CreateButton(
                scrim.transform,
                "Runtime Continue Button",
                "CONTINUE",
                new Vector2(240f, 58f),
                new Vector2(0f, -148f),
                22,
                onClick: null,
                boldLabel: true);

            banner.Configure(canvas, title, detail, continueButton);
            return banner;
        }

        public void Configure(Canvas bannerCanvas, Text title, Text detail, Button continueBtn)
        {
            canvas = bannerCanvas;
            titleText = title;
            detailText = detail;
            continueButton = continueBtn;
            WireContinueButton();
            Hide();
        }

        /// <summary>
        /// Reveals the banner with the supplied copy and the callback to run when Continue is clicked.
        /// </summary>
        public void Show(string title, string detail, Action continueCallback)
        {
            onContinue = continueCallback;

            if (titleText != null)
                titleText.text = title;

            if (detailText != null)
                detailText.text = detail;

            if (canvas != null)
                canvas.enabled = true;

            // Edit-mode (tests) has no coroutine tick: leave it fully shown instantly.
            if (!Application.isPlaying)
                return;

            EnsureGroup();

            if (animateRoutine != null)
                StopCoroutine(animateRoutine);

            if (isActiveAndEnabled)
                animateRoutine = StartCoroutine(AnimateIn());
        }

        public void Hide()
        {
            if (animateRoutine != null)
            {
                StopCoroutine(animateRoutine);
                animateRoutine = null;
            }

            // Leave the banner ready to re-show cleanly next round.
            if (group != null)
                group.alpha = 1f;

            if (titleText != null)
                titleText.transform.localScale = Vector3.one;

            if (canvas != null)
                canvas.enabled = false;
        }

        void EnsureGroup()
        {
            if (group == null && canvas != null)
                group = canvas.GetComponent<CanvasGroup>() ?? canvas.gameObject.AddComponent<CanvasGroup>();
        }

        // Fades the banner up and pops the title in with a springy overshoot, so a round win lands as a
        // beat instead of snapping on screen. Unscaled time so it is unaffected by any slow-motion verdict.
        IEnumerator AnimateIn()
        {
            const float duration = 0.45f;
            var titleTransform = titleText != null ? titleText.transform : null;

            if (group != null)
                group.alpha = 0f;

            if (titleTransform != null)
                titleTransform.localScale = Vector3.one * 0.6f;

            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var p = elapsed / duration;

                if (group != null)
                    group.alpha = Mathf.Clamp01(p / 0.4f);   // fade in over the first ~40%

                if (titleTransform != null)
                    titleTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, Easing.OutBack(p));

                yield return null;
            }

            if (group != null)
                group.alpha = 1f;

            if (titleTransform != null)
                titleTransform.localScale = Vector3.one;

            animateRoutine = null;
        }

        public void Continue()
        {
            // Capture and clear first so a double-click cannot fire the callback twice.
            var callback = onContinue;
            onContinue = null;
            Hide();
            callback?.Invoke();
        }

        void WireContinueButton()
        {
            if (continueButton == null)
                return;

            continueButton.onClick.RemoveListener(Continue);
            continueButton.onClick.AddListener(Continue);
        }
    }
}
