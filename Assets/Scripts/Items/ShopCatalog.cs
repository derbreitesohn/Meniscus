using System.Collections.Generic;

namespace Meniscus.Items
{
    /// <summary>
    /// The code-built default item list, used when no authored <see cref="ItemDefinition"/> assets are
    /// wired on the shop. Mirrors the original three items and adds higher-tier variants to show the
    /// system extends purely through data. Authoring real assets later replaces this with one inspector
    /// list — no code change.
    /// </summary>
    public static class ShopCatalog
    {
        public static List<ItemDefinition> CreateDefaultCatalog() =>
            new()
            {
                // DisplayNames are the plain object/short names shown in the book; the one-line
                // descriptions sit beneath them in the buy menu. The ids are unchanged — they key both
                // the item models (ItemModelLibrary) and the effect handling, so renaming here is safe.
                ItemDefinition.Create(
                    "marked_coin", "Marked Coin",
                    "Your next safe pour pays double.",
                    35, ItemEffectKind.PayoutMultiplier, 2f),
                ItemDefinition.Create(
                    "loaded_dice", "Lucky Coin",
                    "Your next safe pour pays triple.",
                    70, ItemEffectKind.PayoutMultiplier, 3f),
                ItemDefinition.Create(
                    "steady_hand", "Steady Hand",
                    "Shrug off 10 risk next round — every pour eases.",
                    50, ItemEffectKind.SafeZoneBonus, 10f),
                ItemDefinition.Create(
                    "iron_grip", "Iron Grip",
                    "Shrug off 18 risk next round — every pour eases.",
                    90, ItemEffectKind.SafeZoneBonus, 18f),
                ItemDefinition.Create(
                    "dealers_debt", "Dealer's Debt",
                    "The dealer must pour two coins next turn.",
                    60, ItemEffectKind.ForceEnemyCoins, 2f),
                ItemDefinition.Create(
                    "round_for_the_dealer", "Round for the Dealer",
                    "The dealer drinks — he overflows more easily next round.",
                    55, ItemEffectKind.EnemySafeZonePenalty, 12f),
                ItemDefinition.Create(
                    "bartenders_spectacles", "Spyglass",
                    "Reveal the true spill odds on the glass next round.",
                    30, ItemEffectKind.RevealTrueOdds, 0f),
                ItemDefinition.Create(
                    "buy_the_house_a_round", "Buy a Round",
                    "Lower the glass right now, before you pour.",
                    45, ItemEffectKind.ReduceCurrentRisk, 15f),
                ItemDefinition.Create(
                    "step_outside", "Bandana",
                    "End your turn — pass the loaded glass to the dealer.",
                    40, ItemEffectKind.SkipTurn, 0f),
                ItemDefinition.Create(
                    "happy_hour", "Happy Hour",
                    "Every safe pour pays ×1.5 for the rest of this round.",
                    55, ItemEffectKind.RoundPayoutMultiplier, 1.5f),
                ItemDefinition.Create(
                    "recast_coin", "Recast",
                    "Recast your biggest coin one size up — more risk, more reward.",
                    45, ItemEffectKind.UpgradePlayerCoin, 1f),
                ItemDefinition.Create(
                    "taro_laps", "Taro",
                    "Taro laps the glass down — a big cut to the spill risk now.",
                    70, ItemEffectKind.ReduceCurrentRisk, 30f),
            };
    }
}
