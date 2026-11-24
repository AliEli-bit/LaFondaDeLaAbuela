using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace Fonda
{
    /// <summary>
    /// Flow of the shift (S3): counts the customers who leave, and once the time is up and the last customer is gone it
    /// adds the coins left on the bar to the jar (S4) and shows the summary (A2, A5).
    /// </summary>
    public class ShiftManager : MonoBehaviour
    {
        [SerializeField] FondaClock clock;
        [SerializeField] CustomerSpawner spawner;
        [SerializeField] TipJar tipJar;
        [SerializeField] TextMeshPro summaryLabel;

        public UnityEvent onShiftClosed;

        readonly ShiftSummary summary = new ShiftSummary();

        public ShiftSummary Summary => summary;
        public bool Closed { get; private set; }

        void Awake()
        {
            if (summaryLabel != null) summaryLabel.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (clock != null) clock.Ended += TryClose;
            if (spawner != null) spawner.CustomerLeft += OnCustomerLeft;
        }

        void OnDisable()
        {
            if (clock != null) clock.Ended -= TryClose;
            if (spawner != null) spawner.CustomerLeft -= OnCustomerLeft;
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
