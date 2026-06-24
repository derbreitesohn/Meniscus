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


        [Header("Managers")]
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] EnemyAI enemyAI;
        [SerializeField] CameraController cameraController;
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

        [Header("Audio")]
      


        readonly List<Coin> playerCoins = new();
        readonly List<Coin> enemyCoins = new();
        readonly List<Coin> generatedCoins = new();

        GameState currentState = GameState.StartRound;
        MatchOutcome lastMatchOutcome = MatchOutcome.None;
        int currentRound;
        int queuedEnemyForcedCoinCount;

        public event Action<GameState> StateChanged;
        public event Action<TurnActor, IReadOnlyList<Coin>> DropCommitted;
        public event Action<GlassDropResult> DropResolved;
        public event Action<int> RoundStarted;
        public event Action<int, float> HandsRestocked;

        public GameState CurrentState => currentState;
        public int CurrentRound => currentRound;
        public bool ShopBetweenRoundsEnabled => shopBetweenRoundsEnabled;
        public MatchOutcome LastMatchOutcome => lastMatchOutcome;
        public IReadOnlyList<Coin> PlayerCoins => playerCoins;
        public IReadOnlyList<Coin> EnemyCoins => enemyCoins;
        public CoinModelLibrary CoinModels => coinModels;
        public GlassManager GlassManager => glassManager;
        public EconomyManager EconomyManager => economyManager;
        public PlayerInventory Inventory => playerInventory;

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
            StartRound();
        }

        public void StartRound()
        {
            if (currentRound >= GameConstants.TotalRounds)
            {
                EnterGameOver("Round limit reached before a new round could begin.", MatchOutcome.PlayerWon);
                return;
            }

            currentRound++;
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
                OnCoinTossDecided);
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

            if (item == null || playerInventory == null || !playerInventory.Has(item))
                return false;

            playerInventory.TryConsume(item);
            ItemEffectApplier.Apply(item, economyManager, glassManager, this);
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
            if (result.Actor == TurnActor.Player)
            {
                economyManager?.AwardSafeDrop(coins, result.RiskBeforeDrop);
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

            if (playerCoins.Count == 0)
            {
                if (enemyCoins.Count == 0)
                {
                    EnterRestockPhase("Both players ran out of coins.");
                    return;
                }

                BeginEnemyTurn();
                return;
            }

            TransitionTo(GameState.PlayerTurn);
            cameraController?.SwitchCamera(CameraState.PlayerFocus);
        }

        void BeginEnemyTurn()
        {
            RemoveUnavailableCoins(playerCoins);
            RemoveUnavailableCoins(enemyCoins);

            if (enemyCoins.Count == 0)
            {
                if (playerCoins.Count == 0)
                {
                    EnterRestockPhase("Both players ran out of coins.");
                    return;
                }

                BeginPlayerTurn();
                return;
            }

            TransitionTo(GameState.EnemyTurn);
            cameraController?.SwitchCamera(CameraState.DealerFocus);

            if (enemyAI != null)
                enemyAI.BeginTurn(this);
            else
                Debug.LogWarning("[GameManager] EnemyAI reference is missing. Enemy turn cannot progress.");
        }

        void EnterRestockPhase(string reason)
        {
            TransitionTo(GameState.RestockPhase);
            cameraController?.SwitchCamera(CameraState.TableOverview);

            GenerateRoundCoinPools();
            HandsRestocked?.Invoke(currentRound, GetCurrentRisk());
            BeginPlayerTurn();
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
                lossSequence.Begin(this, cameraController, "YOU LOST");
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
            PopulatePool(TurnActor.Player, handAuthoredPlayerCoins, playerCoins);
            PopulatePool(TurnActor.Enemy, handAuthoredEnemyCoins, enemyCoins);

            EnsurePoolHasTargetCoinCount(TurnActor.Player, playerCoins);
            EnsurePoolHasTargetCoinCount(TurnActor.Enemy, enemyCoins);

            // Coins were just (re)sized to this round's rolled sizes, so the authored row no longer fits;
            // re-pack each actor's active coins edge-to-edge into one compact, centred row.
            ArrangeCoinRow(TurnActor.Player, playerCoins);
            ArrangeCoinRow(TurnActor.Enemy, enemyCoins);

            if (playerCoins.Count == 0)
                Debug.LogWarning("[GameManager] Player coin pool is empty after generation.");

            if (enemyCoins.Count == 0)
                Debug.LogWarning("[GameManager] Enemy coin pool is empty after generation.");
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

        void PopulatePool(TurnActor actor, List<Coin> sourceCoins, List<Coin> targetPool)
        {
            for (var i = sourceCoins.Count - 1; i >= 0; i--)
            {
                if (sourceCoins[i] == null)
                    sourceCoins.RemoveAt(i);
            }

            if (sourceCoins.Count == 0)
                return;

            var activeCoinCount = Mathf.Min(sourceCoins.Count, GameConstants.GetCoinCountForRound(currentRound));

            for (var i = 0; i < sourceCoins.Count; i++)
            {
                var coin = sourceCoins[i];

                if (i >= activeCoinCount)
                {
                    coin.ResetVisualSelection();
                    coin.gameObject.SetActive(false);
                    continue;
                }

                coin.gameObject.SetActive(true);
                ConfigureCoinForRound(coin, actor, i);
                targetPool.Add(coin);
            }
        }

        void EnsurePoolHasTargetCoinCount(TurnActor actor, List<Coin> targetPool)
        {
            var targetCount = GameConstants.GetCoinCountForRound(currentRound);
            var missingCount = targetCount - targetPool.Count;

            if (missingCount <= 0)
                return;

            GenerateRuntimeCoins(actor, targetPool, missingCount, targetPool.Count);
        }

        void GenerateRuntimeCoins(TurnActor actor, List<Coin> targetPool, int count, int startIndex)
        {
            for (var i = 0; i < count; i++)
            {
                var index = startIndex + i;
                var coin = CreateRuntimeCoin(actor, index);
                ConfigureCoinForRound(coin, actor, index);
                generatedCoins.Add(coin);
                targetPool.Add(coin);
            }
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
            coin.Configure(
                size,
                GameConstants.GetRiskForSize(size),
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

            if (saloonHudController == null)
                saloonHudController = FindAnyObjectByType<SaloonHudController>();

            if (saloonHudController == null)
            {
                saloonHudController = gameObject.AddComponent<SaloonHudController>();
                saloonHudController.Configure(null, null, this, glassManager, economyManager);
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
