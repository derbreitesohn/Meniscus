using System.Collections.Generic;
using Meniscus.Gameplay;
using UnityEngine;

namespace Meniscus.Core
{
    /// <summary>
    /// Pure helpers for assembling the per-round coin pools. Kept free of MonoBehaviour lifecycle so the
    /// fiddly "which coins belong to which actor" logic can be unit-tested without a live scene.
    /// </summary>
    public static class CoinPoolBuilder
    {
        /// <summary>
        /// Discovers authored coins from <paramref name="sceneCoins"/> for whichever actor list has no usable
        /// (non-null) entries, leaving an already-populated list untouched. The two actors are handled
        /// independently: a fully-wired enemy list must never suppress auto-discovery of an empty player list.
        /// </summary>
        public static void FillMissingAuthoredCoinLists(
            List<Coin> playerCoins, List<Coin> enemyCoins, IReadOnlyList<Coin> sceneCoins)
        {
            // Null/destroyed slots (e.g. a scene list authored as N empty entries) must not count as
            // "populated", or the list would never get filled.
            RemoveNullCoins(playerCoins);
            RemoveNullCoins(enemyCoins);

            var fillPlayer = playerCoins.Count == 0;
            var fillEnemy = enemyCoins.Count == 0;

            if (!fillPlayer && !fillEnemy)
                return;

            if (sceneCoins == null)
                return;

            for (var i = 0; i < sceneCoins.Count; i++)
            {
                var coin = sceneCoins[i];

                if (coin == null)
                    continue;

                if (coin.isPlayerCoin)
                {
                    if (fillPlayer && !playerCoins.Contains(coin))
                        playerCoins.Add(coin);
                }
                else if (fillEnemy && !enemyCoins.Contains(coin))
                {
                    enemyCoins.Add(coin);
                }
            }
        }

        /// <summary>True when the list holds at least one live (non-null) coin.</summary>
        public static bool HasUsableCoin(IReadOnlyList<Coin> coins)
        {
            if (coins == null)
                return false;

            for (var i = 0; i < coins.Count; i++)
            {
                if (coins[i] != null)
                    return true;
            }

            return false;
        }

        /// <summary>Removes null/destroyed entries from a coin list in place.</summary>
        public static void RemoveNullCoins(List<Coin> coins)
        {
            for (var i = coins.Count - 1; i >= 0; i--)
            {
                if (coins[i] == null)
                    coins.RemoveAt(i);
            }
        }

        /// <summary>
        /// Returns the parent transform of the first usable authored coin, or null when none exist. Used as a
        /// spawn-root fallback so runtime coins land in the same container (and table height) as authored ones.
        /// </summary>
        public static Transform ResolveAuthoredParent(IReadOnlyList<Coin> authoredCoins)
        {
            if (authoredCoins == null)
                return null;

            for (var i = 0; i < authoredCoins.Count; i++)
            {
                if (authoredCoins[i] != null)
                    return authoredCoins[i].transform.parent;
            }

            return null;
        }
    }
}
