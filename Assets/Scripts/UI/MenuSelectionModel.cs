// Assets/Scripts/UI/MenuSelectionModel.cs
using System.Collections.Generic;
using Meniscus.Items;
using UnityEngine;

namespace Meniscus.UI
{
    /// <summary>Whether the selected order can be bought, and the label the Buy button should show.</summary>
    public readonly struct BuyState
    {
        public BuyState(bool canBuy, string label)
        {
            CanBuy = canBuy;
            Label = label;
        }

        public bool CanBuy { get; }
        public string Label { get; }
    }

    /// <summary>
    /// View-model for the book menu: holds the catalog split into pages, the player's current
    /// selection, and computes the Buy button state. Pure (no scene objects) so it is unit-tested.
    /// </summary>
    public class MenuSelectionModel
    {
        readonly List<ItemDefinition> catalog = new();

        // The "cart": items toggled for a multi-buy. Selected is the detail-focused item (last tapped);
        // the cart is what the Buy button purchases. Ordered so the display is stable.
        readonly List<ItemDefinition> cart = new();

        public int ItemsPerPage { get; private set; } = 1;
        public int CurrentPage { get; private set; }
        public ItemDefinition Selected { get; private set; }

        public IReadOnlyList<ItemDefinition> Cart => cart;
        public int CartCount => cart.Count;
        public bool IsInCart(ItemDefinition item) => item != null && cart.Contains(item);
        public int CartTotalCost
        {
            get
            {
                var total = 0;
                foreach (var item in cart)
                    total += item.Cost;
                return total;
            }
        }

        public int Count => catalog.Count;
        public int PageCount => MenuLayout.PageCount(catalog.Count, ItemsPerPage);
        public bool CanTurnPrev => CurrentPage > 0;
        public bool CanTurnNext => CurrentPage < PageCount - 1;

        public void SetCatalog(IReadOnlyList<ItemDefinition> items, int itemsPerPage)
        {
            catalog.Clear();

            if (items != null)
                foreach (var item in items)
                    if (item != null)
                        catalog.Add(item);

            ItemsPerPage = Mathf.Max(1, itemsPerPage);
            CurrentPage = 0;
            Selected = null;
            cart.Clear();
        }

        public IReadOnlyList<ItemDefinition> CurrentPageItems()
        {
            var start = CurrentPage * ItemsPerPage;
            var page = new List<ItemDefinition>();

            for (var i = start; i < start + ItemsPerPage && i < catalog.Count; i++)
                page.Add(catalog[i]);

            return page;
        }

        public void TurnPrev()
        {
            if (CanTurnPrev)
                CurrentPage--;
        }

        public void TurnNext()
        {
            if (CanTurnNext)
                CurrentPage++;
        }

        public void Select(ItemDefinition item)
        {
            if (item != null && catalog.Contains(item))
                Selected = item;
        }

        /// <summary>Taps an item: focuses it for the detail view and toggles it in the buy cart.</summary>
        public void ToggleCart(ItemDefinition item)
        {
            if (item == null || !catalog.Contains(item))
                return;

            Selected = item;

            if (!cart.Remove(item))
                cart.Add(item);
        }

        public void RemoveFromCart(ItemDefinition item) => cart.Remove(item);

        public void ClearCart() => cart.Clear();

        public BuyState EvaluateBuy(int bankedCash, bool deskFull)
        {
            if (Selected == null)
                return new BuyState(false, "");

            if (deskFull)
                return new BuyState(false, "Desk full");

            if (bankedCash < Selected.Cost)
                return new BuyState(false, $"Need ${Selected.Cost}");

            return new BuyState(true, $"Buy — ${Selected.Cost}");
        }

        /// <summary>Buy-button state for the whole cart: enabled when at least one item is affordable
        /// and the desk has room; the label shows how many and the total.</summary>
        public BuyState EvaluateCart(int bankedCash, bool deskFull)
        {
            if (cart.Count == 0)
                return new BuyState(false, "Tap items to add");

            if (deskFull)
                return new BuyState(false, "Desk full");

            var cheapest = int.MaxValue;
            foreach (var item in cart)
                cheapest = Mathf.Min(cheapest, item.Cost);

            if (bankedCash < cheapest)
                return new BuyState(false, $"Need ${cheapest}");

            return new BuyState(true, $"Buy {cart.Count} — ${CartTotalCost}");
        }
    }
}
