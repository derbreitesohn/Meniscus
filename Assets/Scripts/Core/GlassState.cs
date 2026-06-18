using System;

namespace Meniscus.Core
{
    [Serializable]
    public class GlassState
    {
        public int CoinsInGlass;
        public int OverflowThreshold;
        public bool HasOverflowed;
        public ParticipantId? OverflowCausedBy;

        public float FillNormalized =>
            OverflowThreshold <= 0 ? 0f : Math.Min(1f, (float)CoinsInGlass / OverflowThreshold);

        public void Reset(int overflowThreshold)
        {
            CoinsInGlass = 0;
            OverflowThreshold = overflowThreshold;
            HasOverflowed = false;
            OverflowCausedBy = null;
        }

        public void AddCoins(int amount, ParticipantId depositor)
        {
            CoinsInGlass += amount;

            if (!HasOverflowed && CoinsInGlass >= OverflowThreshold)
            {
                HasOverflowed = true;
                OverflowCausedBy = depositor;
            }
        }
    }
}
