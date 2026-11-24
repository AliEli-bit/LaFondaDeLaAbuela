using System.Collections.Generic;

namespace Fonda
{
    public enum CustomerMood { Happy, Neutral, Angry }

    /// <summary>Arriving: walking to the seat. Waiting: seated with the order shown. Eating: served. Leaving: on the way out.</summary>
    public enum CustomerState { Arriving, Waiting, Eating, Leaving }

    /// <summary>Face thresholds from the GDD: happy above 60 % patience, angry below 25 %, neutral otherwise.</summary>
    public static class PatienceRule
    {
        public const float HappyAbove = 0.60f;
        public const float AngryBelow = 0.25f;

        public static CustomerMood MoodFor(float remaining, float max)
        {
            float fraction = max > 0f ? remaining / max : 0f;
            if (fraction > HappyAbove) return CustomerMood.Happy;
            if (fraction < AngryBelow) return CustomerMood.Angry;
            return CustomerMood.Neutral;
        }
    }

    public static class SeatRule
    {
        /// <summary>Index of the first unoccupied seat in the given order, or -1 when every seat is taken.</summary>
        public static int FirstFree(IReadOnlyList<bool> occupied)
        {
            if (occupied == null) return -1;
            for (int i = 0; i < occupied.Count; i++)
                if (!occupied[i]) return i;
            return -1;
        }
    }

    /// <summary>
    /// Engine-free state of one customer. Patience runs only while waiting at the seat; when it runs out the
    /// customer leaves angry. Served while waiting, the customer eats for a while and then leaves.
    /// </summary>
    public class CustomerCycle
    {
        readonly float eatSeconds;
        float eatLeft;

        public CustomerCycle(float patienceSeconds, float eatSeconds)
        {
            MaxPatience = patienceSeconds;
            Patience = patienceSeconds;
            this.eatSeconds = eatSeconds;
        }

        public CustomerState State { get; private set; } = CustomerState.Arriving;
        public float MaxPatience { get; }
        public float Patience { get; private set; }
        public CustomerMood Mood => PatienceRule.MoodFor(Patience, MaxPatience);
        public bool LeftAngry { get; private set; }

        /// <summary>The customer reached the seat and orders: patience starts running.</summary>
        public void Sit()
        {
            if (State == CustomerState.Arriving) State = CustomerState.Waiting;
        }

        /// <summary>The tray matched the order. Returns false when the customer is not waiting for it.</summary>
        public bool Serve()
        {
            if (State != CustomerState.Waiting) return false;
            State = CustomerState.Eating;
            eatLeft = eatSeconds;
            return true;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            switch (State)
            {
                case CustomerState.Waiting:
                    Patience -= dt;
                    if (Patience <= 0f)
                    {
                        Patience = 0f;
                        LeftAngry = true;
                        State = CustomerState.Leaving;
                    }
                    break;
                case CustomerState.Eating:
                    eatLeft -= dt;
                    if (eatLeft <= 0f) State = CustomerState.Leaving;
                    break;
            }
        }
    }
}
