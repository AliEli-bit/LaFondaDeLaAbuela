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
        public void IncompleteTray_ProducesNoEvaluation()
        {
            var order = new List<IngredientData> { arroz, huevo };

            // Even a wrong ingredient does not count as an error until the tray is full.
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData>()));
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData> { arroz }));
            Assert.AreEqual(TrayEvaluation.Incomplete, TrayRule.Evaluate(order, new List<IngredientData> { papa }));
        }

        [Test]
        public void FullTrayMatchingOrder_IsCorrect_InAnySlotOrder()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Correct, TrayRule.Evaluate(order, new List<IngredientData> { arroz, huevo }));
            Assert.AreEqual(TrayEvaluation.Correct, TrayRule.Evaluate(order, new List<IngredientData> { huevo, arroz }));
        }

        [Test]
        public void FullTrayNotMatchingOrder_IsWrong()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { arroz, papa }));
        }

        [Test]
        public void DuplicatesCount_ArrozArrozIsNotArrozHuevo()
        {
            var order = new List<IngredientData> { arroz, huevo };

            Assert.AreEqual(TrayEvaluation.Wrong, TrayRule.Evaluate(order, new List<IngredientData> { arroz, arroz }));
        }
    }
}
