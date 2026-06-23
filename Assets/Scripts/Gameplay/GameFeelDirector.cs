using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    /// <summary>
    /// Orchestrates the "moment of truth" feel that a push-your-luck game lives on. It listens to the
    /// existing game events and adds, without modifying the core flow:
    ///   • a splash of droplets thrown up out of the glass as the coins reach the water;
    ///   • a brief slow-motion beat when a <em>risky</em> drop lands, so the verdict is a held breath
    ///     rather than a wait (time eases down, holds, then snaps back);
    ///   • a celebratory coin shower over the glass when the player wins a round;
    ///   • a continuous Tension parameter pushed to Wwise (an RTPC driven by the same spill chance the
    ///     liquid and camera use), so the recorded multi-tempo score can ramp with the danger.
    ///
    /// Self-wiring: <see cref="GameFeelBootstrap"/> spawns one at runtime, so no scene or Inspector
    /// setup is required for the splash, slow-mo, and shower. The audio hooks are optional Wwise
    /// references — assign them (and author a "Tension" RTPC) to light up the adaptive score.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameFeelDirector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] GameManager gameManager;
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;

        [Header("Surface Splash")]
        [SerializeField] bool enableSplash = true;
        [SerializeField] Color splashTint = new(0.62f, 0.4f, 0.12f, 0.9f);

        [Header("Slow-Motion Verdict")]
        [Tooltip("Master switch for the slow-motion beat on risky drops.")]
        [SerializeField] bool enableSlowMo = true;
        [Tooltip("True spill chance (%) a drop must reach for the slow-motion beat to fire.")]
        [SerializeField, Range(0f, 50f)] float slowMoSpillChanceThreshold = 20f;
        [Tooltip("Time scale at the deepest point of the slow-motion beat (1 = normal speed).")]
        [SerializeField, Range(0.05f, 1f)] float slowMoTimeScale = 0.35f;
        [SerializeField, Min(0f)] float slowMoRampIn = 0.08f;
        [SerializeField, Min(0f)] float slowMoHold = 0.22f;
        [SerializeField, Min(0f)] float slowMoRampOut = 0.5f;

        [Header("Win Celebration")]
        [SerializeField] bool enableWinShower = true;
        [SerializeField, Min(1)] int winShowerCoinCount = 22;

        [Header("Audio (optional Wwise hooks)")]
        [Tooltip("RTPC (0..100) driven by the live spill chance. Author a 'Tension' RTPC in Wwise and " +
                 "let the music container ramp on it to make the score tighten as the glass fills.")]
        [SerializeField] AK.Wwise.RTPC tensionRtpc;
        [Tooltip("Played when the player wins a round (the dealer overflowed).")]
        [SerializeField] AK.Wwise.Event winSting;
        [Tooltip("Played when the player busts (overflowed their own glass).")]
        [SerializeField] AK.Wwise.Event bustSting;

        static readonly Color CelebrationGold = new(1f, 0.78f, 0.28f, 1f);

        bool slowMoActive;
        Coroutine breakRoutine;
        float baseFixedDelta = 0.02f;

        void OnEnable()
        {
            ResolveReferences();
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
            RestoreTimeScale();
        }

        public void Configure(GameManager gm, GlassManager glass, EconomyManager economy)
        {
            Unsubscribe();
            gameManager = gm;
            glassManager = glass;
            economyManager = economy;

            if (isActiveAndEnabled)
                Subscribe();
        }

        void ResolveReferences()
        {
            if (gameManager == null)
                gameManager = FindAnyObjectByType<GameManager>();
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();
            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();
        }

        void Subscribe()
        {
            if (gameManager != null)
                gameManager.StateChanged += OnStateChanged;

            if (glassManager != null)
                glassManager.ProbabilityChanged += OnProbabilityChanged;

            if (economyManager != null)
            {
                economyManager.EarningsBanked += OnEarningsBanked;
                economyManager.RoundEarningsWiped += OnRoundEarningsWiped;
            }
        }

        void Unsubscribe()
        {
            if (gameManager != null)
                gameManager.StateChanged -= OnStateChanged;

            if (glassManager != null)
                glassManager.ProbabilityChanged -= OnProbabilityChanged;

            if (economyManager != null)
            {
                economyManager.EarningsBanked -= OnEarningsBanked;
                economyManager.RoundEarningsWiped -= OnRoundEarningsWiped;
            }
        }

        // ── surface-break beat: splash (always) + slow-motion (when risky) ───────────────

        /// <summary>
        /// Plays the surface-break feel at the exact moment the coins hit the water — the drop presentation
        /// conductor calls this on impact (not on a fixed delay from commit). A splash of droplets always,
        /// plus a brief slow-motion "held breath" beat when the drop is risky. The glass risk already
        /// reflects this drop, so tension reads straight off the live spill chance.
        /// </summary>
        public void PlaySurfaceBreak(IReadOnlyList<Coin> coins)
        {
            if (enableSplash)
                SpawnSplash(SplashStrengthForCoins(coins));

            var tense = enableSlowMo
                && glassManager != null
                && IsTenseDrop(glassManager.CurrentTrueSpillChance, slowMoSpillChanceThreshold);

            if (!tense)
                return;

            if (breakRoutine != null)
                StopCoroutine(breakRoutine);

            breakRoutine = StartCoroutine(SlowMoBeat());
        }

        IEnumerator SlowMoBeat()
        {
            slowMoActive = true;
            baseFixedDelta = Time.fixedDeltaTime;

            var total = slowMoRampIn + slowMoHold + slowMoRampOut;
            var elapsed = 0f;

            while (elapsed < total && slowMoActive)
            {
                elapsed += Time.unscaledDeltaTime;
                ApplyTimeScale(SlowMoTimeScale(
                    elapsed, slowMoRampIn, slowMoHold, slowMoRampOut, slowMoTimeScale));
                yield return null;
            }

            RestoreTimeScale();
            breakRoutine = null;
        }

        void SpawnSplash(float strength)
        {
            ResolveGlassSurface(out var center, out var radius);
            ParticleBurst.SpawnSplash(center, radius, splashTint, strength);
        }

        // Any state change away from resolving a drop ends a slow scale immediately, so it can never
        // bleed into the next turn, the shop, or the end screen.
        void OnStateChanged(GameState state)
        {
            if (slowMoActive && state != GameState.Resolution)
                RestoreTimeScale();
        }

        void ApplyTimeScale(float scale)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDelta * scale;
        }

        void RestoreTimeScale()
        {
            if (!slowMoActive && Mathf.Approximately(Time.timeScale, 1f))
                return;

            slowMoActive = false;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = baseFixedDelta;
        }

        // ── win celebration ───────────────────────────────────────────────────────────

        void OnEarningsBanked(int earned, int newBankTotal)
        {
            winSting?.Post(gameObject);

            if (!enableWinShower || earned <= 0)
                return;

            ResolveGlassSurface(out var center, out var radius);
            ParticleBurst.SpawnCoinShower(center, radius, CelebrationGold, winShowerCoinCount);
        }

        void OnRoundEarningsWiped() => bustSting?.Post(gameObject);

        // ── tension audio ───────────────────────────────────────────────────────────

        void OnProbabilityChanged(float probability)
        {
            if (glassManager == null || tensionRtpc == null || !tensionRtpc.IsValid())
                return;

            tensionRtpc.SetGlobalValue(TensionValue(glassManager.CurrentTrueSpillChance));
        }

        void ResolveGlassSurface(out Vector3 center, out float radius)
        {
            var glassObject = GameObject.FindGameObjectWithTag("Glass");
            var glass = glassObject != null ? glassObject.transform : null;
            var visual = glass != null
                ? glass.GetComponent<GlassVisualController>()
                : FindAnyObjectByType<GlassVisualController>();

            if (glass == null && visual != null)
                glass = visual.transform;

            if (glass == null)
            {
                center = Vector3.up;
                radius = 0.3f;
                return;
            }

            var surfaceY = visual != null ? visual.StableSurfaceLocalY : 0.62f;
            var localRadius = visual != null ? visual.SurfaceLocalRadius : 0.39f;
            center = CoinDropPresentationController.ColumnPointToWorld(glass, surfaceY);
            radius = CoinDropPresentationController.HorizontalWorldRadius(glass, localRadius);
        }

        // ── pure helpers (unit-tested) ────────────────────────────────────────────────

        /// <summary>A drop is "tense" enough for the slow-motion beat once its spill chance reaches the threshold.</summary>
        public static bool IsTenseDrop(float spillChance, float threshold) =>
            threshold > 0f && spillChance >= threshold;

        /// <summary>
        /// Time scale across the slow-motion envelope: 1 before it starts, eased down to
        /// <paramref name="slowScale"/> over <paramref name="rampIn"/>, held flat for
        /// <paramref name="hold"/>, eased back to 1 over <paramref name="rampOut"/>, and 1 after.
        /// </summary>
        public static float SlowMoTimeScale(
            float elapsed, float rampIn, float hold, float rampOut, float slowScale)
        {
            var clampedSlow = Mathf.Clamp(slowScale, 0.01f, 1f);

            if (elapsed <= 0f)
                return 1f;

            if (rampIn > 0f && elapsed < rampIn)
                return Mathf.Lerp(1f, clampedSlow, Mathf.SmoothStep(0f, 1f, elapsed / rampIn));

            var holdEnd = rampIn + hold;
            if (elapsed < holdEnd)
                return clampedSlow;

            var total = holdEnd + rampOut;
            if (rampOut > 0f && elapsed < total)
                return Mathf.Lerp(clampedSlow, 1f, Mathf.SmoothStep(0f, 1f, (elapsed - holdEnd) / rampOut));

            return 1f;
        }

        /// <summary>Maps a 0..MaxSpillChance live spill chance to a 0..100 tension RTPC value.</summary>
        public static float TensionValue(float spillChance) =>
            Mathf.Clamp01(spillChance / GameConstants.MaxSpillChance) * 100f;

        /// <summary>Splash strength for a drop: a bigger pour throws more water, clamped to a sane range.</summary>
        public static float SplashStrengthForCoins(IReadOnlyList<Coin> coins)
        {
            var count = coins?.Count ?? 0;
            return Mathf.Clamp(0.7f + 0.35f * count, 0.7f, 2.5f);
        }
    }
}
