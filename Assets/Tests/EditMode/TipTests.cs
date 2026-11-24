using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda.Tests
{
    public class TipTests
    {
        GameObject root;

        [SetUp] public void SetUp() => root = new GameObject("TestRoot");
        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            return go;
        }

        TipCoin Coin(int value = 1)
        {
            var go = Child("Coin");
            go.AddComponent<Rigidbody>();
            go.AddComponent<XRGrabInteractable>();
            var coin = go.AddComponent<TipCoin>();
            var so = new SerializedObject(coin);
            so.FindProperty("value").intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return coin;
        }

        // S1 + A1: 1 to 3 coins by the patience left when served (half rounds up); none when the customer left angry.
        [TestCase(25f, false, 3)]
        [TestCase(12.5f, false, 2)]
        [TestCase(6.25f, false, 2)]
        [TestCase(2f, false, 1)]
        [TestCase(0f, true, 0)]
        public void Coins_DependOnPatience_AndAngryLeavesNothing(float remaining, bool leftAngry, int expected)
        {
            Assert.AreEqual(expected, TipRule.CoinsFor(remaining, 25f, leftAngry, 1, 3));
        }

        // S1: the seat drops the tip next to its tray only for a customer who ate.
        [Test]
        public void Seat_LeavesCoinsAtItsSpot_OnlyForAServedCustomer()
        {
            var seat = Child("Seat").AddComponent<BarSeat>();
            var spot = Child("CoinSpot").transform;
            spot.position = new Vector3(0.2f, 0.9f, 0.4f);
            var prefab = Coin();
            var so = new SerializedObject(seat);
            so.FindProperty("coinSpot").objectReferenceValue = spot;
            so.FindProperty("coinPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            var served = new CustomerCycle(25f, 3f);
            served.Sit();
            served.Serve();
            served.Tick(3f);
            var coins = seat.LeaveTip(served);
            foreach (var c in coins) c.transform.SetParent(root.transform);
            Assert.AreEqual(3, coins.Count);
            Assert.IsTrue(coins.All(c => Vector3.Distance(c.transform.position, spot.position) < 0.06f));

            var angry = new CustomerCycle(25f, 3f);
            angry.Sit();
            angry.Tick(25f);
            Assert.AreEqual(0, seat.LeaveTip(angry).Count);
        }

        // S2 + A3: a released coin in the jar counts once, adds its value and raises the event.
        [Test]
        public void Jar_CountsAReleasedCoinOnce()
        {
            var jar = Child("Jar").AddComponent<TipJar>();
            int events = 0, lastTotal = -1;
            jar.TipBanked += (value, total) => { events++; lastTotal = total; };
            var coin = Coin(2);

            Assert.IsTrue(jar.Deposit(coin));
            Assert.IsFalse(jar.Deposit(coin), "the same coin again");
            Assert.AreEqual(2, jar.Total);
            Assert.AreEqual(1, events);
            Assert.AreEqual(2, lastTotal);
            Assert.IsFalse(jar.Deposit(null));
        }
    }
}
