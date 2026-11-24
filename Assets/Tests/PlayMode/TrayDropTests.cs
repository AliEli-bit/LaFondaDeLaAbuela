#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda.Tests
{
    /// <summary>
    /// Ingredients dropped with physics on a tray of the Fonda scene, the way the player releases them.
    /// The dish is built as a pile on its base, wherever on the tray the ingredient lands.
    /// </summary>
    public class TrayDropTests
    {
        const string ScenePath = "Assets/Scenes/Fonda.unity";
        const float DropHeight = 0.18f; // above the counter, where the hand lets go

        Tray tray;
        XRSocketInteractor[] slots;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true; // OpenXR logs errors without a headset
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            // Only the tray is under test: no customers arrive and the shift does not run.
            foreach (var spawner in Object.FindObjectsByType<CustomerSpawner>(FindObjectsSortMode.None)) spawner.enabled = false;
            foreach (var shift in Object.FindObjectsByType<ShiftManager>(FindObjectsSortMode.None)) shift.enabled = false;
            foreach (var customer in Object.FindObjectsByType<Customer>(FindObjectsSortMode.None)) Object.Destroy(customer.gameObject);
            yield return null;

            tray = Object.FindObjectsByType<Tray>(FindObjectsSortMode.None).First(t => t.name == "Tray Center");
            tray.SetOrder(AssetDatabase.LoadAssetAtPath<RecipeData>("Assets/Data/Recipes/LomitoCompleto.asset"));
            slots = tray.GetComponentsInChildren<XRSocketInteractor>(true);
            yield return null;
        }

        GameObject Drop(string prefabName, Vector3 offset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ingredients/" + prefabName + ".prefab");
            return Object.Instantiate(prefab, tray.transform.position + offset, Quaternion.identity);
        }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
        }

        int HeldCount => slots.Count(s => s.hasSelection);

        bool IsHeld(GameObject ingredient) =>
            slots.Any(s => s.hasSelection && ((Component)s.firstInteractableSelected).gameObject == ingredient);

        // Bug: "no se puede armar un plato, los alimentos quedan dispersos por el plato".
        [UnityTest]
        public IEnumerator Drops_AnywhereOnTheDish_PileUpOnTheBase_AndCompleteTheOrder()
        {
            bool correct = false;
            tray.OrderCorrect += _ => correct = true;

            // Off the center of the dish, toward a corner of the tray.
            var lomito = Drop("Lomito", new Vector3(0.05f, DropHeight, 0.04f));
            yield return Settle();
            Assert.IsTrue(IsHeld(lomito), "an ingredient dropped off center is caught by the tray");
            var center = tray.transform.position;
            Assert.AreEqual(center.x, lomito.transform.position.x, 0.005f, "the first layer sits on the middle of the base");
            Assert.AreEqual(center.z, lomito.transform.position.z, 0.005f, "the first layer sits on the middle of the base");

            // On the other side of the dish.
            var queso = Drop("Queso", new Vector3(-0.04f, DropHeight, -0.03f));
            yield return Settle();
            Assert.IsTrue(IsHeld(queso), "a second ingredient is caught too");
            Assert.Greater(queso.transform.position.y, lomito.transform.position.y, "the second layer sits on the first");
            Assert.AreEqual(lomito.transform.position.x, queso.transform.position.x, 0.005f, "the layers pile up");
            Assert.AreEqual(lomito.transform.position.z, queso.transform.position.z, 0.005f, "the layers pile up");

            // Straight on top of the pile.
            Drop("Tomate", new Vector3(0f, DropHeight + 0.05f, 0f));
            yield return Settle();
            Assert.IsTrue(correct, "the three ordered ingredients complete the dish");
        }

        // The player holds the ingredient over the tray and opens the hand, the way the grandma serves.
        [UnityTest]
        public IEnumerator Release_FromTheHand_OverTheTray_LandsOnThePile()
        {
            var manager = Object.FindFirstObjectByType<XRInteractionManager>();
            var handGo = new GameObject("Test Hand");
            handGo.AddComponent<SphereCollider>().isTrigger = true;
            var handBody = handGo.AddComponent<Rigidbody>();
            handBody.isKinematic = true;
            var hand = handGo.AddComponent<XRDirectInteractor>();
            yield return null;

            GameObject first = null;
            string[] names = { "Lomito", "Queso" };
            Vector3[] over = { new Vector3(0.06f, 0.10f, -0.04f), new Vector3(-0.05f, 0.12f, 0.05f) };
            for (int i = 0; i < names.Length; i++)
            {
                var ingredient = Drop(names[i], new Vector3(0f, 0.6f, -0.5f)); // picked up away from the tray
                var grab = ingredient.GetComponent<XRGrabInteractable>();
                handGo.transform.position = ingredient.transform.position;
                manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)grab);
                yield return null;

                handGo.transform.position = tray.transform.position + over[i]; // carry it over the tray
                for (int f = 0; f < 20; f++) yield return new WaitForFixedUpdate();
                manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)grab); // open the hand
                yield return Settle();

                Assert.IsTrue(IsHeld(ingredient), names[i] + " released over the tray lands on the pile");
                if (first == null) first = ingredient;
                else Assert.Greater(ingredient.transform.position.y, first.transform.position.y, "it rests on the layer below");
            }
            Object.Destroy(handGo);
        }

        // A wrong ingredient must not fill the tray: with a one-ingredient order, the right one still lands on the pile,
        // and taking the wrong one out completes the dish (GDD: "el jugador saca ese ingrediente y sigue").
        [UnityTest]
        public IEnumerator WrongIngredient_DoesNotBlockTheTray_AndTakingItOutCompletesTheDish()
        {
            tray.SetOrder(AssetDatabase.LoadAssetAtPath<RecipeData>("Assets/Data/Recipes/TazonDePapas.asset"));
            yield return null;
            bool wrong = false, correct = false;
            tray.OrderWrong += _ => wrong = true;
            tray.OrderCorrect += _ => correct = true;

            var queso = Drop("Queso", new Vector3(0.03f, DropHeight, 0f));
            yield return Settle();
            Assert.IsTrue(IsHeld(queso) && wrong, "the wrong ingredient is caught and reported");

            var papas = Drop("Papas", new Vector3(-0.04f, DropHeight, 0.03f));
            yield return Settle();
            Assert.IsTrue(IsHeld(papas), "the ordered ingredient still lands on the pile");
            Assert.IsFalse(correct, "the dish is not served while the wrong ingredient is on it");

            var manager = Object.FindFirstObjectByType<XRInteractionManager>();
            var hand = new GameObject("Test Hand").AddComponent<XRDirectInteractor>();
            yield return null;
            manager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)queso.GetComponent<XRGrabInteractable>());
            yield return null;
            Assert.IsTrue(correct, "taking the wrong ingredient out completes the dish");
            Object.Destroy(hand.gameObject);
        }

        // Existing behavior: an ingredient that was not ordered is still caught and reported as an error.
        [UnityTest]
        public IEnumerator Drop_IngredientNotOrdered_IsCaught_AndReportedAsWrong()
        {
            bool wrong = false;
            tray.OrderWrong += _ => wrong = true;

            var medallon = Drop("Medallon", new Vector3(-0.03f, DropHeight, 0.02f));
            yield return Settle();

            Assert.IsTrue(IsHeld(medallon), "the ingredient is caught by the tray");
            Assert.IsTrue(wrong, "the tray reports the ingredient as not ordered");
            Assert.AreEqual(1, HeldCount);
        }
    }
}
#endif
