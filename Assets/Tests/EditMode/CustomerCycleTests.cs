using NUnit.Framework;

namespace Fonda.Tests
{
    public class CustomerCycleTests
    {
        const float Patience = 25f;
        const float Eat = 3f;

        static CustomerCycle Seated()
        {
            var c = new CustomerCycle(Patience, Eat);
            c.Sit();
            return c;
        }

        // S4: "feliz (> 60 % de paciencia), neutral, enojada (< 25 %)".
        [TestCase(0.61f, CustomerMood.Happy)]
        [TestCase(0.60f, CustomerMood.Neutral)]
        [TestCase(0.25f, CustomerMood.Neutral)]
        [TestCase(0.24f, CustomerMood.Angry)]
        public void Mood_FollowsThePatienceThresholds(float fraction, CustomerMood expected)
        {
            Assert.AreEqual(expected, PatienceRule.MoodFor(fraction * Patience, Patience));
        }

        // A6: patience does not run while the customer is still walking to the seat.
        [Test]
        public void Patience_DoesNotRunBeforeSitting()
        {
            var c = new CustomerCycle(Patience, Eat);
            c.Tick(10f);
            Assert.AreEqual(CustomerState.Arriving, c.State);
            Assert.AreEqual(Patience, c.Patience);
        }

        // S3.2 + S4: once seated, patience runs; at 0 the customer leaves angry (L2).
        [Test]
        public void SeatedCustomer_LeavesAngry_WhenPatienceRunsOut()
        {
            var c = Seated();
            c.Tick(20f);
            Assert.AreEqual(CustomerState.Waiting, c.State);
            Assert.AreEqual(CustomerMood.Angry, c.Mood);

            c.Tick(5f);
            Assert.AreEqual(CustomerState.Leaving, c.State);
            Assert.IsTrue(c.LeftAngry);
        }

        // S3.3 + S3.4: served while waiting -> eats for 3 s (patience frozen) -> leaves happy.
        [Test]
        public void ServedCustomer_EatsThenLeaves_NotAngry()
        {
            var c = Seated();
            c.Tick(5f);
            Assert.IsTrue(c.Serve());
            Assert.AreEqual(CustomerState.Eating, c.State);

            c.Tick(2.9f);
            Assert.AreEqual(CustomerState.Eating, c.State);
            Assert.AreEqual(Patience - 5f, c.Patience, 1e-4f);

            c.Tick(0.2f);
            Assert.AreEqual(CustomerState.Leaving, c.State);
            Assert.IsFalse(c.LeftAngry);
        }

        // Serving only counts while waiting at the seat.
        [Test]
        public void Serve_IsIgnored_UnlessWaiting()
        {
            var c = new CustomerCycle(Patience, Eat);
            Assert.IsFalse(c.Serve(), "arriving");
            c.Sit();
            c.Tick(Patience);
            Assert.IsFalse(c.Serve(), "already leaving");
            Assert.AreEqual(CustomerState.Leaving, c.State);
        }

        // S3.1: "se sienta solo en el primer asiento libre"; -1 when every seat is taken (A5).
        [Test]
        public void FirstFreeSeat_IsTheFirstUnoccupied_OrNoneWhenFull()
        {
            Assert.AreEqual(1, SeatRule.FirstFree(new[] { true, false, false }));
            Assert.AreEqual(-1, SeatRule.FirstFree(new[] { true, true, true }));
        }
    }
}
