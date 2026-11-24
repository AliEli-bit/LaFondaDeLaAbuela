using System;
using System.Collections.Generic;

namespace Fonda
{
    /// <summary>Engine-free time of one shift: elapsed seconds, <c>t</c> and the current scene.</summary>
    public class ShiftClock
    {
        readonly DifficultySettings settings;

        public ShiftClock(DifficultySettings settings) => this.settings = settings;

        public float Elapsed { get; private set; }
        public float T => settings.ShiftSeconds > 0f ? Math.Min(1f, Elapsed / settings.ShiftSeconds) : 1f;
        public ShiftPhase Phase => settings.PhaseAt(Elapsed);
        public bool Ended => Phase == ShiftPhase.Closed;

        /// <summary>Moves time forward. Returns true only on the tick the shift runs out.</summary>
        public bool Advance(float dt)
        {
            if (Ended || dt <= 0f) return false;
            Elapsed = Math.Min(Elapsed + dt, settings.ShiftSeconds);
            return Ended;
        }
    }

    /// <summary>Angles of the wall-clock hands, clockwise from 12 o'clock, for an hour of the day.</summary>
    public static class ClockHands
    {
        public static (float hourDegrees, float minuteDegrees) Angles(float hourOfDay)
        {
            float onDial = hourOfDay % 12f;
            if (onDial < 0f) onDial += 12f;
            float minutes = onDial % 1f;
            return (onDial * 30f, minutes * 360f);
        }
    }

    public static class MenuRule
    {
        /// <summary>
        /// Indexes of the dishes with exactly <paramref name="target"/> ingredients; if there are none, the dishes
        /// with the closest count (A4).
        /// </summary>
        public static List<int> Candidates(IReadOnlyList<int> ingredientCounts, int target)
        {
            var result = new List<int>();
            if (ingredientCounts == null) return result;
            int best = int.MaxValue;
            for (int i = 0; i < ingredientCounts.Count; i++)
            {
                int distance = Math.Abs(ingredientCounts[i] - target);
                if (distance < best)
                {
                    best = distance;
                    result.Clear();
                }
                if (distance == best) result.Add(i);
            }
            return result;
        }
    }

    /// <summary>What the end-of-shift summary shows (S3, A5).</summary>
    public class ShiftSummary
    {
        public int Served { get; private set; }
        public int LeftAngry { get; private set; }
        public int TipsBanked { get; set; }
        public int TipsLeftover { get; set; }
        public int TipsTotal => TipsBanked + TipsLeftover;
        public int BestCombo { get; set; }

        public void RecordCustomer(bool leftAngry)
        {
            if (leftAngry) LeftAngry++;
            else Served++;
        }

        public string Text()
        {
            var text = "¡Se terminó el turno!\nClientes atendidos: " + Served + "\nSe fueron enojados: " + LeftAngry;
            if (BestCombo >= 2) text += "\nMejor combo: ×" + BestCombo;
            text += "\nPropinas: $" + TipsTotal;
            if (TipsLeftover > 0) text += "\n($" + TipsLeftover + " sumadas de la barra)";
            return text;
        }
    }
}
