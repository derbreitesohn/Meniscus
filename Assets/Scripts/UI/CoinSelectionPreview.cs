using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// While the player has coins selected (before they pour), floats a label above the selected coins
    /// previewing what that pour would do: the payout multiplier (greed × combo × any queued shop bonus,
    /// matching <see cref="EconomyManager.AwardSafeDrop"/>) and the spill chance the coins would add to
    /// the glass (current → after, with the increase). Tracks the coins on screen each frame and hides
    /// when nothing is selected. Self-installs in any gameplay scene — no scene or Inspector wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinSelectionPreview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Only in gameplay scenes (those that own the glass), and never duplicate.
            if (Object.FindAnyObjectByType<GlassManager>() == null)
                return;
            if (Object.FindAnyObjectByType<CoinSelectionPreview>() != null)
                return;

            new GameObject("Coin Selection Preview").AddComponent<CoinSelectionPreview>();
        }

        // Palette borrowed from MoneyHudWidget so the preview reads as part of the same HUD.
        static readonly Color GoldBright = new(0.97f, 0.83f,  0.31f,  1f);
        static readonly Color MultHot   = new(1f,     0.55f,  0.18f,  1f);
        static readonly Color RiskCalm  = new(0.86f,  0.79f,  0.56f,  1f);
        static readonly Color RiskHot   = new(0.96f,  0.33f,  0.16f,  1f);

        // Reference-resolution pixels the label floats above the coins' projected centre.
        const float VerticalOffsetPixels = 116f;

        PlayerController player;
        GlassManager glass;
        EconomyManager economy;
        Camera viewCamera;

        Canvas canvas;
        RectTransform canvasRect;
        RectTransform panel;
        Text multiplierText;
        Text riskText;
        bool selectionActive;
        int lastSeenCount = -1;

        void OnEnable()
        {
            ResolveReferences();
            BuildUi();
            Subscribe();
            Refresh();
        }

        void OnDisable()
        {
            if (player != null)
                player.SelectionChanged -= Refresh;
        }

        void Update()
        {
            // The player is a scene object present by AfterSceneLoad, but retry resolving/subscribing in
            // case install order put us first. Content is event-driven; only positioning runs per frame.
            if (player == null)
            {
                ResolveReferences();
                Subscribe();
                Refresh();
            }
        }

        // Position after the camera has finished moving for the frame, so the label doesn't lag a frame
        // behind during camera blends.
        void LateUpdate()
        {
            if (canvas == null)
                return;

            // Safety net: if the selection changed without the event reaching us, recompute now so the
            // preview always reflects the current selection (event-driven content + polled fallback).
            var count = player != null && player.SelectedCoins != null ? player.SelectedCoins.Count : 0;
            if (count != lastSeenCount)
            {
                lastSeenCount = count;
                Refresh();
            }

            if (!selectionActive || !TryGetSelectionScreenPoint(out var screenPoint))
            {
                SetVisible(false);
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out var local);
            panel.anchoredPosition = local + new Vector2(0f, VerticalOffsetPixels);
            SetVisible(true);
        }

        void ResolveReferences()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (glass == null) glass = FindAnyObjectByType<GlassManager>();
            if (economy == null) economy = FindAnyObjectByType<EconomyManager>();
            if (viewCamera == null) viewCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        }

        void Subscribe()
        {
            if (player == null)
                return;

            player.SelectionChanged -= Refresh;
            player.SelectionChanged += Refresh;
        }

        // Recompute the previewed numbers when the selection changes (the figures are stable between
        // changes; only the on-screen position follows the coins each frame, in LateUpdate).
        void Refresh()
        {
            if (canvas == null)
                return;

            ResolveReferences();

            var coins = player != null ? player.SelectedCoins : null;
            var count = coins?.Count ?? 0;

            if (count == 0 || glass == null)
            {
                selectionActive = false;
                SetVisible(false);
                return;
            }

            // Greed is charged on the risk already in the glass (AwardSafeDrop uses RiskBeforeDrop).
            var currentRisk = glass.CurrentOverflowProbability;

            var baseTotal = 0;
            var addedRiskWeight = 0f;
            for (var i = 0; i < count; i++)
            {
                var coin = coins[i];
                if (coin == null)
                    continue;

                baseTotal += coin.basePayout;
                addedRiskWeight += Mathf.Max(0f, coin.riskContribution);
            }

            // Payout multiplier: greed × combo (× shop bonus), expressed as (final payout ÷ raw value) —
            // the same figure the money pop-up celebrates after a safe drop.
            var multiplier = 1f;
            if (economy != null)
            {
                var payout = economy.CalculateSafeDropPayout(coins, currentRisk);
                if (economy.NextSafeDropPayoutMultiplier > 1f)
                    payout = Mathf.RoundToInt(payout * economy.NextSafeDropPayoutMultiplier);

                multiplier = baseTotal > 0 ? (float)payout / baseTotal : 1f;
            }

            // Spill chance this pour would add: current vs. the chance at the post-drop fill.
            var spillBefore = glass.CurrentTrueSpillChance;
            var afterWeight = Mathf.Min(currentRisk + addedRiskWeight, GameConstants.MaxOverflowProbability);
            var spillAfter = glass.CalculateCurrentTrueSpillChance(afterWeight);
            var spillIncrease = Mathf.Max(0f, spillAfter - spillBefore);

            var combo = count > 1;
            multiplierText.text = combo ? $"×{multiplier:0.0}   COMBO" : $"×{multiplier:0.0}";
            multiplierText.color = Color.Lerp(GoldBright, MultHot, Mathf.InverseLerp(1f, 3f, multiplier));

            riskText.text = $"+{spillIncrease:0}% spill   →   {spillAfter:0}%";
            riskText.color = Color.Lerp(RiskCalm, RiskHot, Mathf.Clamp01(spillAfter / GameConstants.MaxSpillChance));

            selectionActive = true;
            // Positioned + shown in LateUpdate.
        }

        // Average the selected coins' world positions and project to screen. Returns false when there is
        // no usable camera/selection or the coins are behind the camera.
        bool TryGetSelectionScreenPoint(out Vector3 screenPoint)
        {
            screenPoint = default;

            if (viewCamera == null)
                viewCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();

            var coins = player != null ? player.SelectedCoins : null;
            var count = coins?.Count ?? 0;
            if (viewCamera == null || count == 0)
                return false;

            var sum = Vector3.zero;
            var n = 0;
            for (var i = 0; i < count; i++)
            {
                if (coins[i] == null)
                    continue;

                sum += coins[i].transform.position;
                n++;
            }

            if (n == 0)
                return false;

            screenPoint = viewCamera.WorldToScreenPoint(sum / n);
            return screenPoint.z > 0f; // in front of the camera
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

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Coin Selection Preview Canvas");
            canvas.sortingOrder = 100; // above the table HUD
            canvasRect = canvas.GetComponent<RectTransform>();

            // No background card — just the text, centre-anchored so it can be driven to any screen
            // point each frame (see LateUpdate).
            var root = new GameObject("Preview", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            panel = root.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(360f, 88f);

            multiplierText = RuntimeUiFactory.CreateText(
                panel, "Multiplier", "", new Vector2(0f, 20f), new Vector2(360f, 46f),
                40, GoldBright, TextAnchor.MiddleCenter, true);
            multiplierText.raycastTarget = false;
            AddReadableOutline(multiplierText);

            riskText = RuntimeUiFactory.CreateText(
                panel, "Risk", "", new Vector2(0f, -24f), new Vector2(360f, 34f),
                23, RiskCalm, TextAnchor.MiddleCenter, false);
            riskText.raycastTarget = false;
            AddReadableOutline(riskText);

            canvas.enabled = false;
        }

        // With no card behind it, the text needs a dark outline to stay legible over the bright table.
        static void AddReadableOutline(Text text)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
        }
    }
}
