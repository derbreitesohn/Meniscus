using System;
using System.Collections.Generic;
using System.Collections;
using Meniscus.Gameplay;
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
        [SerializeField] float resolveDelayAfterDrop = 1.0f;   // ~ dropAnimationSeconds (Münzen-Flugzeit)


        [Header("Managers")]
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] EnemyAI enemyAI;
        [SerializeField] CameraController cameraController;
        [SerializeField] ShopManager shopManager;
        [SerializeField] EndScreenManager endScreenManager;
        [SerializeField] CoinDropPresentationController dropPresentationController;
        [SerializeField] SaloonHudController saloonHudController;

        [Header("Optional Coin Sources")]
        [SerializeField] Coin coinPrefab;
        [SerializeField] Transform playerCoinSpawnRoot;
        [SerializeField] Transform enemyCoinSpawnRoot;
        [SerializeField] List<Coin> handAuthoredPlayerCoins = new();
        [SerializeField] List<Coin> handAuthoredEnemyCoins = new();

        [Header("Primitive Coin Fallback Layout")]
        [SerializeField] Vector3 playerFallbackStart = new(-0.62f, 1.09f, -1.02f);
        [SerializeField] Vector3 enemyFallbackStart = new(-0.62f, 1.09f, 0.88f);
        [SerializeField] Vector3 coinSpacing = new(0.38f, 0f, 0f);
        [SerializeField] Vector3 primitiveCoinScale = new(0.42f, 0.05f, 0.42f);

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
        public GlassManager GlassManager => glassManager;
        public EconomyManager EconomyManager => economyManager;

        void Awake()
        {
            EnsureInputSystemUiModules();
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
            NormalizeFallbackLayoutIfNeeded();
            EnsureShopPhaseEnabledForCurrentLoop();
            currentRound = 0;
            lastMatchOutcome = MatchOutcome.None;
            queuedEnemyForcedCoinCount = 0;
            economyManager?.ClearQueuedShopBonuses();
            shopManager?.HideShop();
            endScreenManager?.Hide();
            Debug.Log("[GameManager] Starting new match.");
            StartRound();
        }

        public void StartRound()
        {
            if (currentRound >= GameConstants.TotalRounds)
            {
                Debug.Log("[GameManager] StartRound requested after final round. Entering GameOver.");
                EnterGameOver("Round limit reached before a new round could begin.", MatchOutcome.PlayerWon);
                return;
            }

            currentRound++;
            TransitionTo(GameState.StartRound);
            cameraController?.SwitchCamera(CameraState.TableOverview);

            Debug.Log($"[GameManager] === Starting round {currentRound}/{GameConstants.TotalRounds} ===");

            glassManager?.ResetGlass();
            economyManager?.ResetRoundEarnings();
            GenerateRoundCoinPools();
            RoundStarted?.Invoke(currentRound);

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

            Debug.Log($"[GameManager] Shop phase finished after round {currentRound}.");
            shopManager?.HideShop();

            if (currentRound >= GameConstants.TotalRounds)
                EnterGameOver("Finished final shop phase.", MatchOutcome.PlayerWon);
            else
                StartRound();
        }

       void ResolveDrop(TurnActor actor, IReadOnlyList<Coin> coins)
        {
            cameraController?.SwitchCamera(CameraState.GlassZoom);
            TransitionTo(GameState.Resolution);
            DropCommitted?.Invoke(actor, coins);   

            var result = glassManager != null
                ? glassManager.DropCoins(coins, actor)
                : new GlassDropResult(actor, 0f, 0f, 0f, 0f, false, coins.Count);

            MarkCoinsSpent(actor, coins);          

            StartCoroutine(ResolveDropAfterAnimation(result, coins));
        }

        IEnumerator ResolveDropAfterAnimation(GlassDropResult result, IReadOnlyList<Coin> coins)
        {
            yield return new WaitForSeconds(resolveDelayAfterDrop); 

            DropResolved?.Invoke(result);          

            if (result.Overflowed)
            {
                ResolveOverflow(result);
                yield break;
            }

            ResolveSafeDrop(result, coins);
        }

        void ResolveSafeDrop(GlassDropResult result, IReadOnlyList<Coin> coins)
        {
            if (result.Actor == TurnActor.Player)
            {
                var payout = economyManager != null
                    ? economyManager.AwardSafeDrop(coins, result.RiskBeforeDrop)
                    : 0;

                Debug.Log(
                    $"[GameManager] Player safe drop resolved. Payout={payout}, " +
                    $"roundEarnings={(economyManager == null ? 0 : economyManager.CurrentRoundEarnings)}.");

                BeginEnemyTurn();
                return;
            }

            Debug.Log("[GameManager] Enemy safe drop resolved. Returning control to player.");
            BeginPlayerTurn();
        }

        void ResolveOverflow(GlassDropResult result)
        {
            if (result.Actor == TurnActor.Player)
            {
                Debug.Log(
                    $"[GameManager] Player caused overflow on round {currentRound}. " +
                    "Player instantly loses and current round earnings are wiped.");

                economyManager?.WipeCurrentRoundEarnings();
                EnterGameOver("Player overflowed the glass.", MatchOutcome.PlayerLost);
                return;
            }

            Debug.Log(
                $"[GameManager] Enemy caused overflow on round {currentRound}. " +
                "Player wins the round and banks current earnings.");

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

                Debug.Log("[GameManager] Player has no coins left. Skipping player turn.");
                BeginEnemyTurn();
                return;
            }

            TransitionTo(GameState.PlayerTurn);
            cameraController?.SwitchCamera(CameraState.PlayerFocus);

            Debug.Log(
                $"[GameManager] Player turn started. PlayerCoins={playerCoins.Count}, " +
                $"EnemyCoins={enemyCoins.Count}, risk={GetCurrentRiskForLog():0.##}%.");
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

                Debug.Log("[GameManager] Enemy has no coins left. Skipping enemy turn.");
                BeginPlayerTurn();
                return;
            }

            TransitionTo(GameState.EnemyTurn);
            cameraController?.SwitchCamera(CameraState.DealerFocus);
            Debug.Log($"[GameManager] Enemy turn started with {enemyCoins.Count} coin(s) remaining.");

            if (enemyAI != null)
                enemyAI.BeginTurn(this);
            else
                Debug.LogWarning("[GameManager] EnemyAI reference is missing. Enemy turn cannot progress.");
        }

        void EnterRestockPhase(string reason)
        {
            TransitionTo(GameState.RestockPhase);
            cameraController?.SwitchCamera(CameraState.TableOverview);

            Debug.Log(
                $"[GameManager] Restock phase started in round {currentRound}. Reason={reason}. " +
                $"Glass risk maintained at {GetCurrentRiskForLog():0.##}%.");

            GenerateRoundCoinPools();
            HandsRestocked?.Invoke(currentRound, GetCurrentRiskForLog());
            BeginPlayerTurn();
        }

        public void QueueEnemyForcedCoinCount(int coinCount)
        {
            queuedEnemyForcedCoinCount = Mathf.Clamp(coinCount, 0, GameConstants.MaxEnemyCoinsPerTurn);
            Debug.Log($"[GameManager] Queued enemy forced coin count={queuedEnemyForcedCoinCount}.");
        }

        public int ConsumeQueuedEnemyForcedCoinCount()
        {
            var forcedCount = queuedEnemyForcedCoinCount;
            queuedEnemyForcedCoinCount = 0;

            if (forcedCount > 0)
                Debug.Log($"[GameManager] Consumed queued enemy forced coin count={forcedCount}.");

            return forcedCount;
        }

        void CompleteWonRound(string reason)
        {
            if (currentRound >= GameConstants.TotalRounds)
            {
                EnterGameOver(reason, MatchOutcome.PlayerWon);
                return;
            }

            if (shopBetweenRoundsEnabled)
            {
                EnterShopPhase();
                return;
            }

            Debug.Log(
                $"[GameManager] Round {currentRound} won. Shop disabled for this build, " +
                "starting next round immediately.");
            StartRound();
        }

        void EnterShopPhase()
        {
            if (!shopBetweenRoundsEnabled)
            {
                Debug.Log("[GameManager] Shop phase requested but disabled. Starting next round.");
                StartRound();
                return;
            }

            TransitionTo(GameState.ShopPhase);
            cameraController?.SwitchCamera(CameraState.TableOverview);
            shopManager?.ShowShop();

            Debug.Log(
                $"[GameManager] Shop phase entered after round {currentRound}. " +
                $"BankedCash={(economyManager == null ? 0 : economyManager.PlayerTotalBankedCash)}.");
        }

        void EnterGameOver(string reason, MatchOutcome outcome)
        {
            lastMatchOutcome = outcome;
            TransitionTo(GameState.GameOver);
            cameraController?.SwitchCamera(CameraState.TableOverview);
            shopManager?.HideShop();
            StartCoroutine(ShowEndScreenAfterDelay(outcome, reason));   // ← statt ShowOutcome direkt

            Debug.Log(
                $"[GameManager] GameOver. Outcome={outcome}, reason={reason}. Final banked cash=" +
                $"{(economyManager == null ? 0 : economyManager.PlayerTotalBankedCash)}.");
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

            if (playerCoins.Count == 0)
                Debug.LogWarning("[GameManager] Player coin pool is empty after generation.");

            if (enemyCoins.Count == 0)
                Debug.LogWarning("[GameManager] Enemy coin pool is empty after generation.");

            LogPool("Player", playerCoins);
            LogPool("Enemy", enemyCoins);
        }

        void PopulateSceneCoinListsIfEmpty()
        {
            if (handAuthoredPlayerCoins.Count > 0 || handAuthoredEnemyCoins.Count > 0)
                return;

            var sceneCoins = FindObjectsByType<Coin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (var i = 0; i < sceneCoins.Length; i++)
            {
                if (sceneCoins[i] == null)
                    continue;

                if (sceneCoins[i].isPlayerCoin)
                    handAuthoredPlayerCoins.Add(sceneCoins[i]);
                else
                    handAuthoredEnemyCoins.Add(sceneCoins[i]);
            }

            Debug.Log(
                $"[GameManager] Found scene coins. Player={handAuthoredPlayerCoins.Count}, " +
                $"Enemy={handAuthoredEnemyCoins.Count}.");
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

            Debug.Log(
                $"[GameManager] {actor} pool had {targetPool.Count}/{targetCount} coin(s). " +
                $"Generating {missingCount} runtime fallback coin(s).");

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
            var spawnRoot = actor == TurnActor.Player ? playerCoinSpawnRoot : enemyCoinSpawnRoot;
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

            Debug.Log($"[GameManager] {actor} committed {validCoins.Count} valid coin(s).");
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

            Debug.Log(
                $"[GameManager] Removed spent {actor} coin(s). PlayerCoins={playerCoins.Count}, " +
                $"EnemyCoins={enemyCoins.Count}.");
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

        void LogPool(string label, IReadOnlyList<Coin> coins)
        {
            var description = coins.Count == 0 ? "none" : string.Empty;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] == null)
                    continue;

                description +=
                    $"{coins[i].name}(size={coins[i].size}, risk={coins[i].riskContribution:0.##}, " +
                    $"payout={coins[i].basePayout})";

                if (i < coins.Count - 1)
                    description += ", ";
            }

            Debug.Log($"[GameManager] {label} coin pool: {description}");
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

            if (shopManager == null)
                shopManager = FindAnyObjectByType<ShopManager>();

            if (shopManager == null)
            {
                shopManager = gameObject.AddComponent<ShopManager>();
                shopManager.Configure(null, economyManager, this);
                Debug.Log("[GameManager] Created runtime ShopManager fallback.");
            }

            if (endScreenManager == null)
                endScreenManager = FindAnyObjectByType<EndScreenManager>();

            if (endScreenManager == null)
                endScreenManager = EndScreenManager.CreateRuntimeFallback();

            if (dropPresentationController == null)
                dropPresentationController = FindAnyObjectByType<CoinDropPresentationController>();

            if (dropPresentationController == null)
            {
                dropPresentationController = gameObject.AddComponent<CoinDropPresentationController>();
                dropPresentationController.Configure(this, FindGlassTransform());
                Debug.Log("[GameManager] Created runtime CoinDropPresentationController fallback.");
            }

            if (saloonHudController == null)
                saloonHudController = FindAnyObjectByType<SaloonHudController>();

            if (saloonHudController == null)
            {
                saloonHudController = gameObject.AddComponent<SaloonHudController>();
                saloonHudController.Configure(null, null, this, glassManager, economyManager);
                Debug.Log("[GameManager] Created runtime SaloonHudController fallback.");
            }

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
            Debug.Log(
                "[GameManager] Shop phase was disabled on this scene asset. " +
                "Re-enabled it for the current saloon loop.");
        }

        void NormalizeFallbackLayoutIfNeeded()
        {
            if (playerFallbackStart.y >= 0.5f && enemyFallbackStart.y >= 0.5f)
                return;

            playerFallbackStart = new Vector3(-0.62f, 1.09f, -1.02f);
            enemyFallbackStart = new Vector3(-0.62f, 1.09f, 0.88f);
            coinSpacing = new Vector3(0.38f, 0f, 0f);

            Debug.Log(
                "[GameManager] Normalized fallback coin layout to saloon table coordinates. " +
                "This keeps runtime-spawned restock coins visible in legacy scene layouts.");
        }

        void TransitionTo(GameState newState)
        {
            if (currentState == newState)
            {
                Debug.Log($"[GameManager] State remains {newState}.");
                StateChanged?.Invoke(currentState);
                return;
            }

            var oldState = currentState;
            currentState = newState;
            Debug.Log($"[GameManager] State transition: {oldState} -> {newState}.");
            StateChanged?.Invoke(currentState);
        }

        float GetCurrentRiskForLog() =>
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
                Debug.Log("[GameManager] Created EventSystem with InputSystemUIInputModule.");
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
                Debug.Log(
                    $"[GameManager] Replaced legacy StandaloneInputModule on {eventSystemObject.name} " +
                    "with InputSystemUIInputModule.");
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
                    Debug.Log(
                        $"[GameManager] Added InputSystemUIInputModule to {eventSystems[i].name}.");
                }
            }
        }
    }
}
