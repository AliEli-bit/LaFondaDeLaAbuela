using System.Collections.Generic;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// Sends customers through the door to the first free seat (S5). Without the Fonda clock yet, the arrival
    /// interval, the simultaneous limit, patience and eating time are fixed serialized values (GDD values at t = 0).
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        [SerializeField] Customer customerPrefab;
        [Tooltip("Seats in the order they are offered: center first, then left, then right.")]
        [SerializeField] BarSeat[] seats;
        [SerializeField] RecipeData[] menu;
        [SerializeField] Transform door;

        [Header("Balance (GDD values at t = 0)")]
        [SerializeField] float firstArrivalSeconds = 2f;
        [SerializeField] float arrivalIntervalSeconds = 12f;
        [SerializeField] int maxSimultaneous = 1;
        [SerializeField] float patienceSeconds = 25f;
        [SerializeField] float eatSeconds = 3f;

        readonly List<Customer> active = new List<Customer>();
        readonly List<bool> occupied = new List<bool>();
        float untilNext;

        public IReadOnlyList<Customer> Active => active;

        void Start()
        {
            untilNext = firstArrivalSeconds;
            if (customerPrefab == null || door == null || seats == null || seats.Length == 0 || menu == null || menu.Length == 0)
            {
                Debug.LogError("[CustomerSpawner] Missing prefab, door, seats or menu.", this);
                enabled = false;
            }
        }

        void Update()
        {
            untilNext -= Time.deltaTime;
            if (untilNext > 0f || active.Count >= maxSimultaneous) return;

            occupied.Clear();
            foreach (var seat in seats) occupied.Add(seat == null || !seat.IsFree);
            int index = SeatRule.FirstFree(occupied);
            if (index < 0) return; // every seat is taken: wait until one is freed

            Spawn(seats[index]);
            untilNext = arrivalIntervalSeconds;
        }

        void Spawn(BarSeat seat)
        {
            var recipe = menu[Random.Range(0, menu.Length)];
            var customer = Instantiate(customerPrefab, door.position, door.rotation);
            seat.Claim(customer);
            customer.Left += OnCustomerLeft;
            customer.Init(recipe, seat, door.position, patienceSeconds, eatSeconds);
            active.Add(customer);
        }

        void OnCustomerLeft(Customer customer) => active.Remove(customer);
    }
}
