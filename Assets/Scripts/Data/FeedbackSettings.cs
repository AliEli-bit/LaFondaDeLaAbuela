using System;
using UnityEngine;

namespace Fonda
{
    /// <summary>The moments the game plays a sound for (feedback S1, S4).</summary>
    public enum FeedbackCue
    {
        /// <summary>An ingredient lands in a tray slot (A1).</summary>
        Plop,
        /// <summary>The customer takes a correct dish; higher with the combo (S3).</summary>
        Bell,
        /// <summary>An ingredient the customer did not order (stands in for the grunt, A2).</summary>
        Wrong,
        /// <summary>The customer leaves coins by the tray.</summary>
        CoinsLeft,
        /// <summary>A coin goes into the tip jar.</summary>
        CoinBanked,
    }

    /// <summary>The clips and volume of one cue. One clip is picked at random, never the same twice in a row (A5).</summary>
    [Serializable]
    public class FeedbackSound
    {
        [SerializeField] AudioClip[] clips = new AudioClip[0];
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;

        public AudioClip[] Clips => clips;
        public float Volume => volume;
    }

    /// <summary>
    /// Sounds of the Fonda and how the bell rises with the combo (feedback S1, S3-S5). The scene builder creates the asset
    /// only if it is missing, so manual tuning is kept (A6).
    /// </summary>
    [CreateAssetMenu(fileName = "FeedbackSettings", menuName = "Fonda/Feedback Settings")]
    public class FeedbackSettings : ScriptableObject
    {
        [SerializeField] FeedbackSound plop = new FeedbackSound();
        [SerializeField] FeedbackSound bell = new FeedbackSound();
        [SerializeField] FeedbackSound wrong = new FeedbackSound();
        [SerializeField] FeedbackSound coinsLeft = new FeedbackSound();
        [SerializeField] FeedbackSound coinBanked = new FeedbackSound();

        [Header("Combo (S3)")]
        [Tooltip("How much the bell's pitch rises per delivery of the combo after the first.")]
        [SerializeField, Min(0f)] float bellPitchStep = 0.08f;
        [Tooltip("The bell never goes higher than this pitch.")]
        [SerializeField, Min(1f)] float bellMaxPitch = 1.5f;

        [Header("3D sound (A5)")]
        [Tooltip("Distance with full volume, in meters.")]
        [SerializeField, Min(0.01f)] float minDistance = 0.5f;
        [Tooltip("Distance where the sound stops fading, in meters.")]
        [SerializeField, Min(0.1f)] float maxDistance = 8f;

        public float BellPitchStep => bellPitchStep;
        public float BellMaxPitch => bellMaxPitch;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;

        public FeedbackSound For(FeedbackCue cue)
        {
            switch (cue)
            {
                case FeedbackCue.Plop: return plop;
                case FeedbackCue.Bell: return bell;
                case FeedbackCue.Wrong: return wrong;
                case FeedbackCue.CoinsLeft: return coinsLeft;
                default: return coinBanked;
            }
        }
    }
}
