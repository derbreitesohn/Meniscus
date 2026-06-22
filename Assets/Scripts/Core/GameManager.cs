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
        [SerializeField] float resolveDelayAfterDrop = 1.0f;   // ~ dropAnimationSeconds (coin flight time)


        [Header("Managers")]
        [SerializeField] GlassManager glassManager;
        [SerializeField] EconomyManager economyManager;
        [SerializeField] EnemyAI enemyAI;
        [SerializeField] CameraController cameraController;
        [SerializeField] ShopManager shopManager;
        [SerializeField] EndScreenManager endScreenManager;
        [SerializeField] CoinDropPresentationController dropPresentationController;
        [SerializeField] SaloonHudController saloonHudController;
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
            playerInventory?.Clear();
            shopManager?.HideShop();
            endScreenManager?.Hide();
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
            cameraController?.SwitchCamera(CameraState.TableOverview);
            shopManager?.HideShop();

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
