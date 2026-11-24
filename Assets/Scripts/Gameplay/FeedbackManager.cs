using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// Plays the Fonda's sounds (feedback S1, S3, S4): plop when an ingredient lands in a tray, error for an ingredient
    /// nobody ordered, a bell that rises with the combo when the customer takes the dish, coins when a tip is left and
    /// when a coin goes into the jar. Sounds are 3D, from where they happen (A5), on a small pool of audio sources.
    /// </summary>
    public class FeedbackManager : MonoBehaviour
    {
        [SerializeField] FeedbackSettings settings;
        [SerializeField] Tray[] trays = new Tray[0];
        [SerializeField] BarSeat[] seats = new BarSeat[0];
        [SerializeField] TipJar tipJar;
        [SerializeField] ShiftManager shiftManager;
        [Tooltip("How many sounds can play at once.")]
        [SerializeField, Min(1)] int voices = 6;

        /// <summary>A cue was played on this audio source.</summary>
        public event Action<FeedbackCue, AudioSource> Played;

        public FeedbackSettings Settings => settings;

        readonly List<AudioSource> pool = new List<AudioSource>();
        readonly Dictionary<FeedbackCue, int> lastVariant = new Dictionary<FeedbackCue, int>();
        readonly Dictionary<Tray, Action<RecipeData>> wrongHandlers = new Dictionary<Tray, Action<RecipeData>>();
        int nextSteal;
        bool subscribed;

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        /// <summary>Uses these settings and listens to these trays, seats, jar and shift (replaces the previous ones).</summary>
        public void Bind(FeedbackSettings feedbackSettings, Tray[] barTrays, BarSeat[] barSeats, TipJar jar,
            ShiftManager shift)
        {
            Unsubscribe();
            settings = feedbackSettings;
            trays = barTrays ?? new Tray[0];
            seats = barSeats ?? new BarSeat[0];
            tipJar = jar;
            shiftManager = shift;
            Subscribe();
        }

        /// <summary>Plays one clip of <paramref name="cue"/> at <paramref name="position"/>. Null when the cue has no clip.</summary>
        public AudioSource Play(FeedbackCue cue, Vector3 position, float pitch = 1f)
        {
            var sound = settings != null ? settings.For(cue) : null;
            var clips = sound != null ? sound.Clips : null;
            if (clips == null || clips.Length == 0) return null;

            int index = FeedbackRule.PickVariant(clips.Length, lastVariant.TryGetValue(cue, out var last) ? last : -1,
                UnityEngine.Random.value);
            var clip = clips[index];
            if (clip == null) return null;
            lastVariant[cue] = index;

            var source = Voice();
            source.transform.position = position;
            source.clip = clip;
            source.volume = sound.Volume;
            source.pitch = pitch;
            source.spatialBlend = 1f;
            source.minDistance = settings.MinDistance;
            source.maxDistance = settings.MaxDistance;
            source.Play();
            Played?.Invoke(cue, source);
            return source;
        }

        // A free voice, a new one while under the limit, or the oldest one when all are busy.
        AudioSource Voice()
        {
            pool.RemoveAll(s => s == null);
            foreach (var s in pool)
                if (!s.isPlaying) return s;
            if (pool.Count < Mathf.Max(1, voices))
            {
                var go = new GameObject("Voice " + pool.Count);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                pool.Add(source);
                return source;
            }
            return pool[nextSteal++ % pool.Count];
        }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            foreach (var tray in trays)
            {
                if (tray == null || wrongHandlers.ContainsKey(tray)) continue;
                var current = tray;
                Action<RecipeData> onWrong = _ => Play(FeedbackCue.Wrong, current.transform.position);
                wrongHandlers[tray] = onWrong;
                tray.IngredientPlaced += OnIngredientPlaced;
                tray.OrderWrong += onWrong;
            }
            foreach (var seat in seats)
                if (seat != null) seat.TipLeft += OnTipLeft;
            if (tipJar != null) tipJar.TipBanked += OnTipBanked;
            if (shiftManager != null) shiftManager.Delivered += OnDelivered;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            foreach (var pair in wrongHandlers)
            {
                if (pair.Key == null) continue;
                pair.Key.IngredientPlaced -= OnIngredientPlaced;
                pair.Key.OrderWrong -= pair.Value;
            }
            wrongHandlers.Clear();
            foreach (var seat in seats)
                if (seat != null) seat.TipLeft -= OnTipLeft;
            if (tipJar != null) tipJar.TipBanked -= OnTipBanked;
            if (shiftManager != null) shiftManager.Delivered -= OnDelivered;
        }

        void OnIngredientPlaced(Tray tray) => Play(FeedbackCue.Plop, tray.transform.position);

        void OnTipLeft(BarSeat seat, int coins) => Play(FeedbackCue.CoinsLeft, seat.CoinSpotPosition);

        void OnTipBanked(int value, int total) => Play(FeedbackCue.CoinBanked, tipJar.transform.position);

        void OnDelivered(Customer customer, int combo)
        {
            if (customer == null) return;
            var seat = customer.Seat;
            var where = seat != null && seat.Tray != null ? seat.Tray.transform.position : customer.transform.position;
            float pitch = settings != null ? FeedbackRule.BellPitch(combo, settings.BellPitchStep, settings.BellMaxPitch) : 1f;
            Play(FeedbackCue.Bell, where, pitch);
        }
    }
}
