using System.Collections;
using Meniscus.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// A brief, non-blocking interstitial shown when a round starts: the round number plus the stakes
    /// ("FIRST TO SPILL LOSES"), fading up with a springy title pop and auto-dismissing after a short
    /// hold so the round begins underneath it. Built procedurally like the round-won banner / end screen,
    /// so no scene authoring is required. It is scrim-less and never blocks raycasts, so the table stays
    /// visible and clickable while the card plays.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundIntroCard : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] CanvasGroup group;
        [SerializeField] Text titleText;
        [SerializeField] Text subtitleText;

        Coroutine routine;

        void Awake() => Hide();

        public static RoundIntroCard CreateRuntimeFallback()
        {
            var root = new GameObject("Runtime Round Intro Card");
            var card = root.AddComponent<RoundIntroCard>();

            var canvas = RuntimeUiFactory.CreateOverlayCanvas(root.transform, "Runtime Round Intro Canvas", enabled: false);
            // Above the HUD (0) but below the round-won banner (50) so an end-of-round banner always wins.
            canvas.sortingOrder = 40;

            var group = canvas.gameObject.AddComponent<CanvasGroup>();
            // A pure title card: never eat clicks meant for the coins/table underneath.
            group.blocksRaycasts = false;
            group.interactable = false;

            var title = RuntimeUiFactory.CreateText(
                canvas.transform, "Runtime Round Intro Title", "ROUND 1",
                new Vector2(0f, 60f), new Vector2(1100f, 160f), 96,
                new Color(0.96f, 0.86f, 0.55f), TextAnchor.MiddleCenter, bold: true);

            var subtitle = RuntimeUiFactory.CreateText(
                canvas.transform, "Runtime Round Intro Subtitle", "FIRST TO SPILL LOSES",
                new Vector2(0f, -52f), new Vector2(1100f, 80f), 34,
                new Color(0.82f, 0.72f, 0.58f));

            card.Configure(canvas, group, title, subtitle);
            return card;
        }

        public void Configure(Canvas cardCanvas, CanvasGroup cardGroup, Text title, Text subtitle)
        {
            canvas = cardCanvas;
            group = cardGroup;
            titleText = title;
            subtitleText = subtitle;
            Hide();
        }

        /// <summary>The round label ("ROUND 2 OF 3") so the card always matches the live round count.</summary>
        public static string RoundLabel(int round, int totalRounds) =>
            totalRounds > 0 ? $"ROUND {round} OF {totalRounds}" : $"ROUND {round}";

        public void Show(int round, int totalRounds)
        {
            if (titleText != null)
                titleText.text = RoundLabel(round, totalRounds);

            if (canvas != null)
                canvas.enabled = true;

            // Edit-mode (tests) has no coroutine tick: just leave the label set and the canvas enabled.
            if (!Application.isPlaying)
                return;

            if (routine != null)
                StopCoroutine(routine);

            if (isActiveAndEnabled)
                routine = StartCoroutine(PlayRoutine());
        }

        public void Hide()
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (canvas != null)
                canvas.enabled = false;
        }

        // Fade up + spring the title in, hold briefly, then fade out and disable — a self-dismissing card.
        // Unscaled time so it is unaffected by any slow-motion verdict still ramping out from the last drop.
        IEnumerator PlayRoutine()
        {
            const float fadeIn = 0.4f, hold = 1.1f, fadeOut = 0.4f;
            var titleTransform = titleText != null ? titleText.transform : null;

            if (group != null)
                group.alpha = 0f;

            for (var t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                var p = t / fadeIn;

                if (group != null)
                    group.alpha = Mathf.Clamp01(p / 0.5f);

                if (titleTransform != null)
                    titleTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.55f, 1f, Easing.OutBack(p));

                yield return null;
            }

            if (group != null) group.alpha = 1f;
            if (titleTransform != null) titleTransform.localScale = Vector3.one;

            for (var t = 0f; t < hold; t += Time.unscaledDeltaTime)
                yield return null;

            for (var t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
            {
                if (group != null)
                    group.alpha = 1f - Mathf.Clamp01(t / fadeOut);

                yield return null;
            }

            if (group != null)
                group.alpha = 0f;

            if (canvas != null)
                canvas.enabled = false;

            routine = null;
        }
    }
}
