using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Fonda
{
    /// <summary>
    /// Flow of the shift (S3): counts the customers who leave, keeps the speed combo (combo S1-S2), and once the time is
    /// up and the last customer is gone it adds the coins left on the bar to the jar (S4) and shows the summary (A2, A5).
    /// </summary>
    public class ShiftManager : MonoBehaviour
    {
        [SerializeField] FondaClock clock;
        [SerializeField] CustomerSpawner spawner;
        [SerializeField] TipJar tipJar;
        [SerializeField] TextMeshPro summaryLabel;

        public UnityEvent onShiftClosed;

        /// <summary>A customer took a correct dish; the int is the combo after this delivery, 0 when it was slow (feedback S3).</summary>
        public event System.Action<Customer, int> Delivered;

        readonly ShiftSummary summary = new ShiftSummary();
        readonly ComboCounter combo = new ComboCounter();

        public ShiftSummary Summary => summary;
        public ComboCounter Combo => combo;
        public bool Closed { get; private set; }

        void Awake()
        {
            if (summaryLabel != null) summaryLabel.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (clock != null) clock.Ended += TryClose;
            if (spawner == null) return;
            spawner.CustomerLeft += OnCustomerLeft;
            spawner.CustomerServed += OnCustomerServed;
            spawner.CustomerRefused += OnComboBroken;
            spawner.CustomerLeftAngry += OnComboBroken;
        }

        void OnDisable()
        {
            if (clock != null) clock.Ended -= TryClose;
            if (spawner == null) return;
            spawner.CustomerLeft -= OnCustomerLeft;
            spawner.CustomerServed -= OnCustomerServed;
            spawner.CustomerRefused -= OnComboBroken;
            spawner.CustomerLeftAngry -= OnComboBroken;
        }

        void OnCustomerServed(Customer customer)
        {
            var settings = clock != null ? clock.Settings : null;
            if (settings == null || customer.Cycle == null)
            {
                Delivered?.Invoke(customer, 0);
                return;
            }
            bool fast = ComboRule.IsFast(customer.Cycle.Patience, customer.Cycle.MaxPatience, settings.ComboFastAbove);
            int count = combo.Delivered(fast);
            int bonus = ComboRule.TipBonus(count, settings.ComboCoinsPerStep, settings.ComboMaxBonusCoins);
            customer.ApplyCombo(count, bonus);
            Debug.Log("[ShiftManager] Entrega " + (fast ? "rápida" : "lenta") + ": combo ×" + count +
                      (bonus > 0 ? " (+" + bonus + " moneda(s))" : ""));
            Delivered?.Invoke(customer, count);
        }

        void OnComboBroken(Customer _)
        {
            if (combo.Count > 0) Debug.Log("[ShiftManager] Se corta el combo ×" + combo.Count);
            combo.Break();
        }

        void OnCustomerLeft(Customer customer)
        {
            summary.RecordCustomer(customer.Cycle != null && customer.Cycle.LeftAngry);
            TryClose();
        }

        void TryClose()
        {
            if (Closed || clock == null || !clock.HasEnded) return;
            if (spawner != null && spawner.Active.Count > 0) return; // seated customers still finish (A2)
            Close();
        }

        void Close()
        {
            Closed = true;
            int banked = tipJar != null ? tipJar.Total : 0;
            int leftover = tipJar != null
                ? tipJar.BankLeftovers(FindObjectsByType<TipCoin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                : 0;
            summary.TipsBanked = banked;
            summary.TipsLeftover = leftover;
            summary.BestCombo = combo.Best;

            var text = summary.Text();
            if (summaryLabel != null)
            {
                summaryLabel.text = text;
                summaryLabel.gameObject.SetActive(true);
            }
            Debug.Log("[ShiftManager] " + text.Replace("\n", " | "));
            onShiftClosed?.Invoke();
        }
    }
}
