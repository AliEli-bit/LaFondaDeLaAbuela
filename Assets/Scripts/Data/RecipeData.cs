using System.Collections.Generic;
using UnityEngine;

namespace Fonda
{
    /// <summary>A dish: its display name and the ingredients it requires (duplicates allowed).</summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Fonda/Recipe Data")]
    public class RecipeData : ScriptableObject
    {
        [SerializeField] string displayName;
        [SerializeField] List<IngredientData> ingredients = new List<IngredientData>();

        public string DisplayName => displayName;
        public IReadOnlyList<IngredientData> Ingredients => ingredients;
    }
}
