namespace Meniscus.Core
{
    public readonly struct GlassDropResult
    {
        public GlassDropResult(
            TurnActor actor,
            float riskBeforeDrop,
            float addedRisk,
            float riskAfterDrop,
            float roll,
            bool overflowed,
            int coinCount,
            float trueSpillChance = 0f)
        {
            Actor = actor;
            RiskBeforeDrop = riskBeforeDrop;
            AddedRisk = addedRisk;
            RiskAfterDrop = riskAfterDrop;
            Roll = roll;
            Overflowed = overflowed;
            CoinCount = coinCount;
            TrueSpillChance = trueSpillChance;
        }

        public TurnActor Actor { get; }
        public float RiskBeforeDrop { get; }
        public float AddedRisk { get; }
        public float RiskAfterDrop { get; }
        public float TrueSpillChance { get; }
        public float Roll { get; }
        public bool Overflowed { get; }
        public int CoinCount { get; }
    }
}
