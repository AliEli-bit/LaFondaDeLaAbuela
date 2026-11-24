using NUnit.Framework;
using UnityEngine;

namespace Fonda.Tests
{
    /// <summary>The Blender character a customer wears: one face per mood and the seated pose on the stool.</summary>
    public class CustomerLookTests
    {
        GameObject root;
        CustomerLook look;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Customer_Test");
            var hips = new GameObject("Hips").transform;
            hips.SetParent(root.transform, false);
            hips.localPosition = new Vector3(0f, 0.17f * 1.55f, 0f);
            look = new CustomerLook
            {
                Root = root,
                Hips = hips,
                LegL = Child(hips, "Leg_L"),
                LegR = Child(hips, "Leg_R"),
                Happy = Child(root.transform, "Face_Happy").gameObject,
                Neutral = Child(root.transform, "Face_Neutral").gameObject,
                Angry = Child(root.transform, "Face_Angry").gameObject,
            };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        static Transform Child(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        [TestCase(CustomerMood.Happy)]
        [TestCase(CustomerMood.Neutral)]
        [TestCase(CustomerMood.Angry)]
        public void ShowMood_EnablesOnlyThatFace(CustomerMood mood)
        {
            look.ShowMood(mood);

            Assert.AreEqual(mood == CustomerMood.Happy, look.Happy.activeSelf);
            Assert.AreEqual(mood == CustomerMood.Neutral, look.Neutral.activeSelf);
            Assert.AreEqual(mood == CustomerMood.Angry, look.Angry.activeSelf);
        }

        // README of Tools/Blender: root at seat_height - 0.10 * size * 1.55, hips at 0.17 * size * 1.55.
        [Test]
        public void SeatedLift_PutsTheThighsOnTheCushion()
        {
            Assert.AreEqual(0.50f - 0.10f * 1.55f, look.SeatedLift(0.50f), 1e-4f);
        }

        // The customers are shown at 75 % of the Blender size; the thighs must still land on the cushion.
        [Test]
        public void SeatedLift_FollowsTheLookScale()
        {
            root.transform.localScale = Vector3.one * 0.75f;
            Assert.AreEqual(0.62f - 0.10f * 1.55f * 0.75f, look.SeatedLift(0.62f), 1e-4f);
        }

        [Test]
        public void Sit_BendsBothLegsForward_AndStandStraightensThem()
        {
            look.Sit(true);
            var feet = look.LegL.rotation * Vector3.down;
            Assert.AreEqual(1f, feet.z, 1e-4f, "feet point to the customer's front (+z)");
            Assert.AreEqual(look.LegL.localRotation, look.LegR.localRotation);

            look.Sit(false);
            Assert.AreEqual(Quaternion.identity, look.LegL.localRotation);
            Assert.AreEqual(Quaternion.identity, look.LegR.localRotation);
        }
    }
}
