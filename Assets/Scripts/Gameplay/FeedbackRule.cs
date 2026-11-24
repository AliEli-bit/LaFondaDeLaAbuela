using UnityEngine;

namespace Fonda
{
    /// <summary>Pure rules of the feedback sounds (feedback S3, A5).</summary>
    public static class FeedbackRule
    {
        /// <summary>The bell's pitch for a delivery with this combo: 1 up to x1, then +step per delivery, up to max (S3, A3).</summary>
        public static float BellPitch(int combo, float step, float max)
        {
            if (combo <= 1) return 1f;
            return Mathf.Min(1f + Mathf.Max(0f, step) * (combo - 1), Mathf.Max(1f, max));
        }

        /// <summary>
        /// One of <paramref name="count"/> variants with <paramref name="roll"/> in [0, 1), never <paramref name="last"/>
        /// when there are two or more (A5). -1 when there is none.
        /// </summary>
        public static int PickVariant(int count, int last, float roll)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            roll = Mathf.Clamp01(roll);
            if (last < 0 || last >= count) return Mathf.Min((int)(roll * count), count - 1);
            // Pick among the others: shift past the last one.
            int k = Mathf.Min((int)(roll * (count - 1)), count - 2);
            return k >= last ? k + 1 : k;
        }
    }
}
