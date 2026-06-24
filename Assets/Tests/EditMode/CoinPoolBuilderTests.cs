using System.Collections.Generic;
using Meniscus.Core;
using Meniscus.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class CoinPoolBuilderTests
    {
        readonly List<GameObject> spawned = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in spawned)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        Coin NewCoin(string name, bool isPlayer, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            spawned.Add(go);

            var coin = go.AddComponent<Coin>();
            coin.isPlayerCoin = isPlayer;

            if (parent != null)
                go.transform.SetParent(parent);

            return coin;
        }

        [Test]
        public void FillMissingAuthoredCoinLists_FillsEmptyPlayerList_WhenEnemyListAlreadyPopulated()
        {
            // Mirrors the real scene bug: handAuthoredPlayerCoins is empty/all-null while
            // handAuthoredEnemyCoins is fully wired. The player list must still self-fill.
            var p1 = NewCoin("P1", true);
            var p2 = NewCoin("P2", true);
            var e1 = NewCoin("E1", false);

            var playerList = new List<Coin>();
            var enemyList = new List<Coin> { e1 };

            CoinPoolBuilder.FillMissingAuthoredCoinLists(playerList, enemyList, new[] { p1, p2, e1 });

            CollectionAssert.AreEquivalent(new[] { p1, p2 }, playerList,
                "An empty player list must be auto-filled from the scene even when the enemy list is already populated.");
            CollectionAssert.AreEquivalent(new[] { e1 }, enemyList,
                "An already-populated enemy list must be left untouched.");
        }

        [Test]
        public void FillMissingAuthoredCoinLists_TreatsAllNullListAsEmptyAndFills()
        {
            var p1 = NewCoin("P1", true);

            var playerList = new List<Coin> { null, null, null };
            var enemyList = new List<Coin>();

            CoinPoolBuilder.FillMissingAuthoredCoinLists(playerList, enemyList, new[] { p1 });

            CollectionAssert.AreEquivalent(new[] { p1 }, playerList,
                "A list of only null references must be treated as empty and auto-filled.");
        }

        [Test]
        public void FillMissingAuthoredCoinLists_LeavesBothPopulatedListsUntouched()
        {
            var p1 = NewCoin("P1", true);
            var e1 = NewCoin("E1", false);
            var pExtra = NewCoin("P2", true);

            var playerList = new List<Coin> { p1 };
            var enemyList = new List<Coin> { e1 };

            CoinPoolBuilder.FillMissingAuthoredCoinLists(playerList, enemyList, new[] { p1, e1, pExtra });

            CollectionAssert.AreEquivalent(new[] { p1 }, playerList,
                "A populated list must not absorb extra scene coins.");
            CollectionAssert.AreEquivalent(new[] { e1 }, enemyList);
        }

        [Test]
        public void HasUsableCoin_TrueOnlyWhenAtLeastOneLiveCoinPresent()
        {
            var coin = NewCoin("P1", true);

            Assert.IsTrue(CoinPoolBuilder.HasUsableCoin(new List<Coin> { null, coin }));
            Assert.IsFalse(CoinPoolBuilder.HasUsableCoin(new List<Coin> { null, null }));
            Assert.IsFalse(CoinPoolBuilder.HasUsableCoin(new List<Coin>()));
            Assert.IsFalse(CoinPoolBuilder.HasUsableCoin(null));
        }

        [Test]
        public void ResolveAuthoredParent_ReturnsParentOfFirstNonNullCoin()
        {
            var container = new GameObject("PlayerCoins");
            spawned.Add(container);
            var coin = NewCoin("P1", true, container.transform);

            var parent = CoinPoolBuilder.ResolveAuthoredParent(new List<Coin> { null, coin });

            Assert.AreEqual(container.transform, parent);
        }

        [Test]
        public void ResolveAuthoredParent_ReturnsNullWhenNoUsableCoins()
        {
            Assert.IsNull(CoinPoolBuilder.ResolveAuthoredParent(new List<Coin>()));
            Assert.IsNull(CoinPoolBuilder.ResolveAuthoredParent(new List<Coin> { null, null }));
        }
    }
}
