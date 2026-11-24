using System;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// One Blender character (Assets/Art/Characters/Customer_&lt;Type&gt;.fbx) a customer can wear.
    /// See Tools/Blender/README.md for the hierarchy and the seated pose.
    /// </summary>
    [Serializable]
    public class CustomerLook
    {
        // Blender figure units (README "Seated pose"): hips at 0.17, thighs resting 0.10 above the root when seated.
        const float HipsUnits = 0.17f;
        const float SeatedRootUnits = 0.10f;
        // Rotating a leg -90 degrees on X sends the feet from -Y to +Z, the customer's front.
        static readonly Quaternion BentLeg = Quaternion.Euler(-90f, 0f, 0f);

        public GameObject Root;
        public Transform Hips;
        public Transform Head;
        public Transform LegL;
        public Transform LegR;
        public GameObject Happy;
        public GameObject Neutral;
        public GameObject Angry;
        /// <summary>Standing height to the top of the figure, in the customer's local units.</summary>
        public float Height = 1.4f;

        /// <summary>Enables the face of this mood and hides the other two.</summary>
        public void ShowMood(CustomerMood mood)
        {
            if (Happy != null) Happy.SetActive(mood == CustomerMood.Happy);
            if (Neutral != null) Neutral.SetActive(mood == CustomerMood.Neutral);
            if (Angry != null) Angry.SetActive(mood == CustomerMood.Angry);
        }

        /// <summary>How high the figure's root goes so its thighs rest on a seat of this height.</summary>
        public float SeatedLift(float seatHeight)
        {
            // Hips height in the body's space: the look's root may be scaled down from the Blender size.
            float scale = Root != null ? Root.transform.localScale.y : 1f;
            float hips = Hips != null ? Hips.localPosition.y * scale : 0f;
            return seatHeight - hips * (SeatedRootUnits / HipsUnits);
        }

        /// <summary>Bends both legs forward to sit, or straightens them to stand.</summary>
        public void Sit(bool seated)
        {
            var rotation = seated ? BentLeg : Quaternion.identity;
            if (LegL != null) LegL.localRotation = rotation;
            if (LegR != null) LegR.localRotation = rotation;
        }
    }
}
