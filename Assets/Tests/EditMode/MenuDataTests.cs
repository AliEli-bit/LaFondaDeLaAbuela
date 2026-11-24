using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Fonda.Tests
{
    /// <summary>The generated data assets match the confirmed menu (CLAUDE.md §5, S1, S8).</summary>
    public class MenuDataTests
    {
        static IngredientData[] LoadIngredients() =>
            AssetDatabase.FindAssets("t:IngredientData", new[] { "Assets/Data/Ingredients" })
                .Select(g => AssetDatabase.LoadAssetAtPath<IngredientData>(AssetDatabase.GUIDToAssetPath(g)))
                .ToArray();

        static RecipeData LoadRecipe(string displayName) =>
            AssetDatabase.FindAssets("t:RecipeData", new[] { "Assets/Data/Recipes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<RecipeData>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(r => r.DisplayName == displayName);

        [Test]
        public void Ingredients_AreTheSixOfTheMenu()
        {
            var ids = LoadIngredients().Select(i => i.Id).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(
                new[] { "lechuga", "lomito", "medallon", "papas", "queso", "tomate" }, ids);
        }

        [TestCase("Hamburguesa sola", DishBase.Bun, new[] { "medallon" })]
        [TestCase("Tazón de papas", DishBase.Bowl, new[] { "papas" })]
        [TestCase("Hamburguesa con queso", DishBase.Bun, new[] { "medallon", "queso" })]
        [TestCase("Lomito con tomate", DishBase.Bun, new[] { "lomito", "tomate" })]
        [TestCase("Hamburguesa completa", DishBase.Bun, new[] { "medallon", "lechuga", "tomate" })]
        [TestCase("Lomito completo", DishBase.Bun, new[] { "lomito", "queso", "tomate" })]
        public void Recipe_HasItsBaseAndIngredients(string dish, DishBase expectedBase, string[] expectedIds)
        {
            var recipe = LoadRecipe(dish);
            Assert.IsNotNull(recipe, "Missing recipe " + dish);
            Assert.AreEqual(expectedBase, recipe.Base);
            CollectionAssert.AreEquivalent(expectedIds, recipe.Ingredients.Select(i => i.Id).ToArray());
        }
    }
}
