using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// How many coins a customer leaves (S1, A1): from <c>minCoins</c> to <c>maxCoins</c> by the share of patience
    /// left when served, halves rounding up. A customer who left angry did not eat and leaves nothing.
    /// </summary>
    public static class TipRule
    {
        public static int CoinsFor(float remaining, float max, bool leftAngry, int minCoins, int maxCoins)
        {
            if (leftAngry || maxCoins <= 0) return 0;
            float fraction = max > 0f ? Mathf.Clamp01(remaining / max) : 0f;
            return minCoins + Mathf.FloorToInt((maxCoins - minCoins) * fraction + 0.5f);
        }

        public static int CoinsFor(CustomerCycle cycle, int minCoins, int maxCoins) =>
            cycle == null ? 0 : CoinsFor(cycle.Patience, cycle.MaxPatience, cycle.LeftAngry, minCoins, maxCoins);
    }
}
