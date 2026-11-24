using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Fonda
{
    /// <summary>
    /// Sends customers through the door to the first free seat (S5). The Fonda clock sets the pace (shift S2): the
    /// interval, the patience and the ingredients per order follow <c>t</c>, the limit of customers at once follows the
    /// scene, and no one comes in once the time is up.
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        [SerializeField] Customer customerPrefab;
        [Tooltip("Seats in the order they are offered: center first, then left, then right.")]
        [SerializeField] BarSeat[] seats;
        [SerializeField] RecipeData[] menu;
        [SerializeField] Transform door;
        [SerializeField] FondaClock clock;
        [Tooltip("Types of customer (customer types S1). Empty: plain customers with a random look.")]
        [SerializeField] CustomerProfile[] profiles = Array.Empty<CustomerProfile>();

        readonly List<Customer> active = new List<Customer>();
        readonly List<bool> occupied = new List<bool>();
        readonly List<int> ingredientCounts = new List<int>();
        float untilNext;

        /// <summary>Raised when a customer walks out the door, served or angry.</summary>
        public event Action<Customer> CustomerLeft;
        public event Action<Customer> CustomerServed;
        public event Action<Customer> CustomerRefused;
        public event Action<Customer> CustomerLeftAngry;

        public IReadOnlyList<Customer> Active => active;

        void Start()
        {
            if (customerPrefab == null || door == null || seats == null || seats.Length == 0 || menu == null ||
                menu.Length == 0 || clock == null || clock.Settings == null)
            {
                Debug.LogError("[CustomerSpawner] Missing prefab, door, seats, menu or clock.", this);
                enabled = false;
                return;
            }
            untilNext = clock.Settings.FirstArrivalSeconds;
            foreach (var recipe in menu) ingredientCounts.Add(recipe != null ? recipe.Ingredients.Count : 0);
        }

        void Update()
        {
            if (clock.HasEnded) return; // time is up: no more arrivals
            untilNext -= Time.deltaTime;
            if (untilNext > 0f || active.Count >= clock.Settings.MaxCustomers(clock.Phase)) return;

            occupied.Clear();
            foreach (var seat in seats) occupied.Add(seat == null || !seat.IsFree);
            int index = SeatRule.FirstFree(occupied);
            if (index < 0) return; // every seat is taken: wait until one is freed

            float t = clock.T;
            Spawn(seats[index], t);
            untilNext = clock.Settings.IntervalAt(t);
        }

        void Spawn(BarSeat seat, float t)
        {
            var settings = clock.Settings;
            int target = settings.IngredientsAt(t);
            var profile = PickProfile(target);
            var candidates = profile != null
                ? CustomerTypeRule.DishCandidates(profile, menu, target)
                : MenuRule.Candidates(ingredientCounts, target);
            var recipe = menu[candidates[Random.Range(0, candidates.Count)]];
            var customer = Instantiate(customerPrefab, door.position, door.rotation);
            seat.Claim(customer);
            customer.Left += OnCustomerLeft;
            customer.Served += c => CustomerServed?.Invoke(c);
            customer.Refused += c => CustomerRefused?.Invoke(c);
            customer.LeftAngry += c => CustomerLeftAngry?.Invoke(c);
            float patience = settings.PatienceAt(t), eat = settings.EatSeconds;
            if (profile != null)
            {
                patience = profile.PatienceFor(patience);
                eat = profile.EatFor(eat);
            }
            customer.Init(recipe, seat, door.position, patience, eat, profile);
            active.Add(customer);
        }

        /// <summary>A type that can order a dish of this size, by weight (customer types A4, A5). Null without types.</summary>
        CustomerProfile PickProfile(int target)
        {
            if (profiles == null || profiles.Length == 0) return null;
            var eligible = CustomerTypeRule.Eligible(profiles, menu, target);
            int index = CustomerTypeRule.Pick(profiles, eligible, Random.value);
            if (index < 0) return null;
            var profile = profiles[index];
            // A type whose favourites are not on this menu falls back to a plain customer.
            return CustomerTypeRule.Dishes(profile, menu).Count > 0 ? profile : null;
        }

        void OnCustomerLeft(Customer customer)
        {
            active.Remove(customer);
            CustomerLeft?.Invoke(customer);
        }
    }
}
