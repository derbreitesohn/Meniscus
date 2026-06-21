using Meniscus.Core;
using UnityEngine;

namespace Meniscus.Items
{
    /// <summary>
    /// Translates an <see cref="ItemDefinition"/> into a concrete game effect by calling the existing
    /// queue-bonus hooks on the managers. This is the single switch a new effect kind touches.
    /// </summary>
    public static class ItemEffectApplier
    {
        public static void Apply(
            ItemDefinition item,
            EconomyManager economy,
            GlassManager glass,
            GameManager game)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemEffectApplier] Ignored null item.");
                return;
            }

            switch (item.Effect)
            {
                case ItemEffectKind.PayoutMultiplier:
                    if (economy != null)
                        economy.QueueNextSafeDropPayoutMultiplier(item.Magnitude);
                    else
                        WarnMissing(item, nameof(EconomyManager));
                    break;

                case ItemEffectKind.SafeZoneBonus:
                    if (glass != null)
                        glass.AddPlayerPourRelief(item.Magnitude);
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.ForceEnemyCoins:
                    if (game != null)
                        game.QueueEnemyForcedCoinCount(Mathf.RoundToInt(item.Magnitude));
                    else
                        WarnMissing(item, nameof(GameManager));
                    break;

                case ItemEffectKind.EnemySafeZonePenalty:
                    if (glass != null)
                        glass.AddEnemyPourPenalty(item.Magnitude);
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.RevealTrueOdds:
                    if (glass != null)
                        glass.RevealTrueOddsForRound();
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.ReduceCurrentRisk:
                    if (glass != null)
                        glass.ReduceCurrentRisk(item.Magnitude);
                    else
                        WarnMissing(item, nameof(GlassManager));
                    break;

                case ItemEffectKind.SkipTurn:
                    if (game != null)
                        game.SkipPlayerTurn();
                    else
                        WarnMissing(item, nameof(GameManager));
                    break;

                default:
                    Debug.LogWarning($"[ItemEffectApplier] Unhandled effect '{item.Effect}' for item '{item.Id}'.");
                    break;
            }
        }

        static void WarnMissing(ItemDefinition item, string managerName) =>
            Debug.LogWarning(
                $"[ItemEffectApplier] Applied '{item.Id}' but {managerName} is missing; effect had no target.");
    }
}
