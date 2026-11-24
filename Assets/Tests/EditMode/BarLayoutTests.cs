using NUnit.Framework;
using UnityEngine;

namespace Fonda.Tests
{
    /// <summary>S1: the station follows the GDD curved-bar layout (player at the origin facing +z, angles from +z).</summary>
    public class BarLayoutTests
    {
        static float AngleOf(Vector3 p) => Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
        static float RadiusOf(Vector3 p) => new Vector2(p.x, p.z).magnitude;

        [TestCase("lechuga", -70f)]
        [TestCase("tomate", -42f)]
        [TestCase("queso", -14f)]
        [TestCase("medallon", 14f)]
        [TestCase("lomito", 42f)]
        [TestCase("papas", 70f)]
        public void Ingredient_SitsOnTheArcAt033(string id, float degrees)
        {
            var p = FondaCoreLoopBuilder.DispenserPosition(id);
            Assert.AreEqual(0.33f, RadiusOf(p), 1e-3f);
            Assert.AreEqual(degrees, AngleOf(p), 1e-2f);
        }

        // Seat order is center, left, right (A4); one tray in front of each seat.
        [TestCase(0, 0f)]
        [TestCase(1, -40f)]
        [TestCase(2, 40f)]
        public void TrayAndSeat_AreOnTheirArcs(int seat, float degrees)
        {
            var tray = FondaCoreLoopBuilder.TrayPosition(seat);
            Assert.AreEqual(0.50f, RadiusOf(tray), 1e-3f);
            Assert.AreEqual(degrees, AngleOf(tray), 1e-2f);

            var stool = FondaCoreLoopBuilder.SeatPosition(seat);
            Assert.AreEqual(1.00f, RadiusOf(stool), 1e-3f);
            Assert.AreEqual(degrees, AngleOf(stool), 1e-2f);
        }

        // Existing check kept: everything interactable within reach, on the bar, away from the other sockets.
        [Test]
        public void Layout_PassesTheBuilderValidation()
        {
            CollectionAssert.IsEmpty(FondaCoreLoopBuilder.ValidateLayout());
        }
    }
}
