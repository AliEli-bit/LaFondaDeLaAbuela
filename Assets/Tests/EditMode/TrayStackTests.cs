using NUnit.Framework;
using UnityEngine;

namespace Fonda.Tests
{
    /// <summary>The dish is piled on its base: each layer rests on the one below, without gaps.</summary>
    public class TrayStackTests
    {
        [Test]
        public void Centers_PileTheLayersOnTheBase()
        {
            var centers = TrayStack.Centers(0.036f, new[] { 0.025f, 0.012f, 0.015f });

            Assert.AreEqual(3, centers.Length);
            Assert.AreEqual(0.036f + 0.0125f, centers[0], 1e-5f, "the first layer rests on the base");
            Assert.AreEqual(0.036f + 0.025f + 0.006f, centers[1], 1e-5f, "the second rests on the first");
            Assert.AreEqual(0.036f + 0.025f + 0.012f + 0.0075f, centers[2], 1e-5f, "the third rests on the second");
        }

        [Test]
        public void Top_IsTheBase_WhenTheDishIsEmpty_AndRisesWithEachLayer()
        {
            Assert.AreEqual(0.036f, TrayStack.Top(0.036f, new float[0]), 1e-5f);
            Assert.AreEqual(0.036f + 0.025f + 0.012f, TrayStack.Top(0.036f, new[] { 0.025f, 0.012f }), 1e-5f);
        }

        // The ingredient prefabs are scaled primitives; a cylinder carries a box twice as tall as its unit size.
        [Test]
        public void IngredientThickness_IsTheHeightOfItsCollider()
        {
            var go = new GameObject("Medallon");
            try
            {
                go.transform.localScale = new Vector3(0.08f, 0.01f, 0.08f);
                go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
                var ingredient = go.AddComponent<Ingredient>();
                Assert.AreEqual(0.02f, ingredient.Thickness, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
