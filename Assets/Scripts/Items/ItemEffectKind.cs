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
        SkipTurn
    }
}
