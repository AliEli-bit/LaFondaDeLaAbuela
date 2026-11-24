using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda.Tests
{
    public class ShiftTests
    {
        DifficultySettings settings;
        GameObject root;

        [SetUp]
        public void SetUp()
        {
            settings = ScriptableObject.CreateInstance<DifficultySettings>();
            root = new GameObject("TestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(settings);
            Object.DestroyImmediate(root);
        }

        TipCoin Coin(int value = 1)
        {
            var go = new GameObject("Coin");
            go.transform.SetParent(root.transform);
            go.AddComponent<Rigidbody>();
            go.AddComponent<XRGrabInteractable>();
            var coin = go.AddComponent<TipCoin>();
            var so = new SerializedObject(coin);
            so.FindProperty("value").intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return coin;
        }

        // S2: "Intervalo entre clientes = 12 − 7·t", "Paciencia del cliente = 25 − 10·t",
        // "Ingredientes por pedido = 1 + round(2·t) (1 a 3)", with half rounding up (A4).
        [TestCase(0f, 12f, 25f, 1)]
        [TestCase(0.2f, 10.6f, 23f, 1)]
        [TestCase(0.25f, 10.25f, 22.5f, 2)]
        [TestCase(0.5f, 8.5f, 20f, 2)]
        [TestCase(0.75f, 6.75f, 17.5f, 3)]
        [TestCase(1f, 5f, 15f, 3)]
        public void Difficulty_FollowsTheClockFormula(float t, float interval, float patience, int ingredients)
        {
            Assert.AreEqual(interval, settings.IntervalAt(t), 1e-4f);
            Assert.AreEqual(patience, settings.PatienceAt(t), 1e-4f);
            Assert.AreEqual(ingredients, settings.IngredientsAt(t));
        }

        // S3 + A1: E1 ≈ 1 min, E2 ≈ 3 min, E3 ≈ 1:30, then closed. S2: 1 / 2 / 3 customers at once.
        [TestCase(0f, ShiftPhase.Opening, 1)]
        [TestCase(59.9f, ShiftPhase.Opening, 1)]
        [TestCase(60f, ShiftPhase.Normal, 2)]
        [TestCase(239.9f, ShiftPhase.Normal, 2)]
        [TestCase(240f, ShiftPhase.Rush, 3)]
        [TestCase(329.9f, ShiftPhase.Rush, 3)]
        [TestCase(330f, ShiftPhase.Closed, 0)]
        public void Phase_AndMaxCustomers_FollowTheScenes(float elapsed, ShiftPhase phase, int maxCustomers)
        {
            Assert.AreEqual(330f, settings.ShiftSeconds, 1e-4f);
            Assert.AreEqual(phase, settings.PhaseAt(elapsed));
            Assert.AreEqual(maxCustomers, settings.MaxCustomers(phase));
        }

        // S2: "t = tiempo transcurrido / duración total del turno, de 0 a 1". The end is reported once.
        [Test]
        public void Clock_ComputesT_AndReportsTheEndOnce()
        {
            var clock = new ShiftClock(settings);
            Assert.AreEqual(0f, clock.T, 1e-5f);
            Assert.IsFalse(clock.Advance(165f));
            Assert.AreEqual(0.5f, clock.T, 1e-5f);
            Assert.AreEqual(ShiftPhase.Normal, clock.Phase);
            Assert.IsFalse(clock.Ended);

            Assert.IsTrue(clock.Advance(200f), "the tick that runs out reports the end");
            Assert.IsTrue(clock.Ended);
            Assert.AreEqual(1f, clock.T, 1e-5f);
            Assert.AreEqual(ShiftPhase.Closed, clock.Phase);
            Assert.IsFalse(clock.Advance(1f), "the end is reported only once");
        }

        // A3: the wall clock goes from 15:00 to 20:00 over the shift; hands measured clockwise from 12.
        [TestCase(0f, 15f, 90f, 0f)]
        [TestCase(0.5f, 17.5f, 165f, 180f)]
        [TestCase(1f, 20f, 240f, 0f)]
        public void WallClock_ShowsTheHour(float t, float hour, float hourHand, float minuteHand)
        {
            Assert.AreEqual(hour, settings.HourAt(t), 1e-4f);
            var (h, m) = ClockHands.Angles(hour);
            Assert.AreEqual(hourHand, h, 1e-3f);
            Assert.AreEqual(minuteHand, m, 1e-3f);
        }

        // A4: a dish with exactly that many ingredients, or the closest count when there is none.
        [Test]
        public void Menu_PicksDishesWithTheRightIngredientCount()
        {
            var menu = new List<int> { 1, 1, 2, 2, 3, 3 };
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, MenuRule.Candidates(menu, 1));
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, MenuRule.Candidates(menu, 2));
            CollectionAssert.AreEquivalent(new[] { 4, 5 }, MenuRule.Candidates(menu, 3));
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, MenuRule.Candidates(new List<int> { 1, 3 }, 2));
            CollectionAssert.AreEquivalent(new[] { 1 }, MenuRule.Candidates(new List<int> { 1, 2 }, 3));
            CollectionAssert.IsEmpty(MenuRule.Candidates(new List<int>(), 2));
        }

        // S3 + A5: "clientes atendidos y propinas".
        [Test]
        public void Summary_CountsCustomersAndTips()
        {
            var summary = new ShiftSummary();
            summary.RecordCustomer(false);
            summary.RecordCustomer(false);
            summary.RecordCustomer(true);
            summary.TipsBanked = 5;
            summary.TipsLeftover = 2;

            Assert.AreEqual(2, summary.Served);
            Assert.AreEqual(1, summary.LeftAngry);
            Assert.AreEqual(7, summary.TipsTotal);
            Assert.AreEqual(
                "¡Se terminó el turno!\nClientes atendidos: 2\nSe fueron enojados: 1\nPropinas: $7\n($2 sumadas de la barra)",
                summary.Text());

            var clean = new ShiftSummary();
            clean.RecordCustomer(false);
            clean.TipsBanked = 3;
            Assert.AreEqual("¡Se terminó el turno!\nClientes atendidos: 1\nSe fueron enojados: 0\nPropinas: $3", clean.Text());
        }

        // S4: "se suman solas al final del turno". Coins already counted are not added again.
        [Test]
        public void Jar_BanksLeftoverCoinsAtTheEnd()
        {
            var jar = new GameObject("Jar").AddComponent<TipJar>();
            jar.transform.SetParent(root.transform);
            var counted = Coin(1);
            Assert.IsTrue(jar.Deposit(counted));

            var leftovers = new List<TipCoin> { Coin(1), Coin(2), null };
            Assert.AreEqual(3, jar.BankLeftovers(leftovers));
            Assert.AreEqual(4, jar.Total);
            Assert.AreEqual(0, root.GetComponentsInChildren<TipCoin>().Length, "banked coins are removed");
            Assert.AreEqual(0, jar.BankLeftovers(new List<TipCoin>()));
        }

        // S1: "el reloj a 2,20 de alto" on the back wall, in front of it.
        [Test]
        public void WallClock_HangsOnTheBackWall()
        {
            var p = FondaCoreLoopBuilder.WallClockPosition();
            Assert.AreEqual(2.20f, p.y, 1e-3f);
            Assert.Less(p.z, 3.60f);
            Assert.Greater(p.z, 3.50f);
        }
    }
}
