using System;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// The Fonda clock (S1): runs the shift, exposes <c>t</c>, the current scene and the difficulty settings, and
    /// turns the hands of the wall clock from the start hour to the end hour of the shift.
    /// </summary>
    public class FondaClock : MonoBehaviour
    {
        [SerializeField] DifficultySettings settings;
        [Tooltip("Pivots of the wall-clock hands; they turn around their local z axis, 12 o'clock along local +y.")]
        [SerializeField] Transform hourHand;
        [SerializeField] Transform minuteHand;

        ShiftClock clock;
        ShiftPhase lastPhase;

        /// <summary>The new scene, raised when the shift moves from one scene to the next (including to Closed).</summary>
        public event Action<ShiftPhase> PhaseChanged;
        /// <summary>Raised once, when the time of the shift runs out.</summary>
        public event Action Ended;

        public DifficultySettings Settings => settings;
        public float T => clock != null ? clock.T : 0f;
        public float Elapsed => clock != null ? clock.Elapsed : 0f;
        public ShiftPhase Phase => clock != null ? clock.Phase : ShiftPhase.Opening;
        public bool HasEnded => clock != null && clock.Ended;

        void Awake()
        {
            if (settings == null)
            {
                Debug.LogError("[FondaClock] Missing DifficultySettings.", this);
                enabled = false;
                return;
            }
            clock = new ShiftClock(settings);
            lastPhase = clock.Phase;
            UpdateHands();
        }

        void Start() => Debug.Log("[FondaClock] Empieza el turno: " + PhaseName(Phase) + " (" + settings.ShiftSeconds + " s)");

        void Update()
        {
            bool endedNow = clock.Advance(Time.deltaTime);
            if (clock.Phase != lastPhase)
            {
                lastPhase = clock.Phase;
                Debug.Log("[FondaClock] " + PhaseName(lastPhase) + " (t = " + clock.T.ToString("0.00") + ")");
                PhaseChanged?.Invoke(lastPhase);
            }
            UpdateHands();
            if (endedNow) Ended?.Invoke();
        }

        void UpdateHands()
        {
            var (hour, minute) = ClockHands.Angles(settings.HourAt(T));
            // The face looks at the player (towards −z), so a negative z rotation turns the hands clockwise for them.
            if (hourHand != null) hourHand.localRotation = Quaternion.Euler(0f, 0f, -hour);
            if (minuteHand != null) minuteHand.localRotation = Quaternion.Euler(0f, 0f, -minute);
        }

        static string PhaseName(ShiftPhase phase)
        {
            switch (phase)
            {
                case ShiftPhase.Opening: return "Escena 1: apertura";
                case ShiftPhase.Normal: return "Escena 2: turno normal";
                case ShiftPhase.Rush: return "Escena 3: hora pico";
                default: return "Se terminó el tiempo; no llegan más clientes";
            }
        }
    }
}
