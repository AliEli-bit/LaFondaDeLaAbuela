using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Fonda.Tests
{
    public class CustomerTypeTests
    {
        GameObject root;
        readonly List<Object> assets = new List<Object>();
        List<RecipeData> menu;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TestRoot");
            // Same ingredient counts as the real menu: sola 1, papas 1, queso 2, tomate 2, completa 3, lomito completo 3.
            menu = new List<RecipeData>
            {
                Recipe("Hamburguesa sola", 1), Recipe("Tazón de papas", 1), Recipe("Hamburguesa con queso", 2),
                Recipe("Lomito con tomate", 2), Recipe("Hamburguesa completa", 3), Recipe("Lomito completo", 3),
            };
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var a in assets) Object.DestroyImmediate(a);
            assets.Clear();
        }

        RecipeData Recipe(string name, int ingredients)
        {
            var recipe = ScriptableObject.CreateInstance<RecipeData>();
            assets.Add(recipe);
            var so = new SerializedObject(recipe);
            so.FindProperty("displayName").stringValue = name;
            var list = so.FindProperty("ingredients");
            list.arraySize = ingredients;
            for (int i = 0; i < ingredients; i++)
            {
                var ingredient = ScriptableObject.CreateInstance<IngredientData>();
                assets.Add(ingredient);
                list.GetArrayElementAtIndex(i).objectReferenceValue = ingredient;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return recipe;
        }

        CustomerProfile Profile(string name, float weight = 1f, int minCoins = 1, int maxCoins = 3, float patience = 1f,
            float eat = 1f, string title = null, params int[] favorites)
        {
            var profile = ScriptableObject.CreateInstance<CustomerProfile>();
            assets.Add(profile);
            var so = new SerializedObject(profile);
            so.FindProperty("displayName").stringValue = name;
            so.FindProperty("weight").floatValue = weight;
            so.FindProperty("minCoins").intValue = minCoins;
            so.FindProperty("maxCoins").intValue = maxCoins;
            so.FindProperty("patienceMultiplier").floatValue = patience;
            so.FindProperty("eatMultiplier").floatValue = eat;
            so.FindProperty("orderTitle").stringValue = title;
            var list = so.FindProperty("favorites");
            list.arraySize = favorites.Length;
            for (int i = 0; i < favorites.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = menu[favorites[i]];
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        static FondaCoreLoopBuilder.CustomerProfileDef Def(string type) =>
            FondaCoreLoopBuilder.CustomerProfiles.Single(d => d.Type == type);

        // S2 + A1-A3, A6: the GDD table, one profile per Blender character (S3).
        [TestCase("Office", "Oficinista apurado", 0.7f, 1f, 0, 3, null)]
        [TestCase("Grandpa", "Abuelito paciente", 1.5f, 1f, 1, 3, "HamburguesaSola,TazonDePapas")]
        [TestCase("Child", "Niño", 1.0f, 1f, 1, 2, "TazonDePapas")]
        [TestCase("Regular", "Habitual", 1.0f, 1f, 2, 3, null)]
        [TestCase("Worker", "Trabajador de obra", 1.0f, 1f, 2, 3, "HamburguesaCompleta,LomitoCompleto")]
        [TestCase("Student", "Estudiante", 1.2f, 1f, 1, 2, "HamburguesaSola,TazonDePapas")]
        [TestCase("Tourist", "Turista", 1.3f, 2f, 0, 3, null)]
        [TestCase("Stingy", "Tacaño", 1.0f, 1f, 0, 1, null)]
        public void Defaults_FollowTheGddTable(string type, string name, float patience, float eat, int minCoins,
            int maxCoins, string favorites)
        {
            Assert.AreEqual(8, FondaCoreLoopBuilder.CustomerProfiles.Length);
            var d = Def(type);
            Assert.AreEqual(name, d.DisplayName);
            Assert.AreEqual(patience, d.Patience, 1e-4f);
            Assert.AreEqual(eat, d.Eat, 1e-4f);
            Assert.AreEqual(minCoins, d.MinCoins);
            Assert.AreEqual(maxCoins, d.MaxCoins);
            Assert.AreEqual(favorites, d.Favorites == null ? null : string.Join(",", d.Favorites));
            Assert.AreEqual(type == "Regular" ? "¡Lo de siempre, doña!" : null, d.OrderTitle);
            Assert.That(FondaCoreLoopBuilder.CustomerTypeNames, Does.Contain(type), "each type has its Blender character");
        }

        // A1: with the +2 combo bonus no type leaves more than 5 coins, so they fit by the tray.
        [Test]
        public void Defaults_FitTheBar()
        {
            foreach (var d in FondaCoreLoopBuilder.CustomerProfiles)
            {
                Assert.LessOrEqual(d.MinCoins, d.MaxCoins, d.Type);
                Assert.LessOrEqual(d.MaxCoins + 2, 5, d.Type);
            }
        }

        // S1: "multiplicador de paciencia" and "tiempo comiendo".
        [Test]
        public void Profile_ScalesPatienceAndEatingTime()
        {
            var office = Profile("Oficinista", patience: 0.7f);
            var tourist = Profile("Turista", patience: 1.3f, eat: 2f);
            Assert.AreEqual(14f, office.PatienceFor(20f), 1e-4f);
            Assert.AreEqual(3f, office.EatFor(3f), 1e-4f);
            Assert.AreEqual(26f, tourist.PatienceFor(20f), 1e-4f);
            Assert.AreEqual(6f, tourist.EatFor(3f), 1e-4f);
        }

        // S2 "Pide" + A3: favourites, or any dish.
        [Test]
        public void Dishes_AreTheFavourites_OrTheWholeMenu()
        {
            CollectionAssert.AreEqual(new[] { 1 }, CustomerTypeRule.Dishes(Profile("Niño", favorites: new[] { 1 }), menu));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, CustomerTypeRule.Dishes(Profile("Tacaño"), menu));
        }

        // A4: the clock's ingredient count, or the closest one among the favourites.
        [Test]
        public void DishCandidates_FollowTheClock_WithinTheFavourites()
        {
            var any = Profile("Turista");
            CollectionAssert.AreEquivalent(new[] { 2, 3 }, CustomerTypeRule.DishCandidates(any, menu, 2));
            var grandpa = Profile("Abuelito", favorites: new[] { 0, 1 });
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, CustomerTypeRule.DishCandidates(grandpa, menu, 3));
            var worker = Profile("Trabajador", favorites: new[] { 4, 5 });
            CollectionAssert.AreEquivalent(new[] { 4, 5 }, CustomerTypeRule.DishCandidates(worker, menu, 1));
        }

        // A4 (L6): any type comes in at any time, as long as one of its dishes is on the menu.
        [Test]
        public void Eligible_AreTheTypesWithADishOnTheMenu()
        {
            var offMenu = ScriptableObject.CreateInstance<RecipeData>();
            assets.Add(offMenu);
            var lost = Profile("Perdido");
            var so = new SerializedObject(lost);
            var list = so.FindProperty("favorites");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = offMenu;
            so.ApplyModifiedPropertiesWithoutUndo();

            var profiles = new List<CustomerProfile>
            {
                Profile("Oficinista"), Profile("Abuelito", favorites: new[] { 0, 1 }),
                Profile("Trabajador", favorites: new[] { 4, 5 }), null, lost,
            };
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, CustomerTypeRule.Eligible(profiles, menu));
        }

        // A5: "con qué frecuencia aparece cada uno", a weight per type.
        [Test]
        public void Pick_FollowsTheWeights()
        {
            var profiles = new List<CustomerProfile> { Profile("A", 1f), Profile("B", 0f), Profile("C", 3f) };
            var all = new[] { 0, 1, 2 };
            Assert.AreEqual(0, CustomerTypeRule.Pick(profiles, all, 0f));
            Assert.AreEqual(0, CustomerTypeRule.Pick(profiles, all, 0.24f));
            Assert.AreEqual(2, CustomerTypeRule.Pick(profiles, all, 0.26f));
            Assert.AreEqual(2, CustomerTypeRule.Pick(profiles, all, 0.999f));
            Assert.AreEqual(2, CustomerTypeRule.Pick(profiles, new[] { 1, 2 }, 0f), "only eligible types");
            Assert.AreEqual(1, CustomerTypeRule.Pick(profiles, new[] { 1 }, 0.5f), "all weights zero: the first eligible");
            Assert.AreEqual(-1, CustomerTypeRule.Pick(profiles, new int[0], 0.5f));
        }

        // S2 "Propina" + A1: the coins follow the type's range; no profile keeps 1-3.
        [Test]
        public void Seat_LeavesTheTipOfTheType()
        {
            var seat = new GameObject("Seat").AddComponent<BarSeat>();
            seat.transform.SetParent(root.transform);
            var spot = new GameObject("CoinSpot").transform;
            spot.SetParent(root.transform);
            var prefab = new GameObject("Coin");
            prefab.transform.SetParent(root.transform);
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<XRGrabInteractable>();
            var so = new SerializedObject(seat);
            so.FindProperty("coinSpot").objectReferenceValue = spot;
            so.FindProperty("coinPrefab").objectReferenceValue = prefab.AddComponent<TipCoin>();
            so.ApplyModifiedPropertiesWithoutUndo();

            int Coins(CustomerProfile profile, float waited, int bonus = 0)
            {
                var cycle = new CustomerCycle(25f, 3f);
                cycle.Sit();
                cycle.Tick(waited);
                cycle.Serve();
                var coins = seat.LeaveTip(cycle, bonus, profile);
                foreach (var c in coins) c.transform.SetParent(root.transform);
                return coins.Count;
            }

            var office = Profile("Oficinista", minCoins: 0, maxCoins: 3);
            var stingy = Profile("Tacaño", minCoins: 0, maxCoins: 1);
            var worker = Profile("Trabajador", minCoins: 2, maxCoins: 3);
            Assert.AreEqual(3, Coins(office, 0f), "fast office worker: good tip");
            Assert.AreEqual(0, Coins(office, 24f, 2), "slow office worker: nothing, and no combo bonus on nothing");
            Assert.AreEqual(1, Coins(stingy, 0f));
            Assert.AreEqual(2, Coins(worker, 24f));
            Assert.AreEqual(3, Coins(null, 0f), "no type: 1-3 as before");
        }

        // A6: the regular says "lo de siempre" instead of the dish name; the ingredients stay below.
        [Test]
        public void OrderBubble_UsesTheTypeTitle()
        {
            var dish = menu[0];
            Assert.That(Customer.OrderText(dish, Profile("Habitual", title: "¡Lo de siempre, doña!")),
                Does.StartWith("¡Lo de siempre, doña!\n"));
            Assert.That(Customer.OrderText(dish, Profile("Turista")), Does.StartWith("Hamburguesa sola\n"));
            Assert.That(Customer.OrderText(dish), Does.StartWith("Hamburguesa sola\n"));
        }
    }
}
