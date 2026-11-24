namespace Fonda
{
    /// <summary>
    /// Speed combo over the whole shift (combo S1): each fast delivery in a row raises it; a slow delivery, a wrong
    /// ingredient or a customer leaving angry resets it (A2).
    /// </summary>
    public class ComboCounter
    {
        public int Count { get; private set; }
        public int Best { get; private set; }

        /// <summary>A plate was delivered. Returns the combo after it (0 when it was slow).</summary>
        public int Delivered(bool fast)
        {
            Count = fast ? Count + 1 : 0;
            if (Count > Best) Best = Count;
            return Count;
        }

        public void Break() => Count = 0;
    }

    public static class ComboRule
    {
        /// <summary>Fast = more than <paramref name="fastAbove"/> of the patience left when served (A1).</summary>
        public static bool IsFast(float remaining, float max, float fastAbove) => max > 0f && remaining / max > fastAbove;

        /// <summary>Extra coins: <paramref name="perStep"/> per combo step from the second one, up to <paramref name="maxBonus"/> (A3).</summary>
        public static int TipBonus(int combo, int perStep, int maxBonus)
        {
            if (combo < 2 || perStep <= 0 || maxBonus <= 0) return 0;
            int bonus = (combo - 1) * perStep;
            return bonus < maxBonus ? bonus : maxBonus;
        }
    }
}
