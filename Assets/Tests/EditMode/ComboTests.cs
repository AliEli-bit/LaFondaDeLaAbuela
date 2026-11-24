using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda.Tests
{
    public class ComboTests
    {
        GameObject root;

        [SetUp] public void SetUp() => root = new GameObject("TestRoot");
        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        // S1: "cada entrega rápida seguida sube [...] la propina; un error reinicia el combo". A2: slow deliveries too.
        [Test]
        public void Combo_GrowsWithFastDeliveriesInARow_AndResets()
        {
            var combo = new ComboCounter();
            Assert.AreEqual(1, combo.Delivered(true));
            Assert.AreEqual(2, combo.Delivered(true));
            Assert.AreEqual(3, combo.Delivered(true));

            combo.Break(); // a wrong ingredient or an angry customer
            Assert.AreEqual(0, combo.Count);
            Assert.AreEqual(1, combo.Delivered(true));

            Assert.AreEqual(0, combo.Delivered(false), "a slow delivery breaks the streak");
            Assert.AreEqual(1, combo.Delivered(true));
            Assert.AreEqual(3, combo.Best, "the best combo of the shift is kept");
        }

        // A1: fast = more than 60 % of the patience left (the happy face).
        [TestCase(25f, true)]
        [TestCase(15.1f, true)]
        [TestCase(15f, false)]
        [TestCase(5f, false)]
        public void Fast_IsTheHappyFace(float remaining, bool fast)
        {
            Assert.AreEqual(fast, ComboRule.IsFast(remaining, 25f, 0.60f));
        }

        // A3: +1 coin per step from x2, up to +2.
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(7, 2)]
        public void TipBonus_GrowsWithTheCombo_UpToTheCap(int combo, int bonus)
        {
            Assert.AreEqual(bonus, ComboRule.TipBonus(combo, 1, 2));
        }

        // S2: "El monto depende de la paciencia que le quedaba al cliente y del combo". An angry customer leaves nothing.
        [Test]
        public void Seat_AddsTheComboBonus_OnlyForAServedCustomer()
        {
            var seat = new GameObject("Seat").AddComponent<BarSeat>();
            seat.transform.SetParent(root.transform);
            var spot = new GameObject("CoinSpot").transform;
            spot.SetParent(root.transform);
            var prefab = new GameObject("Coin");
            prefab.transform.SetParent(root.transform);
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<XRGrabInteractable>();
            var so = new SerializedObject(seat);
            so.FindProperty("coinSpot").objectReferenceValue = spot;
            so.FindProperty("coinPrefab").objectReferenceValue = prefab.AddComponent<TipCoin>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var served = new CustomerCycle(25f, 3f);
            served.Sit();
            served.Serve();
            var coins = seat.LeaveTip(served, 2);
            foreach (var c in coins) c.transform.SetParent(root.transform);
            Assert.AreEqual(5, coins.Count, "3 for full patience + 2 for the combo");
            Assert.AreEqual(5, coins.Select(c => c.transform.position).Distinct().Count(), "coins do not overlap");

            var angry = new CustomerCycle(25f, 3f);
            angry.Sit();
            angry.Tick(25f);
            Assert.AreEqual(0, seat.LeaveTip(angry, 2).Count);
        }

        // S3 + A6: the summary shows the best combo only when there was one.
        [Test]
        public void Summary_ShowsTheBestCombo()
        {
            var summary = new ShiftSummary();
            summary.RecordCustomer(false);
            summary.RecordCustomer(false);
            summary.TipsBanked = 9;
            summary.BestCombo = 2;
            Assert.AreEqual(
                "¡Se terminó el turno!\nClientes atendidos: 2\nSe fueron enojados: 0\nMejor combo: ×2\nPropinas: $9",
                summary.Text());

            summary.BestCombo = 1;
            Assert.AreEqual("¡Se terminó el turno!\nClientes atendidos: 2\nSe fueron enojados: 0\nPropinas: $9", summary.Text());
        }
    }
}
