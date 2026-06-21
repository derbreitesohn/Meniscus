using System.Collections;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Gameplay
{
    [DisallowMultipleComponent]
    public class EnemyAI : MonoBehaviour
    {
        Coroutine activeTurn;

        public void BeginTurn(GameManager gameManager)
        {
            if (gameManager == null)
            {
                Debug.LogWarning("[EnemyAI] Cannot begin turn: GameManager reference is missing.");
                return;
            }

            if (activeTurn != null)
                StopCoroutine(activeTurn);

            activeTurn = StartCoroutine(ExecuteTurnAfterDelay(gameManager));
        }

        IEnumerator ExecuteTurnAfterDelay(GameManager gameManager)
        {
            yield return new WaitForSeconds(GameConstants.EnemyTurnDelaySeconds);

            activeTurn = null;

            if (gameManager.CurrentState != GameState.EnemyTurn)
                yield break;

            var forcedCoinCount = gameManager.ConsumeQueuedEnemyForcedCoinCount();
            var trueSpillChance = gameManager.GlassManager == null
                ? 0f
                : gameManager.GlassManager.CurrentTrueSpillChance;
            var chosenCoins = ChooseCoins(gameManager.EnemyCoins, trueSpillChance, forcedCoinCount);

            if (chosenCoins.Count == 0)
            {
                Debug.LogWarning("[EnemyAI] Enemy had no valid coins to choose.");
                yield break;
            }

            for (var i = 0; i < chosenCoins.Count; i++)
                chosenCoins[i].SetSelected(true);

            yield return new WaitForSeconds(GameConstants.EnemyTellDelaySeconds);

            gameManager.ExecuteEnemyDrop(chosenCoins);
        }

        static List<Coin> ChooseCoins(
            IReadOnlyList<Coin> availableCoins,
            float trueSpillChance,
            int forcedCoinCount)
        {
            var candidates = new List<Coin>();

            if (availableCoins != null)
            {
                for (var i = 0; i < availableCoins.Count; i++)
                {
                    var coin = availableCoins[i];

                    if (coin != null && !coin.IsSpent && coin.gameObject.activeInHierarchy)
                        candidates.Add(coin);
                }
            }

            var chosenCoins = new List<Coin>();

            if (candidates.Count == 0)
                return chosenCoins;

            var maxCoins = GetMaximumCoinsForRisk(candidates.Count, trueSpillChance, forcedCoinCount);
            var targetCount = forcedCoinCount > 0
                ? maxCoins
                : Random.Range(1, maxCoins + 1);

            for (var i = 0; i < targetCount; i++)
            {
                var index = Random.Range(0, candidates.Count);
                chosenCoins.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return chosenCoins;
        }

        public static int GetMaximumCoinsForRisk(
            int availableCoinCount,
            float trueSpillChance,
            int forcedCoinCount)
        {
            if (availableCoinCount <= 0)
                return 0;

            var absoluteMax = Mathf.Min(GameConstants.MaxEnemyCoinsPerTurn, availableCoinCount);

            if (forcedCoinCount > 0)
                return Mathf.Clamp(forcedCoinCount, 1, absoluteMax);

            return trueSpillChance >= GameConstants.EnemyConservativeSpillChanceThreshold
                ? 1
                : absoluteMax;
        }
    }
}
