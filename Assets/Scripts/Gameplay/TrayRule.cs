using System.Collections.Generic;

namespace Fonda
{
    public enum TrayEvaluation
    {
        /// <summary>Tray not full yet: nothing is evaluated.</summary>
        Incomplete,
        Correct,
        Wrong
    }

    /// <summary>
    /// Pure rule for judging a tray against an order. Independent of slot order, but duplicates count.
    /// An error is only reported once the tray is full (placed count == required count).
    /// </summary>
    public static class TrayRule
    {
        /// <param name="required">Ingredients of the active order.</param>
        /// <param name="placed">One entry per occupied slot; null means an occupant that is not an identifiable ingredient.</param>
        public static TrayEvaluation Evaluate(IReadOnlyList<IngredientData> required, IReadOnlyList<IngredientData> placed)
        {
            if (required == null || required.Count == 0 || placed == null || placed.Count < required.Count)
                return TrayEvaluation.Incomplete;

            if (placed.Count != required.Count)
                return TrayEvaluation.Wrong;

            var remaining = new Dictionary<IngredientData, int>();
            foreach (var r in required)
            {
                if (r == null) return TrayEvaluation.Wrong;
                remaining.TryGetValue(r, out var n);
                remaining[r] = n + 1;
            }

            foreach (var p in placed)
            {
                if (p == null || !remaining.TryGetValue(p, out var n) || n == 0)
                    return TrayEvaluation.Wrong;
                remaining[p] = n - 1;
            }
            return TrayEvaluation.Correct;
        }
    }
}
