using NUnit.Framework;
using UnityEngine;

namespace Fonda.Tests
{
    /// <summary>S1: the station follows the GDD straight-bar layout (player at the origin facing +z, x to the right).</summary>
    public class BarLayoutTests
    {
        static float ReachOf(Vector3 p) => new Vector2(p.x, p.z).magnitude;

        // "6 ingredientes en fila a z = 0,24 ... a 0,16 entre centros".
        [TestCase("lechuga", -0.40f)]
        [TestCase("tomate", -0.24f)]
        [TestCase("queso", -0.08f)]
        [TestCase("medallon", 0.08f)]
        [TestCase("lomito", 0.24f)]
        [TestCase("papas", 0.40f)]
        public void Ingredient_SitsInTheRow(string id, float x)
        {
            var p = FondaCoreLoopBuilder.DispenserPosition(id);
            Assert.AreEqual(x, p.x, 1e-3f);
            Assert.AreEqual(0.24f, p.z, 1e-3f);
        }

        // Seat order is center, left, right (A4); one tray per seat, the side trays pulled toward the centre.
        [TestCase(0, 0f, 0f)]
        [TestCase(1, -0.34f, -0.60f)]
        [TestCase(2, 0.34f, 0.60f)]
        public void TrayAndSeat_AreInTheirRows(int seat, float trayX, float seatX)
        {
            var tray = FondaCoreLoopBuilder.TrayPosition(seat);
            Assert.AreEqual(trayX, tray.x, 1e-3f);
            Assert.AreEqual(0.40f, tray.z, 1e-3f);

            var stool = FondaCoreLoopBuilder.SeatPosition(seat);
            Assert.AreEqual(seatX, stool.x, 1e-3f);
            Assert.AreEqual(0.95f, stool.z, 1e-3f);
        }

        // "Frasco de propinas en el extremo derecho de la barra, en (0,53; 0,24)".
        [Test]
        public void TipJar_IsAtTheRightEndOfTheBar()
        {
            var jar = FondaCoreLoopBuilder.TipJarPosition();
            Assert.AreEqual(0.53f, jar.x, 1e-3f);
            Assert.AreEqual(0.24f, jar.z, 1e-3f);
            Assert.Less(ReachOf(jar), 0.6f);
        }

        // Tips A2: coins on the customers' side of each tray, without covering it.
        [TestCase(0, 0f, 0.56f)]
        [TestCase(1, -0.22f, 0.53f)]
        [TestCase(2, 0.22f, 0.53f)]
        public void CoinSpot_IsNextToItsTray(int seat, float x, float z)
        {
            var spot = FondaCoreLoopBuilder.CoinSpotPosition(seat);
            Assert.AreEqual(x, spot.x, 1e-3f);
            Assert.AreEqual(z, spot.z, 1e-3f);
            Assert.Greater(spot.z, FondaCoreLoopBuilder.TrayPosition(seat).z, "coins sit past the tray, toward the customer");
        }

        // Existing check kept: everything interactable within reach, on the bar, away from the other sockets.
        [Test]
        public void Layout_PassesTheBuilderValidation()
        {
            CollectionAssert.IsEmpty(FondaCoreLoopBuilder.ValidateLayout());
        }
    }
}
