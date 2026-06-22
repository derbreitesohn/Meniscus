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

        public int ItemsPerPage { get; private set; } = 1;
        public int CurrentPage { get; private set; }
        public ItemDefinition Selected { get; private set; }

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
    }
}
