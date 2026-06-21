using System;
using System.Collections.Generic;
using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Items
{
    /// <summary>One purchased-item stack held on the player's desk.</summary>
    public readonly struct ItemStack
    {
        public ItemStack(ItemDefinition item, int count)
        {
            Item = item;
            Count = count;
        }

        public ItemDefinition Item { get; }
        public int Count { get; }
    }

    /// <summary>
    /// The player's held desk items. Purchases grant a stack here (instead of applying an effect);
    /// using an item on the player's turn consumes one. Persists across rounds within a match and is
    /// capped at <see cref="GameConstants.DeskCapacity"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInventory : MonoBehaviour
    {
        readonly List<ItemDefinition> items = new();
        readonly List<int> counts = new();

        public event Action Changed;

        public int TotalCount { get; private set; }
        public bool IsFull => TotalCount >= GameConstants.DeskCapacity;

        public IReadOnlyList<ItemStack> Contents()
        {
            var result = new List<ItemStack>(items.Count);

            for (var i = 0; i < items.Count; i++)
                result.Add(new ItemStack(items[i], counts[i]));

            return result;
        }

        public bool Grant(ItemDefinition item)
        {
            if (item == null || IsFull)
                return false;

            var index = IndexOf(item);

            if (index >= 0)
                counts[index]++;
            else
            {
                items.Add(item);
                counts.Add(1);
            }

            TotalCount++;
            Changed?.Invoke();
            return true;
        }

        public bool Has(ItemDefinition item) => item != null && IndexOf(item) >= 0;

        public bool TryConsume(ItemDefinition item)
        {
            var index = item == null ? -1 : IndexOf(item);

            if (index < 0)
                return false;

            counts[index]--;
            TotalCount--;

            if (counts[index] <= 0)
            {
                items.RemoveAt(index);
                counts.RemoveAt(index);
            }

            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (items.Count == 0 && TotalCount == 0)
                return;

            items.Clear();
            counts.Clear();
            TotalCount = 0;
            Changed?.Invoke();
        }

        int IndexOf(ItemDefinition item)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] == item)
                    return i;
            }

            return -1;
        }
    }
}
