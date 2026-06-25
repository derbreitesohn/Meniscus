using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// While the player has coins selected (before they pour), floats the payout multiplier that pour would
    /// earn — "×2.4 PAYOUT" — above the selected coins, in gold. It is purely a reward read: nothing about
    /// risk, no spill odds, no danger colour or tremble. Because the multiplier is the boldness payout
    /// (bigger coins and a fuller glass push it up) folded with the combo and any active shop boosts, the
    /// number climbing as the player stacks more coins is the whole signal. Self-installs in any gameplay
    /// scene; tracks the coins on screen each frame and hides when nothing is selected.
    /// </summary>
    [DisallowMultipleComponent]
    public class CoinSelectionPreview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // RuntimeInitializeOnLoadMethod fires once per launch, so an AfterSceneLoad install misses a
            // gameplay scene reached later via the menu (LoadScene). Subscribe instead and install whenever
            // a scene finishes loading; the guards in Install keep it to gameplay scenes and avoid dupes.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            // Only in gameplay scenes (those that own the glass), and never duplicate.
            if (Object.FindAnyObjectByType<GlassManager>() == null)
                return;
            if (Object.FindAnyObjectByType<CoinSelectionPreview>() != null)
                return;

            new GameObject("Coin Selection Preview").AddComponent<CoinSelectionPreview>();
        }

        static readonly Color MultiplierColor = new(1f, 0.84f, 0.33f, 1f); // warm gold — this is money
        const string LabelColorHex = "D8B255";                             // muted gold for the "PAYOUT" tag

        // Reference-resolution pixels the label floats above the coins' projected centre.
        const float VerticalOffsetPixels = 128f;

        PlayerController player;
        GlassManager glass;
        EconomyManager economy;
        Camera viewCamera;

        Canvas canvas;
        RectTransform canvasRect;
        RectTransform panel;
        Text payoutText;
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

        // Recompute the read when the selection changes (it is stable between changes; only the on-screen
        // position follows the coins each frame, in LateUpdate).
        void Refresh()
        {
            if (canvas == null)
                return;

            ResolveReferences();

            var coins = player != null ? player.SelectedCoins : null;
            var count = coins?.Count ?? 0;

            if (count == 0 || glass == null || economy == null)
            {
                selectionActive = false;
                SetVisible(false);
                return;
            }

            var addedRiskWeight = 0f;
            for (var i = 0; i < count; i++)
            {
                if (coins[i] != null)
                    addedRiskWeight += Mathf.Max(0f, coins[i].riskContribution);
            }

            // The spill chance this pour would face (the relief-adjusted chance at the post-drop fill) feeds
            // the boldness payout — it is never shown, only converted into the reward multiplier below.
            var afterWeight = Mathf.Min(
                glass.CurrentOverflowProbability + addedRiskWeight, GameConstants.MaxOverflowProbability);
            var spillAfter = glass.CalculateCurrentTrueSpillChance(afterWeight);

            var multiplier = economy.PreviewSafeDropMultiplier(coins, spillAfter);
            payoutText.text = $"×{multiplier:0.0}\n<size=22><color=#{LabelColorHex}>PAYOUT</color></size>";

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

            // No background card — just the multiplier, centre-anchored so it can be driven to any screen
            // point each frame (see LateUpdate).
            var root = new GameObject("Preview", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            panel = root.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(360f, 110f);

            payoutText = RuntimeUiFactory.CreateText(
                panel, "Payout", "", Vector2.zero, new Vector2(360f, 110f),
                46, MultiplierColor, TextAnchor.MiddleCenter, true);
            payoutText.supportRichText = true;                        // smaller, muted "PAYOUT" tag via tags
            payoutText.verticalOverflow = VerticalWrapMode.Overflow;  // never clip the two-line read
            payoutText.raycastTarget = false;
            AddReadableOutline(payoutText);

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
