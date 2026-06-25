using System;
using System.Collections.Generic;
using System.Collections;
using Meniscus.Gameplay;
using Meniscus.Items;
using Meniscus.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Meniscus.Core
{
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        [Header("Lifecycle")]
        [SerializeField] bool autoStart = true;
        [SerializeField] bool shopBetweenRoundsEnabled = true;
        [SerializeField] float endScreenDelay = 1.5f;
        [Tooltip("Play the camera 'walk in and take your seat' intro before the first coin toss of a match.")]
        [SerializeField] bool playSeatingIntro = true;
        [Tooltip("Play the dealer's wake-up monologue intro before the match. Takes precedence over the " +
                 "walk-in seating intro above.")]
        [SerializeField] bool playIntroMonologue = true;
        [Tooltip("Play the dealer's losing monologue and fade to black when the player wins the match.")]
        [SerializeField] bool playOutroMonologue = true;


        [Header("Managers")]
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] EnemyAI enemyAI;
        [SerializeField] CameraController cameraController;
        [SerializeField] PlayerSeatingIntro seatingIntro;
        [SerializeField] DialogueController dialogueController;
        [SerializeField] DealerMonologue dealerMonologue;
        [SerializeField] ShopManager shopManager;
        [SerializeField] EndScreenManager endScreenManager;
        [SerializeField] RoundWonBanner roundWonBanner;
        [SerializeField] RoundIntroCard roundIntroCard;
        [SerializeField] LossSequence lossSequence;
        [SerializeField] CoinTossOverlay coinTossOverlay;
        [SerializeField] CoinDropPresentationController dropPresentationController;
        [SerializeField] SaloonHudController saloonHudController;
        [SerializeField] MoneyHudWidget moneyHudWidget;
        [SerializeField] PlayerInventory playerInventory;
        [SerializeField] DeskItemTray deskItemTray;
        [Tooltip("Plays the per-item 'use' performance (e.g. the spyglass scoping the glass). Auto-added in " +
                 "play mode if left empty; it only animates — the effect itself is applied by TryUseItem.")]
        [SerializeField] ItemUsePresentationController itemUsePresentation;
        [Tooltip("Per-item desk models (e.g. spyglass for Bartender's Spectacles, bandana for Step Outside). " +
                 "Populate via Tools ▸ Meniscus ▸ Wire Item Models; items with no entry use the placeholder box.")]
        [SerializeField] ItemModelLibrary itemModels = new();

        [Header("Optional Coin Sources")]
        [SerializeField] Coin coinPrefab;
        [Tooltip("Per-size coin models. Assign Small/Medium/Big coin models to replace the placeholder cylinders.")]
        [SerializeField] CoinModelLibrary coinModels = new();
        [SerializeField] Transform playerCoinSpawnRoot;
        [SerializeField] Transform enemyCoinSpawnRoot;
        [SerializeField] List<Coin> handAuthoredPlayerCoins = new();
        [SerializeField] List<Coin> handAuthoredEnemyCoins = new();

        [Header("Primitive Coin Fallback Layout")]
        [SerializeField] Vector3 playerFallbackStart = new(-0.62f, 1.09f, -1.02f);
        [SerializeField] Vector3 enemyFallbackStart = new(-0.62f, 1.09f, 0.88f);
        [SerializeField] Vector3 coinSpacing = new(0.38f, 0f, 0f);
        [SerializeField] Vector3 primitiveCoinScale = new(0.42f, 0.05f, 0.42f);

        [Header("Coin Row Layout")]
        [Tooltip("Each round the coins are re-rolled (size and count), so they are re-packed into one compact, " +
                 "centred row after configuration. Gap is the world spacing between neighbouring coins; Y is " +
                 "the table-resting height; the two Z values place each actor's row (container-local).")]
        [SerializeField, Min(0f)] float coinRowGap = 0.015f;
        [SerializeField] float coinRowLocalY = 1.09f;
        [SerializeField] float coinRowLocalZPlayer = -0.78f;
        [SerializeField] float coinRowLocalZEnemy = 1.12f;

        [Header("Coin Pile (refill fly-in)")]
        [Tooltip("Where a fresh hand flies in FROM — the shared coin pile. Leave empty to use a point above " +
                 "the table centre (CoinPileFallbackLocalPosition, in the coin container's local space).")]
        [SerializeField] Transform coinPileAnchor;
        [SerializeField] Vector3 coinPileFallbackLocalPosition = new(0f, 1.45f, 0.17f);
        [Tooltip("Seconds each coin takes to fly from the pile to its slot, and the launch gap between " +
                 "successive coins so the hand fans out one at a time.")]
        [SerializeField, Min(0.01f)] float coinFlyDuration = 0.45f;
        [SerializeField, Min(0f)] float coinFlyStagger = 0.05f;

        [Header("Audio")]
        [SerializeField] AK.Wwise.Event coinFlip;
        [SerializeField] AK.Wwise.Event loseSound;

      


        readonly List<Coin> playerCoins = new();
        readonly List<Coin> enemyCoins = new();
        readonly List<Coin> generatedCoins = new();

        GameState currentState = GameState.StartRound;
        MatchOutcome lastMatchOutcome = MatchOutcome.None;
        int currentRound;
        int queuedEnemyForcedCoinCount;
        // Held while an item's "use" performance plays (e.g. the spyglass reveal), so a pour or a second
        // item use can't cut the beat short. Purely an input gate — it never changes the game state.
        bool itemPresentationLock;
        // Coins left in the shared reserve both hands draw from. Refills itself when drained (see
        // RefillHand / RestockSharedPile) so neither actor is ever left empty-handed mid-round.
        int sharedPile;

        public event Action<GameState> StateChanged;
        public event Action<TurnActor, IReadOnlyList<Coin>> DropCommitted;
        public event Action<GlassDropResult> DropResolved;
        public event Action<int> RoundStarted;
        public event Action<int, float> HandsRestocked;
        // Raised after an item's effect has been applied (and the item consumed), so the presentation layer
        // can play that item's "use" performance. The logical effect is already done by the time this fires.
        public event Action<ItemDefinition> ItemUsed;

        public GameState CurrentState => currentState;
        public int CurrentRound => currentRound;
        public bool ShopBetweenRoundsEnabled => shopBetweenRoundsEnabled;
        public MatchOutcome LastMatchOutcome => lastMatchOutcome;
        public IReadOnlyList<Coin> PlayerCoins => playerCoins;
        public IReadOnlyList<Coin> EnemyCoins => enemyCoins;
        public int SharedPileRemaining => sharedPile;
        public CoinModelLibrary CoinModels => coinModels;
        public ItemModelLibrary ItemModels => itemModels;
        public GlassManager GlassManager => glassManager;
        public EconomyManager EconomyManager => economyManager;
        public PlayerInventory Inventory => playerInventory;

        /// <summary>True while an item-use performance is playing; pours and further item uses are gated.</summary>
        public bool ItemPresentationActive => itemPresentationLock;

        /// <summary>Set by <see cref="ItemUsePresentationController"/> to gate input around a use performance.</summary>
        public void SetItemPresentationActive(bool active) => itemPresentationLock = active;

        void Awake()
        {
            EnsureInputSystemUiModules();
            // Re-apply settings chosen in the main menu (e.g. sound) now that this scene is live.
            GameSession.EnsureExists().ApplySettings();
            ResolveReferences();
        }

        void Start()
        {
            if (autoStart)
                StartMatch();
        }

        public void StartMatch()
        {
            ResolveReferences();
            lossSequence?.End(cameraController);   // restore the scene if the last match ended in the loss orbit
            NormalizeFallbackLayoutIfNeeded();
            EnsureShopPhaseEnabledForCurrentLoop();
            currentRound = 0;
            lastMatchOutcome = MatchOutcome.None;
            queuedEnemyForcedCoinCount = 0;
            economyManager?.ClearQueuedShopBonuses();
            playerInventory?.Clear();
            shopManager?.HideShop();
            endScreenManager?.Hide();
            roundWonBanner?.Hide();
            roundIntroCard?.Hide();
            coinTossOverlay?.Hide();

            // Open the match with the dealer's wake-up monologue (camera lifts off the table, then he talks);
            // the round only begins once it finishes. Falls back to the walk-in seating intro, then to an
            // instant start. Edit-mode / unwired scenes skip straight to the round so the old flow is preserved.
            if (ShouldPlayIntroMonologue())
                dealerMonologue.PlayIntro(cameraController, dialogueController, StartRound);
            else if (ShouldPlaySeatingIntro())
                seatingIntro.Play(cameraController, StartRound);
            else
                StartRound();
        }

        bool ShouldPlayIntroMonologue() =>
            Application.isPlaying && playIntroMonologue && dealerMonologue != null
            && dialogueController != null && cameraController != null;

        bool ShouldPlaySeatingIntro() =>
            Application.isPlaying && playSeatingIntro && seatingIntro != null && cameraController != null;

        bool ShouldPlayOutroMonologue() =>
            Application.isPlaying && playOutroMonologue && dealerMonologue != null && dialogueController != null;

        public void StartRound()
        {
            if (currentRound >= GameConstants.TotalRounds)
            {
                EnterGameOver("Round limit reached before a new round could begin.", MatchOutcome.PlayerWon);
                return;
            }

            currentRound++;
            itemPresentationLock = false;   // defensive: never carry a stale use-performance lock into a round
            TransitionTo(GameState.StartRound);
            cameraController?.SwitchCamera(CameraState.TableOverview);

            glassManager?.ResetGlass();
            economyManager?.ResetRoundEarnings();
            GenerateRoundCoinPools();
            RoundStarted?.Invoke(currentRound);

            BeginCoinToss();
        }

        // Each round opens on a coin toss: the player calls it and the winner takes the first turn. The
        // round intro card is held back until the toss resolves so the two interstitials don't overlap.
        // Without an overlay (EditMode tests / unwired scene) the player starts, preserving the old flow.
        void BeginCoinToss()
        {
            if (coinTossOverlay == null)
            {
                roundIntroCard?.Show(currentRound, GameConstants.TotalRounds);
                BeginPlayerTurn();
                return;
            }
           

            // Flip the real gold coin (the large/gold model); the overlay auto-fits and stands in with a
            // placeholder if no model is wired.
            coinTossOverlay.Show(
                coinModels.GetModelForSize(CoinSize.Large),
                coinModels.ModelScale,
                OnCoinTossDecided,
                coinFlip);
        }

        void OnCoinTossDecided(TurnActor starter)
        {
            roundIntroCard?.Show(currentRound, GameConstants.TotalRounds);

            if (starter == TurnActor.Enemy)
                BeginEnemyTurn();
            else
                BeginPlayerTurn();
        }

        public bool TryPlayerDropSelectedCoins(IReadOnlyList<Coin> selectedCoins)
        {
            if (currentState != GameState.PlayerTurn)
            {
                Debug.LogWarning($"[GameManager] Ignored player drop while state={currentState}.");
                return false;
            }

            // An item-use performance owns the moment (e.g. the spyglass reveal); don't pour through it.
            if (itemPresentationLock)
            {
                Debug.LogWarning("[GameManager] Ignored player drop while an item performance is playing.");
                return false;
            }

            if (!TryBuildValidDropList(selectedCoins, TurnActor.Player, out var validCoins))
                return false;

            ResolveDrop(TurnActor.Player, validCoins);
            return true;
        }

        public bool ExecuteEnemyDrop(IReadOnlyList<Coin> selectedCoins)
        {
            if (currentState != GameState.EnemyTurn)
            {
                Debug.LogWarning($"[GameManager] Ignored enemy drop while state={currentState}.");
                return false;
            }

            if (!TryBuildValidDropList(selectedCoins, TurnActor.Enemy, out var validCoins))
                return false;

            ResolveDrop(TurnActor.Enemy, validCoins);
            return true;
        }

        public void FinishShopPhase()
        {
            if (currentState != GameState.ShopPhase)
            {
                Debug.LogWarning($"[GameManager] FinishShopPhase ignored while state={currentState}.");
                return;
            }

            shopManager?.HideShop();

            if (currentRound >= GameConstants.TotalRounds)
                EnterGameOver("Finished final shop phase.", MatchOutcome.PlayerWon);
            else
                StartRound();
        }

        public bool TryUseItem(ItemDefinition item)
        {
            if (currentState != GameState.PlayerTurn)
            {
                Debug.LogWarning($"[GameManager] Ignored item use while state={currentState}.");
                return false;
            }

            // Don't let a second item interrupt a use performance mid-play (e.g. the spyglass reveal).
            if (itemPresentationLock)
            {
                Debug.LogWarning("[GameManager] Ignored item use while an item performance is playing.");
                return false;
            }

            if (item == null || playerInventory == null || !playerInventory.Has(item))
                return false;

            playerInventory.TryConsume(item);
            ItemEffectApplier.Apply(item, economyManager, glassManager, this);
            // The effect is now applied and the item consumed; let the presentation layer play its beat.
            ItemUsed?.Invoke(item);
            return true;
        }

        public void SkipPlayerTurn()
        {
            if (currentState != GameState.PlayerTurn)
            {
                Debug.LogWarning($"[GameManager] SkipPlayerTurn ignored while state={currentState}.");
                return;
            }

            BeginEnemyTurn();
        }

        /// <summary>
        /// Recasts one of the player's coins one size up (Small→Medium→Large): more risk on the glass, but a
        /// bigger payout — the Recast item's lever. Picks the largest coin that isn't already Gold so the
        /// boost lands where it matters most, reconfigures its risk/payout for the current round, swaps in the
        /// matching model, then re-packs the row (its footprint changed). No-op (warns) when every active
        /// player coin is already Large. Used via TryUseItem on the player's turn, so no coin is mid-selection.
        /// </summary>
        public bool UpgradePlayerCoinOneSize()
        {
            Coin best = null;

            for (var i = 0; i < playerCoins.Count; i++)
            {
                var coin = playerCoins[i];

                if (coin == null || coin.IsSpent || !coin.gameObject.activeInHierarchy ||
                    coin.size == CoinSize.Large)
                    continue;

                if (best == null || (int)coin.size > (int)best.size)
                    best = coin;
            }

            if (best == null)
            {
                Debug.LogWarning("[GameManager] Recast had no upgradeable player coin (all already Gold).");
                return false;
            }

            var newSize = best.size == CoinSize.Small ? CoinSize.Medium : CoinSize.Large;
            var roundRiskMultiplier = GameConstants.GetRoundRiskMultiplier(currentRound);

            best.Configure(
                newSize,
                GameConstants.GetRiskForSize(newSize) * roundRiskMultiplier,
                GameConstants.GetBasePayoutForSize(newSize),
                belongsToPlayer: true);
            best.ApplyModel(coinModels.GetModelForSize(newSize), coinModels.ModelScale);
            best.name = $"Player {GameConstants.GetDisplayNameForSize(newSize)} Coin (recast)";

            // The coin's footprint grew, so re-pack the row edge-to-edge (as a fresh round would).
            ArrangeCoinRow(TurnActor.Player, playerCoins);
            return true;
        }

        void ResolveDrop(TurnActor actor, IReadOnlyList<Coin> coins)
        {
            TransitionTo(GameState.Resolution);

            // The outcome (accumulated risk + RNG roll) is decided up front; the presentation stages every
            // visual/audio beat so the spill reveal lands at the dramatic moment, not the instant of commit.
            var result = glassManager != null
                ? glassManager.DropCoins(coins, actor)
                : new GlassDropResult(actor, 0f, 0f, 0f, 0f, false, coins.Count);

            DropCommitted?.Invoke(actor, coins);

            // In play mode the drop presentation conducts the whole sequence (close book, camera framing,
            // pick up, hold over the water, plunge, suspense, overflow reveal) and calls back when it
            // finishes, so the turn resolves on the reveal rather than a fixed timer. PlayDropSequence clones
            // the coin proxies synchronously, so it is safe to hide the source coins right after. Without a
            // presentation (or in edit-mode tests) the outcome resolves immediately.
            if (Application.isPlaying && dropPresentationController != null)
            {
                dropPresentationController.PlayDropSequence(actor, coins, result, () => FinishDrop(result, coins));
                MarkCoinsSpent(actor, coins);
            }
            else
            {
                MarkCoinsSpent(actor, coins);
                FinishDrop(result, coins);
            }
        }

        void FinishDrop(GlassDropResult result, IReadOnlyList<Coin> coins)
        {
            DropResolved?.Invoke(result);

            if (result.Overflowed)
            {
                ResolveOverflow(result);
                return;
            }

            ResolveSafeDrop(result, coins);
        }

        void ResolveSafeDrop(GlassDropResult result, IReadOnlyList<Coin> coins)
        {
            // The surface eases back between drops (tug-of-war recovery): a safe pour spikes the dome,
            // then it settles a little before the next actor pours, so the glass hovers at the brim over
            // many turns instead of racing over the top in a couple. Overflow skips this (the round is over).
            glassManager?.SettleSurface();

            if (result.Actor == TurnActor.Player)
            {
                // Pay for the danger this pour actually braved (the dome spill chance it survived), not the
                // fill that was already there — boldness, not bookkeeping.
                economyManager?.AwardSafeDrop(coins, result.TrueSpillChance);
                BeginEnemyTurn();
                return;
            }

            BeginPlayerTurn();
        }

        void ResolveOverflow(GlassDropResult result)
        {
            if (result.Actor == TurnActor.Player)
            {
                economyManager?.WipeCurrentRoundEarnings();
                EnterGameOver("Player overflowed the glass.", MatchOutcome.PlayerLost);
                return;
            }

            economyManager?.BankCurrentRoundEarnings();

            CompleteWonRound("Enemy overflowed the glass.");
        }

        void BeginPlayerTurn()
        {
            RemoveUnavailableCoins(playerCoins);
            RemoveUnavailableCoins(enemyCoins);

            // Top the hand back up from the shared pile (which refills itself) before the turn, so the
            // player always has coins to pour and is never stranded watching the Dealer play out a pile.
            RefillHand(TurnActor.Player);

            TransitionTo(GameState.PlayerTurn);
            cameraController?.SwitchCamera(CameraState.PlayerFocus);
        }

        void BeginEnemyTurn()
        {
            RemoveUnavailableCoins(playerCoins);
            RemoveUnavailableCoins(enemyCoins);

            RefillHand(TurnActor.Enemy);

            TransitionTo(GameState.EnemyTurn);
            cameraController?.SwitchCamera(CameraState.DealerFocus);

            if (enemyAI != null)
                enemyAI.BeginTurn(this);
            else
                Debug.LogWarning("[GameManager] EnemyAI reference is missing. Enemy turn cannot progress.");
        }

        /// <summary>
        /// Refills the actor's hand from the shared pile, but ONLY once it has run fully dry — at which
        /// point the pile flings out a brand-new full hand (<see cref="GameConstants.HandSize"/> coins,
        /// each freshly rolled). A partially-spent hand is left as-is, so coins deplete visibly
        /// (10 → … → 0 → a fresh 10 flies in) rather than trickling. The pile refills itself when drained
        /// (<see cref="RestockSharedPile"/>), so a hand can always be filled — that is what guarantees
        /// neither actor is ever left empty-handed mid-round. Re-packs the row, then plays the fly-in.
        /// </summary>
        void RefillHand(TurnActor actor)
        {
            var hand = actor == TurnActor.Player ? playerCoins : enemyCoins;

            // Only deal a fresh hand once the current one is fully spent; never trickle into a partial hand.
            if (hand.Count > 0)
                return;

            var handSize = Mathf.Max(1, GameConstants.HandSize);
            var dealt = new List<Coin>();

            while (hand.Count < handSize)
            {
                if (sharedPile <= 0)
                    RestockSharedPile();

                var coin = AcquireCoinForHand(actor, hand);

                if (coin == null)
                    break;   // safety net: could not produce a coin — avoid spinning forever

                coin.gameObject.SetActive(true);
                ConfigureCoinForRound(coin, actor, hand.Count);
                hand.Add(coin);
                dealt.Add(coin);
                sharedPile--;
            }

            if (dealt.Count == 0)
                return;

            ArrangeCoinRow(actor, hand);
            FlyHandInFromPile(actor, dealt);
        }

        // The shared reserve is bottomless by design: when it empties it just refills, because a round is
        // meant to end on an overflow, not on running out of coins. The event lets the HUD/feel layer play a
        // "fresh pile" beat; there is no state change, so the turn flow is never interrupted.
        void RestockSharedPile()
        {
            sharedPile = Mathf.Max(1, GameConstants.SharedPileSize);
            HandsRestocked?.Invoke(currentRound, GetCurrentRisk());
        }

        /// <summary>
        /// Play-mode-only flourish: sends a freshly-dealt hand flying out of the shared pile to the resting
        /// slots <see cref="ArrangeCoinRow"/> just assigned, fanned out one coin at a time. No-op in
        /// edit-mode / tests, where the coins simply appear at their slots.
        /// </summary>
        void FlyHandInFromPile(TurnActor actor, List<Coin> dealtCoins)
        {
            if (!Application.isPlaying)
                return;

            var pileWorld = ResolvePileWorldPosition(actor);

            for (var i = 0; i < dealtCoins.Count; i++)
            {
                if (dealtCoins[i] != null)
                    dealtCoins[i].FlyInFrom(pileWorld, i * coinFlyStagger, coinFlyDuration);
            }
        }

        Vector3 ResolvePileWorldPosition(TurnActor actor)
        {
            if (coinPileAnchor != null)
                return coinPileAnchor.position;

            var container = ResolveSpawnRoot(actor);
            return container != null
                ? container.TransformPoint(coinPileFallbackLocalPosition)
                : coinPileFallbackLocalPosition;
        }

        public void QueueEnemyForcedCoinCount(int coinCount)
        {
            queuedEnemyForcedCoinCount = Mathf.Clamp(coinCount, 0, GameConstants.MaxEnemyCoinsPerTurn);
        }

        public int ConsumeQueuedEnemyForcedCoinCount()
        {
            var forcedCount = queuedEnemyForcedCoinCount;
            queuedEnemyForcedCoinCount = 0;
            return forcedCount;
        }

        void CompleteWonRound(string reason)
        {
            // The final round wraps straight into the match end screen (which already reads "YOU WON").
            if (currentRound >= GameConstants.TotalRounds)
            {
                EnterGameOver(reason, MatchOutcome.PlayerWon);
                return;
            }

            // Mid-match wins pause on a "round won" banner; the shop (or next round) only begins once
            // the player presses Continue via ContinueAfterRoundWon.
            EnterRoundWon(reason);
        }

        void EnterRoundWon(string reason)
        {
            TransitionTo(GameState.RoundWon);
            cameraController?.SwitchCamera(CameraState.TableOverview);

            var bankedCash = economyManager == null ? 0 : economyManager.PlayerTotalBankedCash;
            roundWonBanner?.Show(
                "ROUND WON",
                $"{reason}\nBank: ${bankedCash}",
                ContinueAfterRoundWon);
        }

        public void ContinueAfterRoundWon()
        {
            if (currentState != GameState.RoundWon)
            {
                Debug.LogWarning($"[GameManager] ContinueAfterRoundWon ignored while state={currentState}.");
                return;
            }

            roundWonBanner?.Hide();

            if (shopBetweenRoundsEnabled)
                EnterShopPhase();
            else
                StartRound();
        }

        void EnterShopPhase()
        {
            if (!shopBetweenRoundsEnabled)
            {
                StartRound();
                return;
            }

            TransitionTo(GameState.ShopPhase);
            // The book lifts itself into a held pose in front of the camera (BookShopView), so the
            // camera holds the calm table framing instead of travelling to the book on the desk.
            cameraController?.SwitchCamera(CameraState.TableOverview);
            shopManager?.ShowShop();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Debug-only (editor / development builds): force the match to end with <paramref name="outcome"/>,
        /// driving the real end-of-match path — a PlayerLost runs the loss sequence and posts the lose
        /// sting, a PlayerWon shows the end screen. Wired from the on-screen <see cref="UI.StateDebugMenu"/>;
        /// never compiled into a release build.
        /// </summary>
        public void DebugForceOutcome(MatchOutcome outcome) =>
            EnterGameOver($"[debug] forced {outcome}.", outcome);
#endif

        void EnterGameOver(string reason, MatchOutcome outcome)
        {
            lastMatchOutcome = outcome;
            TransitionTo(GameState.GameOver);
            shopManager?.HideShop();

            // Fold the finished match into the persistent session so the main menu can show running stats.
            GameSession.EnsureExists().RecordMatchResult(
                outcome,
                economyManager == null ? 0 : economyManager.PlayerTotalBankedCash);

            // A loss is its own beat: strip the scene to just the spilled glass and orbit it until the player
            // restarts, instead of the flat end screen. EditMode tests have no LossSequence, so they fall
            // through to the unchanged end-screen path below.
            if (outcome == MatchOutcome.PlayerLost && lossSequence != null)
            {
                lossSequence.Begin(this, cameraController, "YOU LOST", loseSound);
                return;
            }

            // A win is its own beat too: the dealer gives his losing monologue, then the screen dims to black
            // and lifts to reveal the end screen. EditMode tests have no monologue, so they fall through to the
            // unchanged end-screen path below.
            if (outcome == MatchOutcome.PlayerWon && ShouldPlayOutroMonologue())
            {
                cameraController?.SwitchCamera(CameraState.TableOverview);
                dealerMonologue.PlayOutro(
                    cameraController,
                    dialogueController,
                    () => endScreenManager?.ShowOutcome(
                        outcome,
                        reason,
                        economyManager == null ? 0 : economyManager.PlayerTotalBankedCash));
                return;
            }

            cameraController?.SwitchCamera(CameraState.TableOverview);

            // Delay the end screen so the final drop animation can finish playing.
            StartCoroutine(ShowEndScreenAfterDelay(outcome, reason));
        }

        IEnumerator ShowEndScreenAfterDelay(MatchOutcome outcome, string reason)
        {
            yield return new WaitForSeconds(endScreenDelay);
            endScreenManager?.ShowOutcome(
                outcome,
                reason,
                economyManager == null ? 0 : economyManager.PlayerTotalBankedCash);
        }

        void GenerateRoundCoinPools()
        {
            ClearGeneratedCoins();
            playerCoins.Clear();
            enemyCoins.Clear();

            PopulateSceneCoinListsIfEmpty();

            // Idle every authored coin first; the opening deal re-activates only the coins it deals into a
            // hand (recycling these before spawning runtime ones), so spares never float on the table.
            DeactivateAll(handAuthoredPlayerCoins);
            DeactivateAll(handAuthoredEnemyCoins);

            // Open the round with a full shared reserve, then deal each actor a starting hand from it. Every
            // turn afterwards tops the active hand back up (RefillHand), so both sides always have coins and
            // neither is ever stranded. RefillHand re-packs each row edge-to-edge as it deals.
            sharedPile = Mathf.Max(1, GameConstants.SharedPileSize);
            RefillHand(TurnActor.Player);
            RefillHand(TurnActor.Enemy);

            if (playerCoins.Count == 0)
                Debug.LogWarning("[GameManager] Player hand is empty after the opening deal.");

            if (enemyCoins.Count == 0)
                Debug.LogWarning("[GameManager] Enemy hand is empty after the opening deal.");
        }

        void PopulateSceneCoinListsIfEmpty()
        {
            // Safety net for a scene whose coin lists were left unwired. In the normal, fully-wired case
            // both actors already have a usable coin, so skip the scene scan entirely. Otherwise each actor
            // is filled independently — a wired enemy list never suppresses discovery of an empty player
            // list — and inactive coins are included so a list still recovers after a previous round
            // deactivated spare coins.
            if (CoinPoolBuilder.HasUsableCoin(handAuthoredPlayerCoins) &&
                CoinPoolBuilder.HasUsableCoin(handAuthoredEnemyCoins))
                return;

            var sceneCoins = FindObjectsByType<Coin>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CoinPoolBuilder.FillMissingAuthoredCoinLists(handAuthoredPlayerCoins, handAuthoredEnemyCoins, sceneCoins);

            // Reaching here means a list was left unwired in the scene. Auto-discovery recovers gracefully,
            // but surface it so the misconfiguration is fixed rather than silently relied upon.
            Debug.LogWarning(
                "[GameManager] A coin list was unwired in the scene; auto-discovered coins as a fallback. " +
                "Run Tools ▸ Meniscus ▸ Set Up Coin References to wire them explicitly.");
        }

        static void DeactivateAll(List<Coin> coins)
        {
            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                coins[i].ResetVisualSelection();
                coins[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Produces one coin to deal into <paramref name="actor"/>'s hand: prefer recycling an idle authored
        /// coin (so the scene's hand-placed coins are reused), then an idle runtime coin already created for
        /// this actor, and only spawn a fresh runtime coin when none are free. The caller activates and
        /// configures it for the round.
        /// </summary>
        Coin AcquireCoinForHand(TurnActor actor, List<Coin> hand)
        {
            var authored = actor == TurnActor.Player ? handAuthoredPlayerCoins : handAuthoredEnemyCoins;
            var recycledAuthored = FindIdleCoin(authored, hand);

            if (recycledAuthored != null)
                return recycledAuthored;

            var wantsPlayerCoin = actor == TurnActor.Player;

            for (var i = 0; i < generatedCoins.Count; i++)
            {
                var coin = generatedCoins[i];

                if (coin != null && coin.isPlayerCoin == wantsPlayerCoin &&
                    !coin.gameObject.activeSelf && !hand.Contains(coin))
                    return coin;
            }

            var spawned = CreateRuntimeCoin(actor, hand.Count);
            generatedCoins.Add(spawned);
            return spawned;
        }

        // Returns an idle (deactivated, not-in-hand) coin from the list, pruning any null/destroyed entries
        // it passes. Used to recycle authored coins back into a hand before spawning new runtime ones.
        static Coin FindIdleCoin(List<Coin> coins, List<Coin> hand)
        {
            for (var i = coins.Count - 1; i >= 0; i--)
            {
                if (coins[i] == null)
                {
                    coins.RemoveAt(i);
                    continue;
                }

                if (!coins[i].gameObject.activeSelf && !hand.Contains(coins[i]))
                    return coins[i];
            }

            return null;
        }

        Coin CreateRuntimeCoin(TurnActor actor, int index)
        {
            var spawnRoot = ResolveSpawnRoot(actor);
            var localPosition = GetFallbackCoinLocalPosition(actor, index);
            var worldPosition = spawnRoot == null ? localPosition : spawnRoot.TransformPoint(localPosition);
            var rotation = Quaternion.identity;
            Coin coin;

            if (coinPrefab != null)
            {
                // Later production hook: replace this prefab with final modeled coin variants and table placement.
                coin = Instantiate(coinPrefab, worldPosition, rotation, spawnRoot);
            }
            else
            {
                var primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                primitive.name = $"{actor} Runtime Coin {index + 1}";
                primitive.transform.SetParent(spawnRoot, true);
                primitive.transform.position = worldPosition;
                primitive.transform.rotation = rotation;
                primitive.transform.localScale = primitiveCoinScale;
                coin = primitive.AddComponent<Coin>();
            }

            return coin;
        }

        Transform ResolveSpawnRoot(TurnActor actor)
        {
            var assignedRoot = actor == TurnActor.Player ? playerCoinSpawnRoot : enemyCoinSpawnRoot;

            if (assignedRoot != null)
                return assignedRoot;

            // No explicit spawn root wired: drop runtime top-up coins under the same container as the
            // authored coins so they share the table-local origin (authored coins live at container-local
            // y ~1.09) instead of floating at raw world fallback coordinates.
            var authored = actor == TurnActor.Player ? handAuthoredPlayerCoins : handAuthoredEnemyCoins;
            return CoinPoolBuilder.ResolveAuthoredParent(authored);
        }

        /// <summary>
        /// Packs an actor's active coins into a single straight row, centred on the container origin and
        /// resting on the table. Coins are ordered small→large and spaced edge-to-edge from their actual
        /// (post-configure) footprint plus a fixed gap, so whatever sizes this round rolled, the coins sit
        /// flush next to each other with no overlaps and no holes. Runs every round because both the sizes
        /// and the active count change. Each coin's resting position is recorded so selection lifts/returns
        /// to the new spot rather than the stale authored slot.
        /// </summary>
        void ArrangeCoinRow(TurnActor actor, List<Coin> pool)
        {
            if (pool == null || pool.Count == 0)
                return;

            var ordered = new List<Coin>(pool);
            ordered.Sort((a, b) =>
            {
                var bySize = ((int)a.size).CompareTo((int)b.size);
                return bySize != 0 ? bySize : string.CompareOrdinal(a.name, b.name);
            });

            var rowZ = actor == TurnActor.Player ? coinRowLocalZPlayer : coinRowLocalZEnemy;

            var totalWidth = coinRowGap * (ordered.Count - 1);

            for (var i = 0; i < ordered.Count; i++)
                totalWidth += CoinFootprintWidth(ordered[i]);

            var x = -totalWidth * 0.5f;

            for (var i = 0; i < ordered.Count; i++)
            {
                var width = CoinFootprintWidth(ordered[i]);
                x += width * 0.5f;
                ordered[i].SetRestingLocalPosition(new Vector3(x, coinRowLocalY, rowZ));
                x += width * 0.5f + coinRowGap;
            }
        }

        static float CoinFootprintWidth(Coin coin)
        {
            var scale = coin.transform.localScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        Vector3 GetFallbackCoinLocalPosition(TurnActor actor, int index)
        {
            const int columns = 4;
            var fallbackStart = actor == TurnActor.Player ? playerFallbackStart : enemyFallbackStart;
            var column = index % columns;
            var row = index / columns;
            var stagger = row % 2 == 0 ? 0f : 0.12f;

            return fallbackStart + new Vector3(
                column * coinSpacing.x + stagger,
                row * 0.035f,
                row * 0.28f);
        }

        void ConfigureCoinForRound(Coin coin, TurnActor actor, int index)
        {
            var size = RollCoinSizeForRound(currentRound);
            var displayName = GameConstants.GetDisplayNameForSize(size);

            // Later rounds scale every coin's risk up so the glass fills faster and the danger zone
            // arrives sooner — the round-to-round difficulty ramp. Payout is left unscaled here; the higher
            // risk feeds the greed multiplier, so a riskier round also pays more on a safe pour.
            var roundRiskMultiplier = GameConstants.GetRoundRiskMultiplier(currentRound);
            coin.Configure(
                size,
                GameConstants.GetRiskForSize(size) * roundRiskMultiplier,
                GameConstants.GetBasePayoutForSize(size),
                actor == TurnActor.Player);

            // Show the real per-size model when one is assigned; null falls back to the placeholder.
            coin.ApplyModel(coinModels.GetModelForSize(size), coinModels.ModelScale);

            coin.name = $"{actor} {displayName} Coin {index + 1}";
        }

        CoinSize RollCoinSizeForRound(int round)
        {
            var roll = UnityEngine.Random.value;

            if (round <= 1)
            {
                if (roll < 0.55f)
                    return CoinSize.Small;

                return roll < 0.9f ? CoinSize.Medium : CoinSize.Large;
            }

            if (round == 2)
            {
                if (roll < 0.35f)
                    return CoinSize.Small;

                return roll < 0.75f ? CoinSize.Medium : CoinSize.Large;
            }

            if (roll < 0.2f)
                return CoinSize.Small;

            return roll < 0.6f ? CoinSize.Medium : CoinSize.Large;
        }

        bool TryBuildValidDropList(
            IReadOnlyList<Coin> requestedCoins,
            TurnActor actor,
            out List<Coin> validCoins)
        {
            validCoins = new List<Coin>();

            if (requestedCoins == null || requestedCoins.Count == 0)
            {
                Debug.LogWarning($"[GameManager] {actor} attempted to drop no coins.");
                return false;
            }

            var sourcePool = actor == TurnActor.Player ? playerCoins : enemyCoins;
            var mustBePlayerCoin = actor == TurnActor.Player;

            for (var i = 0; i < requestedCoins.Count; i++)
            {
                var coin = requestedCoins[i];

                if (coin == null)
                {
                    Debug.LogWarning($"[GameManager] {actor} drop ignored null coin at index {i}.");
                    continue;
                }

                if (coin.IsSpent || !coin.gameObject.activeInHierarchy)
                {
                    Debug.LogWarning($"[GameManager] {actor} drop ignored unavailable coin {coin.name}.");
                    continue;
                }

                if (coin.isPlayerCoin != mustBePlayerCoin)
                {
                    Debug.LogWarning($"[GameManager] {actor} drop rejected wrong-owner coin {coin.name}.");
                    continue;
                }

                if (!sourcePool.Contains(coin))
                {
                    Debug.LogWarning($"[GameManager] {actor} drop rejected coin outside active pool: {coin.name}.");
                    continue;
                }

                if (!validCoins.Contains(coin))
                    validCoins.Add(coin);
            }

            if (validCoins.Count == 0)
            {
                Debug.LogWarning($"[GameManager] {actor} had no valid coins to drop.");
                return false;
            }

            return true;
        }

        void MarkCoinsSpent(TurnActor actor, IReadOnlyList<Coin> coins)
        {
            var sourcePool = actor == TurnActor.Player ? playerCoins : enemyCoins;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                coins[i].MarkSpent();
                sourcePool.Remove(coins[i]);
            }
        }

        static void RemoveUnavailableCoins(List<Coin> coins)
        {
            for (var i = coins.Count - 1; i >= 0; i--)
            {
                if (coins[i] == null || coins[i].IsSpent || !coins[i].gameObject.activeInHierarchy)
                    coins.RemoveAt(i);
            }
        }

        void ClearGeneratedCoins()
        {
            for (var i = generatedCoins.Count - 1; i >= 0; i--)
            {
                if (generatedCoins[i] == null)
                    continue;

                generatedCoins[i].gameObject.SetActive(false);
                Destroy(generatedCoins[i].gameObject);
            }

            generatedCoins.Clear();
        }

        void ResolveReferences()
        {
            if (glassManager == null)
                glassManager = FindAnyObjectByType<GlassManager>();

            if (economyManager == null)
                economyManager = FindAnyObjectByType<EconomyManager>();

            if (enemyAI == null)
                enemyAI = FindAnyObjectByType<EnemyAI>();

            if (cameraController == null)
                cameraController = FindAnyObjectByType<CameraController>();

            if (seatingIntro == null)
                seatingIntro = FindAnyObjectByType<PlayerSeatingIntro>();

            // Play-mode-only, like the coin toss / loss sequence: the intro builds an input-blocking prompt
            // canvas, so EditMode tests keep the null path (StartMatch → StartRound) and never spawn one.
            if (seatingIntro == null && Application.isPlaying)
                seatingIntro = PlayerSeatingIntro.CreateRuntimeFallback();

            if (dialogueController == null)
                dialogueController = FindAnyObjectByType<DialogueController>();

            // Play-mode-only, same as the other overlays: the dialogue box is pure presentation, so EditMode
            // tests keep the null path (instant StartRound / plain end screen) and never spawn a stray canvas.
            if (dialogueController == null && Application.isPlaying)
                dialogueController = DialogueController.CreateRuntimeFallback();

            if (dealerMonologue == null)
                dealerMonologue = FindAnyObjectByType<DealerMonologue>();

            if (dealerMonologue == null && Application.isPlaying)
                dealerMonologue = DealerMonologue.CreateRuntimeFallback();

            if (dropPresentationController == null)
                dropPresentationController = FindAnyObjectByType<CoinDropPresentationController>();

            if (shopManager == null)
                shopManager = FindAnyObjectByType<ShopManager>();

            if (shopManager == null)
            {
                shopManager = gameObject.AddComponent<ShopManager>();
                shopManager.Configure(null, economyManager, this);
            }

            if (endScreenManager == null)
                endScreenManager = FindAnyObjectByType<EndScreenManager>();

            if (endScreenManager == null)
                endScreenManager = EndScreenManager.CreateRuntimeFallback();

            if (roundWonBanner == null)
                roundWonBanner = FindAnyObjectByType<RoundWonBanner>();

            // Build the runtime fallback only in play mode: the banner is a play-mode-only gate, and
            // EnterRoundWon never auto-continues, so EditMode tests drive ContinueAfterRoundWon directly
            // without leaking a banner GameObject into the shared test scene.
            if (roundWonBanner == null && Application.isPlaying)
                roundWonBanner = RoundWonBanner.CreateRuntimeFallback();

            if (roundIntroCard == null)
                roundIntroCard = FindAnyObjectByType<RoundIntroCard>();

            // Play-mode-only, same as the round-won banner: the card is pure presentation, so EditMode
            // tests (no Update/coroutine tick) never spawn a stray canvas into the shared test scene.
            if (roundIntroCard == null && Application.isPlaying)
                roundIntroCard = RoundIntroCard.CreateRuntimeFallback();

            if (lossSequence == null)
                lossSequence = FindAnyObjectByType<LossSequence>();

            if (lossSequence == null && Application.isPlaying)
                lossSequence = LossSequence.CreateRuntimeFallback();

            if (coinTossOverlay == null)
                coinTossOverlay = FindAnyObjectByType<CoinTossOverlay>();

            // Play-mode-only: the toss blocks on a button click, so EditMode tests must keep the null path
            // (BeginCoinToss → player starts) and never spawn an input-blocking canvas into the test scene.
            if (coinTossOverlay == null && Application.isPlaying)
                coinTossOverlay = CoinTossOverlay.CreateRuntimeFallback();

            if (dropPresentationController == null)
                dropPresentationController = FindAnyObjectByType<CoinDropPresentationController>();

            if (dropPresentationController == null)
            {
                dropPresentationController = gameObject.AddComponent<CoinDropPresentationController>();
                dropPresentationController.Configure(this, FindGlassTransform());
            }

            if (playerInventory == null)
                playerInventory = FindAnyObjectByType<PlayerInventory>();

            if (playerInventory == null)
                playerInventory = gameObject.AddComponent<PlayerInventory>();

            if (deskItemTray == null)
                deskItemTray = FindAnyObjectByType<DeskItemTray>();

            if (deskItemTray == null)
            {
                deskItemTray = gameObject.AddComponent<DeskItemTray>();
                deskItemTray.Configure(this, playerInventory);
            }

            if (itemUsePresentation == null)
                itemUsePresentation = FindAnyObjectByType<ItemUsePresentationController>();

            // Play-mode-only, like the drop conductor: it builds an overlay canvas and held props, so
            // EditMode tests keep the null path (TryUseItem just applies the effect, no performance).
            if (itemUsePresentation == null && Application.isPlaying)
            {
                itemUsePresentation = gameObject.AddComponent<ItemUsePresentationController>();
                itemUsePresentation.Configure(this);
            }

            if (saloonHudController == null)
                saloonHudController = FindAnyObjectByType<SaloonHudController>();

            if (saloonHudController == null)
            {
                saloonHudController = gameObject.AddComponent<SaloonHudController>();
                saloonHudController.Configure(null, null, this);
            }

            if (moneyHudWidget == null)
                moneyHudWidget = FindAnyObjectByType<MoneyHudWidget>();

            if (moneyHudWidget == null)
                moneyHudWidget = gameObject.AddComponent<MoneyHudWidget>();

            if (glassManager == null)
                Debug.LogWarning("[GameManager] GlassManager reference is missing.");

            if (economyManager == null)
                Debug.LogWarning("[GameManager] EconomyManager reference is missing.");
        }

        void EnsureShopPhaseEnabledForCurrentLoop()
        {
            if (shopBetweenRoundsEnabled)
                return;

            shopBetweenRoundsEnabled = true;
        }

        void NormalizeFallbackLayoutIfNeeded()
        {
            if (playerFallbackStart.y >= 0.5f && enemyFallbackStart.y >= 0.5f)
                return;

            playerFallbackStart = new Vector3(-0.62f, 1.09f, -1.02f);
            enemyFallbackStart = new Vector3(-0.62f, 1.09f, 0.88f);
            coinSpacing = new Vector3(0.38f, 0f, 0f);
        }

        void TransitionTo(GameState newState)
        {
            if (currentState == newState)
            {
                StateChanged?.Invoke(currentState);
                return;
            }

            currentState = newState;
            StateChanged?.Invoke(currentState);
        }

        float GetCurrentRisk() =>
            glassManager == null ? 0f : glassManager.CurrentOverflowProbability;

        static Transform FindGlassTransform()
        {
            var glassObject = GameObject.FindGameObjectWithTag("Glass");
            return glassObject == null ? null : glassObject.transform;
        }

        static void EnsureInputSystemUiModules()
        {
            var eventSystem = FindAnyObjectByType<EventSystem>();

            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem");
                eventSystemObject.AddComponent<EventSystem>();
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
            }

            var standaloneModules = FindObjectsByType<StandaloneInputModule>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (var i = 0; i < standaloneModules.Length; i++)
            {
                var standaloneModule = standaloneModules[i];

                if (standaloneModule == null)
                    continue;

                var eventSystemObject = standaloneModule.gameObject;
                standaloneModule.enabled = false;

                if (eventSystemObject.GetComponent<InputSystemUIInputModule>() == null)
                    eventSystemObject.AddComponent<InputSystemUIInputModule>();

                Destroy(standaloneModule);
            }

            var eventSystems = FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (var i = 0; i < eventSystems.Length; i++)
            {
                if (eventSystems[i] == null)
                    continue;

                if (eventSystems[i].GetComponent<InputSystemUIInputModule>() == null)
                {
                    eventSystems[i].gameObject.AddComponent<InputSystemUIInputModule>();
                }
            }
        }
    }
}
