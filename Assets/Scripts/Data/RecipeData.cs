using System.Collections.Generic;
using UnityEngine;

namespace Fonda
{
    /// <summary>What a dish is served on. The base appears on the tray by itself and is not an ingredient.</summary>
    public enum DishBase { Bun, Bowl }

    /// <summary>A dish: its display name, its base and the ingredients it requires (duplicates allowed).</summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Fonda/Recipe Data")]
    public class RecipeData : ScriptableObject
    {
        [SerializeField] string displayName;
        [SerializeField] DishBase dishBase;
        [SerializeField] List<IngredientData> ingredients = new List<IngredientData>();

        public string DisplayName => displayName;
        public DishBase Base => dishBase;
        public IReadOnlyList<IngredientData> Ingredients => ingredients;
    }
}
