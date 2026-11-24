using System.Collections.Generic;

namespace Fonda
{
    /// <summary>
    /// Which type of customer comes in and what it orders (customer types A3-A5): any type at any time, ordering its favourite closest to the clock.
    /// </summary>
    public static class CustomerTypeRule
    {
        /// <summary>Menu indices this type orders: its favourites, or the whole menu when it has none.</summary>
        public static List<int> Dishes(CustomerProfile profile, IReadOnlyList<RecipeData> menu)
        {
            var dishes = new List<int>();
            bool any = profile == null || profile.Favorites.Count == 0;
            for (int i = 0; i < menu.Count; i++)
            {
                if (menu[i] == null) continue;
                if (any || Contains(profile.Favorites, menu[i])) dishes.Add(i);
            }
            return dishes;
        }

        /// <summary>Menu indices for this type with <paramref name="target"/> ingredients, or the closest count (A4).</summary>
        public static List<int> DishCandidates(CustomerProfile profile, IReadOnlyList<RecipeData> menu, int target)
        {
            var dishes = Dishes(profile, menu);
            var counts = new List<int>(dishes.Count);
            foreach (var i in dishes) counts.Add(menu[i].Ingredients.Count);
            var picked = MenuRule.Candidates(counts, target);
            for (int k = 0; k < picked.Count; k++) picked[k] = dishes[picked[k]];
            return picked;
        }

        /// <summary>Indices of the types with at least one dish on the menu: any type comes in at any time (A4).</summary>
        public static List<int> Eligible(IReadOnlyList<CustomerProfile> profiles, IReadOnlyList<RecipeData> menu)
        {
            var eligible = new List<int>();
            for (int p = 0; p < profiles.Count; p++)
                if (profiles[p] != null && Dishes(profiles[p], menu).Count > 0) eligible.Add(p);
            return eligible;
        }

        /// <summary>Picks one of <paramref name="eligible"/> by weight with <paramref name="roll"/> in [0, 1) (A5). -1 when empty.</summary>
        public static int Pick(IReadOnlyList<CustomerProfile> profiles, IReadOnlyList<int> eligible, float roll)
        {
            if (eligible == null || eligible.Count == 0) return -1;
            float total = 0f;
            foreach (var p in eligible) total += Weight(profiles[p]);
            if (total <= 0f) return eligible[0];
            float left = roll * total;
            foreach (var p in eligible)
            {
                float w = Weight(profiles[p]);
                if (w <= 0f) continue;
                if (left < w) return p;
                left -= w;
            }
            // roll at 1 or rounding: the last type with weight
            for (int k = eligible.Count - 1; k >= 0; k--)
                if (Weight(profiles[eligible[k]]) > 0f) return eligible[k];
            return eligible[0];
        }

        static float Weight(CustomerProfile profile) => profile != null ? profile.Weight : 0f;

        static bool Contains(IReadOnlyList<RecipeData> list, RecipeData recipe)
        {
            foreach (var r in list) if (r == recipe) return true;
            return false;
        }
    }
}
