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
            Debug.Log($"[EnemyAI] Enemy is thinking for {GameConstants.EnemyTurnDelaySeconds:0.##} seconds.");
            yield return new WaitForSeconds(GameConstants.EnemyTurnDelaySeconds);

            activeTurn = null;

            if (gameManager.CurrentState != GameState.EnemyTurn)
            {
                Debug.Log($"[EnemyAI] Enemy turn aborted. Current state={gameManager.CurrentState}.");
                yield break;
            }

            var chosenCoins = ChooseCoins(gameManager.EnemyCoins);

            if (chosenCoins.Count == 0)
            {
                Debug.LogWarning("[EnemyAI] Enemy had no valid coins to choose.");
                yield break;
            }

            Debug.Log($"[EnemyAI] Enemy chose {chosenCoins.Count} coin(s): {DescribeCoins(chosenCoins)}.");
            gameManager.ExecuteEnemyDrop(chosenCoins);
        }

        static List<Coin> ChooseCoins(IReadOnlyList<Coin> availableCoins)
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

            var maxCoins = Mathf.Min(GameConstants.MaxEnemyCoinsPerTurn, candidates.Count);
            var targetCount = Random.Range(1, maxCoins + 1);

            for (var i = 0; i < targetCount; i++)
            {
                var index = Random.Range(0, candidates.Count);
                chosenCoins.Add(candidates[index]);
                candidates.RemoveAt(index);
            }

            return chosenCoins;
        }

        static string DescribeCoins(IReadOnlyList<Coin> coins)
        {
            var description = string.Empty;

            for (var i = 0; i < coins.Count; i++)
            {
                description +=
                    $"{coins[i].name}(size={coins[i].size}, risk={coins[i].riskContribution:0.##})";

                if (i < coins.Count - 1)
                    description += ", ";
            }

            return description;
        }
    }
}
