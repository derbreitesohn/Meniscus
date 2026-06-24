using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// While the player has coins selected (before they pour), floats a single qualitative RISK read above
    /// the selected coins — STEADY / DICEY / HAIRY / BUST — telling them how dangerous that
    /// pour would be, with no numbers (the spill odds are deliberately never shown as a percentage). The
    /// label is colour-coded and trembles as the brim nears; the selected coins' glows are tinted to match,
    /// so the danger reads off the coins themselves too. Money is intentionally absent here — it is the
    /// reward you collect after a safe pour, not a figure to optimise mid-decision. Self-installs in any
    /// gameplay scene; tracks the coins on screen each frame and hides when nothing is selected.
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

        // The four reads, coolest → hottest. The glass opens at the brim, so STEADY is rare in practice —
        // most pours are at least DICEY, which is the point (every pour carries weight).
        enum RiskTier { Steady, Dicey, OnTheBrink, Certain }

        static readonly Color SteadyColor    = new(0.55f, 0.84f, 0.66f, 1f); // cool, calm green
        static readonly Color DiceyColor     = new(0.97f, 0.74f, 0.28f, 1f); // amber
        static readonly Color OnTheBrinkColor = new(0.99f, 0.45f, 0.17f, 1f); // hot orange
        static readonly Color CertainColor   = new(0.96f, 0.22f, 0.16f, 1f); // deep red — this pour WILL spill

        // Tier cut-offs on the would-be spill chance (0..100). Certain is anything at/over the brim, which
        // the dome model reports as a flat MaxOverflowProbability.
        const float DiceyThreshold = 25f;
        const float OnTheBrinkThreshold = 60f;

        // Reference-resolution pixels the label floats above the coins' projected centre.
        const float VerticalOffsetPixels = 116f;

        PlayerController player;
        GlassManager glass;
        Camera viewCamera;

        Canvas canvas;
        RectTransform canvasRect;
        RectTransform panel;
        Text riskWordText;
        bool selectionActive;
        RiskTier currentTier;
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
            // The label trembles as the danger climbs — a held breath when it gets HAIRY, a shudder once a
            // pour would BUST.
            panel.anchoredPosition = local + new Vector2(0f, VerticalOffsetPixels) + TrembleOffset();
            SetVisible(true);
        }

        void ResolveReferences()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (glass == null) glass = FindAnyObjectByType<GlassManager>();
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

            if (count == 0 || glass == null)
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

            // The spill chance this pour would face: the relief-adjusted chance at the post-drop fill. Never
            // shown as a number — only used to pick the qualitative tier and colour.
            var afterWeight = Mathf.Min(
                glass.CurrentOverflowProbability + addedRiskWeight, GameConstants.MaxOverflowProbability);
            var spillAfter = glass.CalculateCurrentTrueSpillChance(afterWeight);

            currentTier = TierFor(spillAfter);
            var tierColor = ColorFor(currentTier);

            riskWordText.text = WordFor(currentTier);
            riskWordText.color = tierColor;

            // Tint the selected coins' glows to match, so the danger reads off the coins, not just the label.
            for (var i = 0; i < count; i++)
                coins[i]?.SetSelectionGlowTint(tierColor);

            selectionActive = true;
            // Positioned + shown in LateUpdate.
        }

        static RiskTier TierFor(float spillChance)
        {
            if (spillChance >= GameConstants.MaxOverflowProbability)
                return RiskTier.Certain;     // at/over the brim — a guaranteed spill
            if (spillChance >= OnTheBrinkThreshold)
                return RiskTier.OnTheBrink;
            if (spillChance >= DiceyThreshold)
                return RiskTier.Dicey;
            return RiskTier.Steady;
        }

        static string WordFor(RiskTier tier) => tier switch
        {
            RiskTier.Steady => "STEADY",
            RiskTier.Dicey => "DICEY",
            RiskTier.OnTheBrink => "HAIRY",
            _ => "BUST",
        };

        static Color ColorFor(RiskTier tier) => tier switch
        {
            RiskTier.Steady => SteadyColor,
            RiskTier.Dicey => DiceyColor,
            RiskTier.OnTheBrink => OnTheBrinkColor,
            _ => CertainColor,
        };

        // A small, deterministic shudder for the hot tiers (no Random, so it can't desync). Reference pixels.
        Vector2 TrembleOffset()
        {
            var amplitude = currentTier switch
            {
                RiskTier.OnTheBrink => 2.5f,
                RiskTier.Certain => 5f,
                _ => 0f,
            };

            if (amplitude <= 0f)
                return Vector2.zero;

            var t = Time.unscaledTime;
            return new Vector2(Mathf.Sin(t * 47f) * amplitude, Mathf.Cos(t * 43f) * amplitude);
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

            // No background card — just the word, centre-anchored so it can be driven to any screen point
            // each frame (see LateUpdate).
            var root = new GameObject("Preview", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);
            panel = root.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(420f, 60f);

            riskWordText = RuntimeUiFactory.CreateText(
                panel, "RiskWord", "", Vector2.zero, new Vector2(420f, 60f),
                42, SteadyColor, TextAnchor.MiddleCenter, true);
            riskWordText.raycastTarget = false;
            AddReadableOutline(riskWordText);

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
