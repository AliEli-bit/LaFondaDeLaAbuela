using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Fonda
{
    /// <summary>
    /// The tip jar (S2): a coin released inside its trigger is counted once and disappears into a pile that grows (A3).
    /// A coin still held by the hand is not counted until it is let go.
    /// </summary>
    public class TipJar : MonoBehaviour
    {
        [SerializeField] TextMeshPro label;
        [Tooltip("Visual stack of coins inside the jar; grows with each coin.")]
        [SerializeField] Transform pile;
        [SerializeField] float pileStepPerCoin = 0.004f;
        [SerializeField] float pileMaxHeight = 0.10f;

        public UnityEvent onTipBanked;
        /// <summary>(value of the coin, new total)</summary>
        public event Action<int, int> TipBanked;

        public int Total { get; private set; }
        int coins;

        void Start() => Refresh();

        void OnTriggerStay(Collider other)
        {
            var coin = other.GetComponentInParent<TipCoin>();
            if (coin != null) Deposit(coin);
        }

        /// <summary>Counts a coin that is inside the jar. False when it is held, already counted or missing.</summary>
        public bool Deposit(TipCoin coin)
        {
            if (coin == null || coin.Banked || coin.IsHeld || !coin.MarkBanked()) return false;
            Total += coin.Value;
            coins++;
            Refresh();
            Debug.Log("[TipJar] Propina cobrada: +" + coin.Value + " (total " + Total + ")");
            TipBanked?.Invoke(coin.Value, Total);
            onTipBanked?.Invoke();
            if (Application.isPlaying) Destroy(coin.gameObject);
            else DestroyImmediate(coin.gameObject);
            return true;
        }

        void Refresh()
        {
            if (label != null) label.text = "Propinas: $" + Total;
            if (pile != null)
            {
                float h = Mathf.Min(coins * pileStepPerCoin, pileMaxHeight);
                pile.gameObject.SetActive(coins > 0);
                var s = pile.localScale;
                pile.localScale = new Vector3(s.x, h / 2f, s.z); // cylinder primitive is 2 units tall
                var p = pile.localPosition;
                pile.localPosition = new Vector3(p.x, h / 2f, p.z);
            }
        }
    }
}
