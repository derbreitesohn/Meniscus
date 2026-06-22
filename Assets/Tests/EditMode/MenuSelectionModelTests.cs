// Assets/Tests/EditMode/MenuSelectionModelTests.cs
using System.Collections.Generic;
using Meniscus.Items;
using Meniscus.UI;
using NUnit.Framework;

namespace Meniscus.Tests.EditMode
{
    public class MenuSelectionModelTests
    {
        static List<ItemDefinition> Catalog(int n)
        {
            var list = new List<ItemDefinition>();
            for (var i = 0; i < n; i++)
                list.Add(ItemDefinition.Create($"id_{i}", $"Item {i}", $"Desc {i}", 10 * (i + 1), ItemEffectKind.PayoutMultiplier, 0f));
            return list;
        }

        [Test]
        public void SetCatalog_DropsNullsAndResetsPageAndSelection()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            list.Add(null);
            model.SetCatalog(list, 5);
            Assert.AreEqual(3, model.Count);
            Assert.AreEqual(0, model.CurrentPage);
            Assert.IsNull(model.Selected);
        }

        [Test]
        public void CurrentPageItems_SlicesByPage()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(9), 5);
            Assert.AreEqual(2, model.PageCount);
            Assert.AreEqual(5, model.CurrentPageItems().Count);
            model.TurnNext();
            Assert.AreEqual(1, model.CurrentPage);
            Assert.AreEqual(4, model.CurrentPageItems().Count);
        }

        [Test]
        public void Turn_RespectsBoundsAndCanFlags()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(9), 5);
            Assert.IsFalse(model.CanTurnPrev);
            Assert.IsTrue(model.CanTurnNext);
            model.TurnPrev();                 // no-op at start
            Assert.AreEqual(0, model.CurrentPage);
            model.TurnNext();
            Assert.IsTrue(model.CanTurnPrev);
            Assert.IsFalse(model.CanTurnNext);
            model.TurnNext();                 // no-op at end
            Assert.AreEqual(1, model.CurrentPage);
        }

        [Test]
        public void Select_IgnoresItemsNotInCatalog()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            var stranger = ItemDefinition.Create("x", "X", "", 5, ItemEffectKind.PayoutMultiplier, 0f);
            model.Select(stranger);
            Assert.IsNull(model.Selected);
            model.Select(list[1]);
            Assert.AreSame(list[1], model.Selected);
        }

        [Test]
        public void EvaluateBuy_NoSelection_NotBuyableEmptyLabel()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(3), 5);
            var state = model.EvaluateBuy(1000, false);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("", state.Label);
        }

        [Test]
        public void EvaluateBuy_DeskFull_BeatsAffordability()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[0]); // cost 10
            var state = model.EvaluateBuy(1000, deskFull: true);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Desk full", state.Label);
        }

        [Test]
        public void EvaluateBuy_Unaffordable_ShowsNeed()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[2]); // cost 30
            var state = model.EvaluateBuy(20, deskFull: false);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Need $30", state.Label);
        }

        [Test]
        public void EvaluateBuy_Affordable_ShowsBuyLabel()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.Select(list[1]); // cost 20
            var state = model.EvaluateBuy(50, deskFull: false);
            Assert.IsTrue(state.CanBuy);
            Assert.AreEqual("Buy — $20", state.Label);
        }

        // --- Multi-select cart ---

        [Test]
        public void ToggleCart_AddsRemovesAndFocusesSelection()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);

            model.ToggleCart(list[0]);
            Assert.IsTrue(model.IsInCart(list[0]));
            Assert.AreSame(list[0], model.Selected);   // tapping also focuses the detail view
            Assert.AreEqual(1, model.CartCount);

            model.ToggleCart(list[2]);
            Assert.AreEqual(2, model.CartCount);

            model.ToggleCart(list[0]);                  // toggling again removes it
            Assert.IsFalse(model.IsInCart(list[0]));
            Assert.AreEqual(1, model.CartCount);
        }

        [Test]
        public void ToggleCart_IgnoresItemsNotInCatalog()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(2), 5);
            var stranger = ItemDefinition.Create("x", "X", "", 5, ItemEffectKind.PayoutMultiplier, 0f);
            model.ToggleCart(stranger);
            Assert.AreEqual(0, model.CartCount);
        }

        [Test]
        public void CartTotalCost_SumsSelectedCosts()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3); // costs 10, 20, 30
            model.SetCatalog(list, 5);
            model.ToggleCart(list[0]);
            model.ToggleCart(list[2]);
            Assert.AreEqual(40, model.CartTotalCost);
        }

        [Test]
        public void SetCatalog_ClearsTheCart()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.ToggleCart(list[0]);
            model.SetCatalog(Catalog(2), 5);
            Assert.AreEqual(0, model.CartCount);
        }

        [Test]
        public void EvaluateCart_EmptyCart_NotBuyable()
        {
            var model = new MenuSelectionModel();
            model.SetCatalog(Catalog(3), 5);
            var state = model.EvaluateCart(1000, deskFull: false);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Tap items to add", state.Label);
        }

        [Test]
        public void EvaluateCart_DeskFull_BeatsAffordability()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3);
            model.SetCatalog(list, 5);
            model.ToggleCart(list[0]);
            var state = model.EvaluateCart(1000, deskFull: true);
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Desk full", state.Label);
        }

        [Test]
        public void EvaluateCart_CannotAffordCheapest_ShowsNeed()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3); // 10, 20, 30
            model.SetCatalog(list, 5);
            model.ToggleCart(list[1]);
            model.ToggleCart(list[2]);
            var state = model.EvaluateCart(5, deskFull: false); // cheapest selected is 20
            Assert.IsFalse(state.CanBuy);
            Assert.AreEqual("Need $20", state.Label);
        }

        [Test]
        public void EvaluateCart_Affordable_ShowsCountAndTotal()
        {
            var model = new MenuSelectionModel();
            var list = Catalog(3); // 10, 20, 30
            model.SetCatalog(list, 5);
            model.ToggleCart(list[0]);
            model.ToggleCart(list[1]);
            var state = model.EvaluateCart(100, deskFull: false);
            Assert.IsTrue(state.CanBuy);
            Assert.AreEqual("Buy 2 — $30", state.Label);
        }
    }
}
