using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda
{
    /// <summary>A grabbable coin left by a customer (S3). It only counts once it is released inside the tip jar.</summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class TipCoin : MonoBehaviour
    {
        [SerializeField] int value = 1;

        XRGrabInteractable grab;

        public int Value => value;
        public bool Banked { get; private set; }

        /// <summary>True while a hand holds the coin.</summary>
        public bool IsHeld
        {
            get
            {
                if (grab == null) grab = GetComponent<XRGrabInteractable>();
                return grab != null && grab.isSelected;
            }
        }

        /// <summary>Marks the coin as counted; returns false if it was already counted.</summary>
        public bool MarkBanked()
        {
            if (Banked) return false;
            Banked = true;
            return true;
        }
    }
}
