using System.Collections.Generic;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// A type of customer (customer types S1-S2): its Blender look, patience, favourite dishes, tip, eating time and how
    /// often it comes in. Sauce reactions, phrases and voice are left for their own features (A7).
    /// </summary>
    [CreateAssetMenu(fileName = "CustomerProfile", menuName = "Fonda/Customer Profile")]
    public class CustomerProfile : ScriptableObject
    {
        [SerializeField] string displayName;
        [Tooltip("Index into the customer prefab's looks, in FondaCoreLoopBuilder.CustomerTypes order. -1: random look.")]
        [SerializeField] int lookIndex = -1;
        [Tooltip("Multiplies the patience the Fonda clock gives.")]
        [SerializeField, Min(0.05f)] float patienceMultiplier = 1f;
        [Tooltip("Multiplies the eating time of the shift.")]
        [SerializeField, Min(0f)] float eatMultiplier = 1f;
        [Tooltip("Coins left with no patience left (A1).")]
        [SerializeField, Min(0)] int minCoins = 1;
        [Tooltip("Coins left with full patience (A1).")]
        [SerializeField, Min(0)] int maxCoins = 3;
        [Tooltip("Dishes this type orders. Empty: any dish on the menu.")]
        [SerializeField] List<RecipeData> favorites = new List<RecipeData>();
        [Tooltip("First line of the order bubble instead of the dish name, e.g. \"¡Lo de siempre, doña!\". Empty: the dish name.")]
        [SerializeField] string orderTitle;
        [Tooltip("How often this type comes in, relative to the others (A5).")]
        [SerializeField, Min(0f)] float weight = 1f;

        public string DisplayName => displayName;
        public int LookIndex => lookIndex;
        public float PatienceMultiplier => patienceMultiplier;
        public float EatMultiplier => eatMultiplier;
        public int MinCoins => minCoins;
        public int MaxCoins => maxCoins;
        public IReadOnlyList<RecipeData> Favorites => favorites;
        public string OrderTitle => orderTitle;
        public float Weight => weight;

        /// <summary>This type's patience for the patience the Fonda clock gives now.</summary>
        public float PatienceFor(float clockPatience) => clockPatience * patienceMultiplier;
        /// <summary>This type's eating time for the shift's eating time (A2).</summary>
        public float EatFor(float shiftEatSeconds) => shiftEatSeconds * eatMultiplier;
    }
}
