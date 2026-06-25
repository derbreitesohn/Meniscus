using Meniscus.Core;
using Meniscus.Gameplay;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Meniscus.Tests.EditMode
{
    public class ShopManagerTests
    {
        GameObject host;
        EconomyManager economy;
        PlayerInventory inventory;
        ShopManager shop;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Shop Test Host");
            economy = host.AddComponent<EconomyManager>();
            inventory = host.AddComponent<PlayerInventory>();
            shop = host.AddComponent<ShopManager>();

            // Pin the shop's dependencies so ResolveReferences does not bind a stray manager left in the
            // shared EditMode test scene by another fixture (its FindAnyObjectByType is order-sensitive).
            var so = new SerializedObject(shop);
            so.FindProperty("economyManager").objectReferenceValue = economy;
            so.FindProperty("playerInventory").objectReferenceValue = inventory;
            so.ApplyModifiedProperties();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(host);
        }

        // Grant cash directly to the bank — the award path now pays for boldness, not 1:1 face value.
        void BankCash(int amount)
        {
            economy.GrantBankedCash(amount);
        }

        static ItemDefinition Item(string id, int cost) =>
            ItemDefinition.Create(id, id, "", cost, ItemEffectKind.PayoutMultiplier, 2f);

        // A player coin used to fill currentRoundEarnings via AwardSafeDrop in the mid-round buy test.
        static Coin CreateCoin(string name, int payout)
        {
            var coinObject = new GameObject(name);
            var coin = coinObject.AddComponent<Coin>();
            coin.Configure(CoinSize.Medium, 0f, payout, belongsToPlayer: true);
            return coin;
        }

        [Test]
        public void TryBuyItem_GrantsToInventoryAndChargesCash()
        {
            BankCash(100);
            var item = Item("marked_coin", 35);

            Assert.IsTrue(shop.TryBuyItem(item));
            Assert.IsTrue(inventory.Has(item));
            Assert.AreEqual(65, economy.PlayerTotalBankedCash);
        }

        [Test]
        public void TryBuyItem_Unaffordable_ReturnsFalseAndGrantsNothing()
        {
            BankCash(20);
            var item = Item("loaded_dice", 70);

            Assert.IsFalse(shop.TryBuyItem(item));
            Assert.IsFalse(inventory.Has(item));
            Assert.AreEqual(20, economy.PlayerTotalBankedCash);
        }

        [Test]
        public void TryBuyItem_DeskFull_NotChargedAndNotGranted()
        {
            BankCash(1000);

            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                Assert.IsTrue(inventory.Grant(Item($"filler{i}", 1)));

            var cashBefore = economy.PlayerTotalBankedCash;
            var item = Item("marked_coin", 35);

            Assert.IsFalse(shop.TryBuyItem(item));
            Assert.IsFalse(inventory.Has(item));
            Assert.AreEqual(cashBefore, economy.PlayerTotalBankedCash);
        }

        [Test]
        public void SpendableCash_MirrorsEconomyManager()
        {
            BankCash(120);
            Assert.AreEqual(120, shop.SpendableCash);
        }

        [Test]
        public void TryBuyItem_PaysFromRoundEarnings_WhenBankIsEmpty()
        {
            // No banked savings yet — but this round has earned money. A mid-round purchase should still
            // go through, drawing on the at-risk round earnings, so what the wallet HUD shows and what the
            // shop lets you spend stay in step (the bug where every item was greyed out mid-round).
            var coin = CreateCoin("round earner", 200);
            economy.AwardSafeDrop(new[] { coin }, 100f);   // banks nothing; fills currentRoundEarnings

            Assert.AreEqual(0, economy.PlayerTotalBankedCash);
            Assert.Greater(economy.CurrentRoundEarnings, 35);

            var spendableBefore = economy.SpendableCash;
            var item = Item("marked_coin", 35);

            Assert.IsTrue(shop.TryBuyItem(item));
            Assert.IsTrue(inventory.Has(item));
            Assert.AreEqual(0, economy.PlayerTotalBankedCash);              // bank stays empty
            Assert.AreEqual(spendableBefore - 35, economy.SpendableCash);  // paid from round earnings

            Object.DestroyImmediate(coin.gameObject);
        }

        [Test]
        public void IsDeskFull_TrueWhenInventoryFull()
        {
            for (var i = 0; i < GameConstants.DeskCapacity; i++)
                inventory.Grant(ItemDefinition.Create($"f_{i}", $"F{i}", "", 1, ItemEffectKind.PayoutMultiplier, 0f));
            Assert.IsTrue(shop.IsDeskFull);
        }

        [Test]
        public void OwnedCount_ReflectsGrants()
        {
            var item = ItemDefinition.Create("steady_hand", "Steady Hand", "", 50, ItemEffectKind.PayoutMultiplier, 0f);
            Assert.AreEqual(0, shop.OwnedCount(item));
            inventory.Grant(item);
            inventory.Grant(item);
            Assert.AreEqual(2, shop.OwnedCount(item));
        }
    }
}
