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
                    "steady_hand", "Tschick",
                    "Light one up — steady your nerves and shrug off 10 risk next round.",
                    50, ItemEffectKind.SafeZoneBonus, 10f),
                ItemDefinition.Create(
                    "dealers_debt", "Dealer's Debt",
                    "The dealer must pour two coins next turn.",
                    60, ItemEffectKind.ForceEnemyCoins, 2f),
                ItemDefinition.Create(
                    "bartenders_spectacles", "Spyglass",
                    "Reveal the true spill odds on the glass next round.",
                    30, ItemEffectKind.RevealTrueOdds, 0f),
                ItemDefinition.Create(
                    "dog_whistle", "Pfeifi",
                    "Blow the whistle — your dog harries the dealer, who overflows more easily next round.",
                    50, ItemEffectKind.EnemySafeZonePenalty, 15f),
                ItemDefinition.Create(
                    "step_outside", "Bandana",
                    "End your turn — pass the loaded glass to the dealer.",
                    40, ItemEffectKind.SkipTurn, 0f),
                ItemDefinition.Create(
                    "taro_laps", "Taro",
                    "Taro laps the glass down — a big cut to the spill risk now.",
                    70, ItemEffectKind.ReduceCurrentRisk, 30f),
            };
    }
}
