using Meniscus.Core;
using Meniscus.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Meniscus.UI
{
    /// <summary>
    /// Top-left debug readout (a tuning aid, not shipped UI): the glass's current chance to overspill
    /// on a pour right now, and — next to it — how much spill chance the player's currently-selected
    /// coin combination would add if poured this turn. Each coin's risk already bakes in the round's
    /// risk multiplier, so the combo figure is the contribution of those coin sizes "for that round".
    /// Both numbers are read off the same <see cref="GlassManager"/> projection the live pour and the
    /// payout preview use (including the combo claw-back), so they match what actually resolves.
    /// Self-installs once in any gameplay scene that owns the glass, like <see cref="CoinSelectionPreview"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpillDebugReadout : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            // Subscribe (rather than install once) so a gameplay scene reached later via the menu still
            // gets a readout; the guards in Install keep it to gameplay scenes and avoid duplicates.
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Install();

        static void Install()
        {
            if (Object.FindAnyObjectByType<GlassManager>() == null)
                return;
            if (Object.FindAnyObjectByType<SpillDebugReadout>() != null)
                return;

            new GameObject("Spill Debug Readout").AddComponent<SpillDebugReadout>();
        }

        static readonly Color ReadoutColor = new(0.95f, 0.78f, 0.45f, 1f); // warm amber, easy to spot

        GlassManager glass;
        PlayerController player;
        Canvas canvas;
        Text readout;
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
            if (glass != null)
                glass.ProbabilityChanged -= OnProbabilityChanged;

            if (player != null)
                player.SelectionChanged -= Refresh;
        }

        // Install order may run before the glass/player exist; keep retrying until both are wired.
        void Update()
        {
            if (glass == null || player == null)
            {
                ResolveReferences();
                Subscribe();
                Refresh();
            }
        }

        // Safety net: catch a selection change that didn't reach us via the event.
        void LateUpdate()
        {
            var count = player != null && player.SelectedCoins != null ? player.SelectedCoins.Count : 0;

            if (count != lastSeenCount)
            {
                lastSeenCount = count;
                Refresh();
            }
        }

        void ResolveReferences()
        {
            if (glass == null) glass = FindAnyObjectByType<GlassManager>();
            if (player == null) player = FindAnyObjectByType<PlayerController>();
        }

        void Subscribe()
        {
            if (glass != null)
            {
                glass.ProbabilityChanged -= OnProbabilityChanged;
                glass.ProbabilityChanged += OnProbabilityChanged;
            }

            if (player != null)
            {
                player.SelectionChanged -= Refresh;
                player.SelectionChanged += Refresh;
            }
        }

        void OnProbabilityChanged(float _) => Refresh();

        void Refresh()
        {
            if (readout == null)
                return;

            ResolveReferences();

            if (glass == null)
            {
                readout.text = string.Empty;
                return;
            }

            // Standing chance the glass spills on a pour at the current dome fill (relief-adjusted, the
            // player's view) — "how likely it is to overspill" right now.
            var spillNow = glass.CurrentTrueSpillChance;

            // How much the currently-selected coin combination would push that up if poured this turn.
            // ProjectFillAfterDrop applies the same combo claw-back as the live pour, so the forecast
            // matches what would actually resolve.
            var coins = player != null ? player.SelectedCoins : null;
            var count = coins?.Count ?? 0;
            var combo = 0f;

            if (count > 0)
            {
                var addedRisk = 0f;
                for (var i = 0; i < count; i++)
                {
                    if (coins[i] != null)
                        addedRisk += Mathf.Max(0f, coins[i].riskContribution);
                }

                var afterFill = glass.ProjectFillAfterDrop(addedRisk, count);
                var spillAfter = glass.CalculateCurrentTrueSpillChance(afterFill);
                combo = Mathf.Max(0f, spillAfter - spillNow);
            }

            readout.text = $"OVERSPILL {spillNow:0.#}%   |   COINS +{combo:0.#}%";
        }

        void BuildUi()
        {
            if (canvas != null)
                return;

            canvas = RuntimeUiFactory.CreateOverlayCanvas(transform, "Spill Debug Canvas");
            canvas.sortingOrder = 120; // above the coin preview / HUD so the debug read is never hidden

            readout = RuntimeUiFactory.CreateText(
                canvas.transform, "Spill Debug", string.Empty, Vector2.zero, new Vector2(560f, 44f),
                22, ReadoutColor, TextAnchor.UpperLeft, bold: true);
            readout.raycastTarget = false;
            readout.horizontalOverflow = HorizontalWrapMode.Overflow;
            readout.verticalOverflow = VerticalWrapMode.Overflow;

            // Pin to the top-left corner (the wallet HUD owns the top-right).
            var rt = readout.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(18f, -16f);

            // No card behind it, so a dark outline keeps it legible over the bright table.
            var outline = readout.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            canvas.enabled = true;
        }
    }
}
