using System.Collections.Generic;

namespace Fonda
{
    public enum TrayEvaluation
    {
        /// <summary>Everything placed so far is part of the order, but something is still missing.</summary>
        Incomplete,
        Correct,
        /// <summary>Something placed is not part of the order (not ordered, or more copies than ordered).</summary>
        Wrong
    }

    /// <summary>
    /// Pure rule for judging a tray against an order. Independent of slot order, but duplicates count.
    /// An error is reported as soon as the placed ingredients stop being part of the order,
    /// even if the tray is not full; removing the extra ingredient makes the tray valid again.
    /// </summary>
    public static class TrayRule
    {
        /// <param name="required">Ingredients of the active order.</param>
        /// <param name="placed">One entry per occupied slot; null means an occupant that is not an identifiable ingredient.</param>
        public static TrayEvaluation Evaluate(IReadOnlyList<IngredientData> required, IReadOnlyList<IngredientData> placed)
        {
            if (required == null || required.Count == 0 || placed == null)
                return TrayEvaluation.Incomplete;

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
            return placed.Count == required.Count ? TrayEvaluation.Correct : TrayEvaluation.Incomplete;
        }
    }
}
