using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda.Tests
{
    public class TrayAndSocketTests
    {
        GameObject root;

        [SetUp] public void SetUp() => root = new GameObject("TestRoot");
        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            return go;
        }

        static RecipeData Recipe(DishBase dishBase, int ingredientCount)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeData>();
            var so = new SerializedObject(recipe);
            so.FindProperty("dishBase").enumValueIndex = (int)dishBase;
            var list = so.FindProperty("ingredients");
            list.arraySize = ingredientCount;
            for (int i = 0; i < ingredientCount; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = ScriptableObject.CreateInstance<IngredientData>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }

        (Tray tray, GameObject bun, GameObject bowl, XRSocketInteractor[] slots) BuildTray()
        {
            var tray = Child("Tray").AddComponent<Tray>();
            var bun = Child("BunBase");
            var bowl = Child("BowlBase");
            var slots = new XRSocketInteractor[3];
            for (int i = 0; i < slots.Length; i++)
            {
                var slotGo = Child("Slot " + i);
                slotGo.AddComponent<SphereCollider>().isTrigger = true;
                slots[i] = slotGo.AddComponent<XRSocketInteractor>();
            }
            var so = new SerializedObject(tray);
            so.FindProperty("bunBase").objectReferenceValue = bun;
            so.FindProperty("bowlBase").objectReferenceValue = bowl;
            var slotsProp = so.FindProperty("slots");
            slotsProp.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
                slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return (tray, bun, bowl, slots);
        }

        // S8: the tray shows the base of the active order.
        [Test]
        public void SetOrder_ShowsOnlyTheBaseOfTheOrder()
        {
            var (tray, bun, bowl, _) = BuildTray();

            tray.SetOrder(Recipe(DishBase.Bowl, 1));
            Assert.IsTrue(bowl.activeSelf, "bowl after a bowl order");
            Assert.IsFalse(bun.activeSelf, "bun after a bowl order");

            tray.SetOrder(Recipe(DishBase.Bun, 2));
            Assert.IsTrue(bun.activeSelf, "bun after a bun order");
            Assert.IsFalse(bowl.activeSelf, "bowl after a bun order");
        }

        // Existing behavior touched by SetOrder: one active slot per ordered ingredient.
        [Test]
        public void SetOrder_StillActivatesOneSlotPerIngredient()
        {
            var (tray, _, _, slots) = BuildTray();
            tray.SetOrder(Recipe(DishBase.Bun, 2));
            Assert.IsTrue(slots[0].gameObject.activeSelf);
            Assert.IsTrue(slots[1].gameObject.activeSelf);
            Assert.IsFalse(slots[2].gameObject.activeSelf);
        }

        // S9: slots only take ingredients; a sauce bottle (any other grabbable) is rejected.
        [Test]
        public void SlotFilter_AcceptsIngredients_RejectsOtherGrabbables()
        {
            var filter = Child("Filter").AddComponent<IngredientOnlySocketFilter>();
            var socket = Child("Socket").AddComponent<XRSocketInteractor>();

            var ingredientGo = Child("Ingredient");
            ingredientGo.AddComponent<Rigidbody>();
            var ingredient = ingredientGo.AddComponent<XRGrabInteractable>();
            ingredientGo.AddComponent<Ingredient>();

            var bottleGo = Child("SauceBottle");
            bottleGo.AddComponent<Rigidbody>();
            var bottle = bottleGo.AddComponent<XRGrabInteractable>();

            Assert.IsTrue(filter.Process((IXRSelectInteractor)socket, ingredient), "select ingredient");
            Assert.IsFalse(filter.Process((IXRSelectInteractor)socket, bottle), "select bottle");
            Assert.IsTrue(filter.Process((IXRHoverInteractor)socket, ingredient), "hover ingredient");
            Assert.IsFalse(filter.Process((IXRHoverInteractor)socket, bottle), "hover bottle");
        }
    }
}
