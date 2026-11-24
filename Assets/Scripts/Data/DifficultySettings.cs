using UnityEngine;

namespace Fonda
{
    /// <summary>Opening (scene 1, tutorial pace), Normal (scene 2), Rush (scene 3, peak hour), Closed (time is up).</summary>
    public enum ShiftPhase { Opening, Normal, Rush, Closed }

    /// <summary>
    /// Balance of a shift (S5). <c>t</c> is elapsed time over the whole shift, from 0 to 1. Defaults are the GDD values:
    /// interval 12 − 7·t, patience 25 − 10·t, ingredients 1 + round(2·t), up to 1 / 2 / 3 customers per scene.
    /// </summary>
    [CreateAssetMenu(fileName = "DifficultySettings", menuName = "Fonda/Difficulty Settings")]
    public class DifficultySettings : ScriptableObject
    {
        [Header("Shift length (seconds per scene)")]
        [SerializeField] float openingSeconds = 60f;
        [SerializeField] float normalSeconds = 180f;
        [SerializeField] float rushSeconds = 90f;

        [Header("Arrivals: interval goes from start to end over the shift")]
        [SerializeField] float firstArrivalSeconds = 2f;
        [SerializeField] float intervalStart = 12f;
        [SerializeField] float intervalEnd = 5f;

        [Header("Patience: goes from start to end over the shift")]
        [SerializeField] float patienceStart = 25f;
        [SerializeField] float patienceEnd = 15f;
        [SerializeField] float eatSeconds = 3f;

        [Header("Ingredients per order")]
        [SerializeField] int minIngredients = 1;
        [SerializeField] int maxIngredients = 3;

        [Header("Customers at the bar at once, per scene")]
        [SerializeField] int openingMaxCustomers = 1;
        [SerializeField] int normalMaxCustomers = 2;
        [SerializeField] int rushMaxCustomers = 3;

        [Header("Speed combo")]
        [Tooltip("A delivery is fast when more than this share of patience is left (the happy face).")]
        [SerializeField, Range(0f, 1f)] float comboFastAbove = 0.60f;
        [Tooltip("Extra coins per combo step from the second fast delivery in a row.")]
        [SerializeField] int comboCoinsPerStep = 1;
        [SerializeField] int comboMaxBonusCoins = 2;

        [Header("Wall clock (hours of the day)")]
        [SerializeField] float startHour = 15f;
        [SerializeField] float endHour = 20f;

        public float ShiftSeconds => openingSeconds + normalSeconds + rushSeconds;
        public float FirstArrivalSeconds => firstArrivalSeconds;
        public float EatSeconds => eatSeconds;
        public float ComboFastAbove => comboFastAbove;
        public int ComboCoinsPerStep => comboCoinsPerStep;
        public int ComboMaxBonusCoins => comboMaxBonusCoins;

        public float IntervalAt(float t) => Mathf.Lerp(intervalStart, intervalEnd, Mathf.Clamp01(t));
        public float PatienceAt(float t) => Mathf.Lerp(patienceStart, patienceEnd, Mathf.Clamp01(t));

        /// <summary>min + round((max − min)·t), with half rounding up (A4).</summary>
        public int IngredientsAt(float t) =>
            minIngredients + Mathf.FloorToInt((maxIngredients - minIngredients) * Mathf.Clamp01(t) + 0.5f);

        public ShiftPhase PhaseAt(float elapsedSeconds)
        {
            if (elapsedSeconds < openingSeconds) return ShiftPhase.Opening;
            if (elapsedSeconds < openingSeconds + normalSeconds) return ShiftPhase.Normal;
            if (elapsedSeconds < ShiftSeconds) return ShiftPhase.Rush;
            return ShiftPhase.Closed;
        }

        public int MaxCustomers(ShiftPhase phase)
        {
            switch (phase)
            {
                case ShiftPhase.Opening: return openingMaxCustomers;
                case ShiftPhase.Normal: return normalMaxCustomers;
                case ShiftPhase.Rush: return rushMaxCustomers;
                default: return 0;
            }
        }

        /// <summary>Hour of the day shown on the wall clock (A3).</summary>
        public float HourAt(float t) => Mathf.Lerp(startHour, endHour, Mathf.Clamp01(t));
    }
}
