using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fonda.Tests
{
    /// <summary>A thrown ingredient bumps into the customer instead of passing through (user request 2026-11-24).</summary>
    public class CustomerColliderTests
    {
        GameObject root;
        RecipeData order;
        // A preview scene has its own physics, so the test steps it by hand without touching the project's settings.
        Scene scene;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("TestRoot");
            SceneManager.MoveGameObjectToScene(root, scene);
            order = ScriptableObject.CreateInstance<RecipeData>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(order);
            EditorSceneManager.ClosePreviewScene(scene);
        }

        Customer SpawnCustomer(Vector3 at, CustomerProfile profile = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Customer.prefab");
            Assert.IsNotNull(prefab, "customer prefab");
            var customer = Object.Instantiate(prefab, root.transform).GetComponent<Customer>();
            var seat = new GameObject("Seat").AddComponent<BarSeat>();
            seat.transform.SetParent(root.transform);
            customer.Init(order, seat, at, 20f, 3f, profile);
            Physics.SyncTransforms();
            return customer;
        }

        Rigidbody Throw(Vector3 from, Vector3 velocity)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ingredients/Medallon.prefab");
            Assert.IsNotNull(prefab, "ingredient prefab");
            var body = Object.Instantiate(prefab, from, Quaternion.identity, root.transform).GetComponent<Rigidbody>();
            body.linearVelocity = velocity;
            return body;
        }

        void Simulate(float seconds)
        {
            var physics = scene.GetPhysicsScene();
            for (float t = 0f; t < seconds; t += 0.01f) physics.Simulate(0.01f);
        }

        [Test]
        public void ThrownIngredient_BumpsIntoTheCustomer()
        {
            var customer = SpawnCustomer(new Vector3(0f, 0f, 2f));
            var ingredient = Throw(new Vector3(0f, 0.8f, 1f), new Vector3(0f, 0f, 4f));

            Simulate(0.6f);

            Assert.Greater(ingredient.position.z, 1.4f, "it flew up to the customer (physics ran)");
            Assert.Less(ingredient.position.z, customer.transform.position.z,
                "the ingredient stays on the player's side of the customer");
        }

        [Test]
        public void Customer_IsMovedByCode_NotPushedByThings()
        {
            var customer = SpawnCustomer(new Vector3(0f, 0f, 2f));
            var body = customer.GetComponent<Rigidbody>();
            Assert.IsNotNull(body, "a rigidbody so moving the customer updates physics");
            Assert.IsTrue(body.isKinematic, "a hit never pushes the customer");
            Throw(new Vector3(0f, 0.8f, 1f), new Vector3(0f, 0f, 6f));

            Simulate(0.6f);

            Assert.AreEqual(new Vector3(0f, 0f, 2f), customer.transform.position);
        }

        [Test]
        public void HitBox_FitsEachLook()
        {
            foreach (var index in new[] { 2, 4 }) // Child (short) and Worker
            {
                var profile = ScriptableObject.CreateInstance<CustomerProfile>();
                var so = new SerializedObject(profile);
                so.FindProperty("lookIndex").intValue = index;
                so.ApplyModifiedPropertiesWithoutUndo();
                var customer = SpawnCustomer(new Vector3(index, 0f, 2f), profile);
                var look = new SerializedObject(customer).FindProperty("looks").GetArrayElementAtIndex(index);
                float height = look.FindPropertyRelative("Height").floatValue;

                Assert.IsNotNull(customer.HitBox, "look " + index);
                Assert.AreEqual(height, customer.HitBox.height, 1e-3f, "look " + index);
                Assert.AreEqual(height / 2f, customer.HitBox.center.y, 1e-3f, "look " + index);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
