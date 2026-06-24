namespace Meniscus.Items
{
    /// <summary>
    /// The lever an item pulls when bought. Each kind maps to one existing manager hook in
    /// <see cref="ItemEffectApplier"/>, so a brand-new item is usually just a new catalog entry with a
    /// different magnitude/cost — no new code. Add a kind here only when an item needs a genuinely new
    /// mechanic.
    /// </summary>
    public enum ItemEffectKind
    {
        PayoutMultiplier,
        SafeZoneBonus,
        ForceEnemyCoins,
        EnemySafeZonePenalty,
        RevealTrueOdds,
        ReduceCurrentRisk,
        SkipTurn,
        // A payout multiplier that lasts the WHOLE round (every safe pour), not just the next one.
        RoundPayoutMultiplier,
        // Recasts one of the player's coins one size up — more risk, more reward.
        UpgradePlayerCoin
    }
}
