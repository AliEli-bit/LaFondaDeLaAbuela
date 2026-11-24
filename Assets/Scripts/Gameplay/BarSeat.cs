using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Fonda
{
    /// <summary>
    /// One seat at the bar: the customer sitting there and the tray in front of it (S2).
    /// The tray takes the customer's order when they sit and is closed again when they leave.
    /// </summary>
    public class BarSeat : MonoBehaviour
    {
        [SerializeField] Tray tray;
        [Tooltip("Where the customer stands to sit down; its forward axis faces the player.")]
        [SerializeField] Transform seatPoint;

        [Header("Tip (tips S1)")]
        [Tooltip("Where this seat's customer leaves coins, next to the tray without covering it.")]
        [SerializeField] Transform coinSpot;
        [SerializeField] TipCoin coinPrefab;
        [SerializeField] int minCoins = 1;
        [SerializeField] int maxCoins = 3;

        // Coins form a row along the spot's local x axis (the builder points it away from the tray).
        static readonly Vector3[] CoinOffsets =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.045f, 0f, 0f), new Vector3(-0.045f, 0f, 0f),
            new Vector3(0.09f, 0f, 0f), new Vector3(-0.09f, 0f, 0f),
        };

        public UnityEvent onFreed;
        public event Action<BarSeat> Freed;
        /// <summary>A customer left this many coins (always 1 or more) by the tray (feedback S4).</summary>
        public event Action<BarSeat, int> TipLeft;

        /// <summary>Where the coins of this seat appear.</summary>
        public Vector3 CoinSpotPosition => coinSpot != null ? coinSpot.position : transform.position;

        public Customer Occupant { get; private set; }
        public bool IsFree => Occupant == null;
        public Tray Tray => tray;
        public Transform SeatPoint => seatPoint != null ? seatPoint : transform;

        void OnEnable()
        {
            if (tray == null) return;
            tray.OrderCorrect += OnOrderCorrect;
            tray.OrderWrong += OnOrderWrong;
        }

        void OnDisable()
        {
            if (tray == null) return;
            tray.OrderCorrect -= OnOrderCorrect;
            tray.OrderWrong -= OnOrderWrong;
        }

        /// <summary>
        /// Drops the tip of a customer who is leaving: coins by the patience left when served, none if they left angry.
        /// Coins nobody picks up just stay on the bar (S4). <paramref name="bonusCoins"/> is the combo bonus (combo S2).
        /// </summary>
        public List<TipCoin> LeaveTip(CustomerCycle cycle, int bonusCoins = 0, CustomerProfile profile = null)
        {
            var dropped = new List<TipCoin>();
            if (coinPrefab == null || coinSpot == null) return dropped;
            int count = profile != null
                ? TipRule.CoinsFor(cycle, profile.MinCoins, profile.MaxCoins) // the type's tip (customer types A1)
                : TipRule.CoinsFor(cycle, minCoins, maxCoins);
            if (count > 0) count += Mathf.Max(0, bonusCoins); // an angry customer leaves nothing, combo or not
            for (int i = 0; i < count; i++)
            {
                var offset = coinSpot.rotation * CoinOffsets[i % CoinOffsets.Length] + Vector3.up * 0.01f * (i / CoinOffsets.Length);
                dropped.Add(Instantiate(coinPrefab, coinSpot.position + offset, coinSpot.rotation));
            }
            if (dropped.Count > 0) TipLeft?.Invoke(this, dropped.Count);
            return dropped;
        }

        /// <summary>Reserves the seat for a customer who is walking to it.</summary>
        public bool Claim(Customer customer)
        {
            if (!IsFree || customer == null) return false;
            Occupant = customer;
            return true;
        }

        /// <summary>The customer sat down and orders: the tray opens with that order.</summary>
        public void OpenOrder(RecipeData order)
        {
            if (tray != null) tray.SetOrder(order);
        }

        /// <summary>The customer stood up: close the tray and announce that the seat is free.</summary>
        public void Release(Customer customer)
        {
            if (Occupant != customer) return;
            Occupant = null;
            StopAllCoroutines();
            if (tray != null) tray.SetOrder(null);
            Freed?.Invoke(this);
            onFreed?.Invoke();
        }

        void OnOrderCorrect(RecipeData _)
        {
            if (Occupant == null || !Occupant.Serve()) return;
            // The customer takes the plate: close the tray once its own delivery has emptied it,
            // never inside the XRI select callback that raised this event.
            StartCoroutine(CloseAfterDelivery());
        }

        IEnumerator CloseAfterDelivery()
        {
            yield return null;
            while (tray != null && tray.IsDelivering) yield return null;
            if (tray != null) tray.SetOrder(null);
        }

        void OnOrderWrong(RecipeData _)
        {
            if (Occupant != null) Occupant.ShakeHead();
        }
    }
}
