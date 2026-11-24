using System.Collections.Generic;

namespace Fonda
{
    /// <summary>
    /// Where each ingredient of a dish rests: the layers pile up on the base (bun or bowl) without gaps,
    /// bottom to top in the order they arrived. Heights are in the tray's space.
    /// </summary>
    public static class TrayStack
    {
        /// <summary>Center height of each layer, bottom to top, for layers of these thicknesses on the base.</summary>
        public static float[] Centers(float baseTop, IReadOnlyList<float> thicknesses)
        {
            var centers = new float[thicknesses.Count];
            float top = baseTop;
            for (int i = 0; i < thicknesses.Count; i++)
            {
                centers[i] = top + thicknesses[i] / 2f;
                top += thicknesses[i];
            }
            return centers;
        }

        /// <summary>Height of the top of the pile, where the next layer lands.</summary>
        public static float Top(float baseTop, IReadOnlyList<float> thicknesses)
        {
            float top = baseTop;
            foreach (var t in thicknesses) top += t;
            return top;
        }
    }
}
