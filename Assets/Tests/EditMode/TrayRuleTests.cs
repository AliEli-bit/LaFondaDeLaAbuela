using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Fonda.Tests
{
    public class TrayRuleTests
    {
        IngredientData arroz, huevo, papa;
        readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            arroz = Make("arroz");
            huevo = Make("huevo");
            papa = Make("papa");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created) Object.DestroyImmediate(o);
            created.Clear();
        }

        IngredientData Make(string id)
        {
            var data = ScriptableObject.CreateInstance<IngredientData>();
            data.name = id;
            created.Add(data);
            return data;
        }

        [Test]
        public void PartOfTheOrder_IsIncomplete()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData>()));
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData> { arroz }));
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData> { huevo }));
        }

        [Test]
        public void FullTrayMatchingOrder_IsCorrect_InAnySlotOrder()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Correct, TrayRule.Evaluate(order, new List<IngredientData> { arroz, huevo }));
            Assert.AreEqual(TrayEvaluation.Correct, TrayRule.Evaluate(order, new List<IngredientData> { huevo, arroz }));
        }

        [Test]
        public void IngredientNotOrdered_IsWrongImmediately_EvenIfTrayNotFull()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { papa }));
            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { arroz, papa }));
        }

        [Test]
        public void MoreCopiesThanOrdered_IsWrongImmediately()
        {
            var order = new List<IngredientData> { arroz, huevo, papa };

            // The second arroz is not part of the order, even though the tray still has a free slot.
            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { arroz, arroz }));
        }

        [Test]
        public void RemovingTheExtraIngredient_ReturnsToIncompleteOrCorrect()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { arroz, papa }));
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData> { arroz }));
            Assert.AreEqual(TrayEvaluation.Correct, TrayRule.Evaluate(order, new List<IngredientData> { arroz, huevo }));
        }

        [Test]
        public void UnidentifiedOccupant_IsWrong()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { null }));
        }
    }
}
