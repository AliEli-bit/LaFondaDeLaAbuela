using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Fonda;

/// <summary>
/// Creates the gameplay content for the Fonda scene: ingredient/recipe data assets for the menu in CLAUDE.md §5,
/// ingredient prefabs and dispensers in a row on the straight bar, three seats with one tray each, a customer prefab
/// and the customer spawner. Called from <see cref="FondaSceneBuilder.Build"/>. Assets are created or updated in place.
/// </summary>
public static class FondaCoreLoopBuilder
{
    // ---- Paths ----------------------------------------------------------------------------
    const string IngredientDataFolder = "Assets/Data/Ingredients";
    const string RecipeFolder = "Assets/Data/Recipes";
    const string PrefabFolder = "Assets/Prefabs/Ingredients";
    const string CustomerPrefabPath = "Assets/Prefabs/Customer.prefab";
    const string CoinPrefabPath = "Assets/Prefabs/TipCoin.prefab";

    // ---- Layout (GDD §5 "Distribución de la estación"; meters, player at the origin facing +z,
    //      x to the right). Straight bar: dispensers in a row, one tray per seat. The side trays sit
    //      toward the centre (x = ±0.34) so their outer slot stays within reach. ------------------
    const float DispenserZ = 0.24f;
    const float TrayZ = 0.40f;
    const float SeatZ = 0.95f;
    // Seats in the order they are offered to customers: center, left, right (A4).
    static readonly float[] TrayX = { 0f, -0.34f, 0.34f };
    static readonly float[] SeatX = { 0f, -0.60f, 0.60f };
    static readonly string[] SeatNames = { "Center", "Left", "Right" };

    const float SlotSpacing = 0.10f;         // along the tray, which faces the player
    const int SlotCount = 3;                 // longest menu dish has 3 ingredients
    const float MinDispenserToSlot = 0.15f;  // so a socket never grabs a resting ingredient
    const float BarEdgeMargin = 0.05f;       // keep things off the bar edges

    const float PlateHeight = 0.01f;
    const float SpawnLift = 0.045f;          // spawn point above the counter top
    const float TrayBaseHeight = 0.02f;
    const float TrayBaseDepth = 0.14f;
    const float SlotLift = 0.055f;           // socket center above the counter top
    const float SocketRadius = 0.045f;

    // Tips (GDD §5 "Propinas en mano"; tips A2, A5).
    static readonly Vector2 TipJarXZ = new Vector2(0.53f, 0.24f); // right end of the bar
    const float JarInnerRadius = 0.06f;
    const float JarHeight = 0.14f;
    const float JarWall = 0.01f;
    const int JarWallSegments = 12;
    // On the customers' side of each tray; the side spots lean toward the centre to stay within reach.
    static readonly Vector2[] CoinSpots = { new Vector2(0f, 0.56f), new Vector2(-0.22f, 0.53f), new Vector2(0.22f, 0.53f) };
    const float MinTipToOthers = 0.10f;    // coin spots and jar vs dispensers, slots and each other
    const float CoinDiameter = 0.04f;
    const float CoinThickness = 0.008f;

    // ---- Shift (shift S1, S3) ---------------------------------------------------------------
    const string DifficultyPath = "Assets/Data/DifficultySettings.asset";
    const float WallClockX = 0.6f;          // right of the center seat, away from the door
    const float WallClockHeight = 2.20f;
    const float WallClockDiameter = 0.40f;
    const float WallClockDepth = 0.04f;
    static readonly Vector3 SummaryPosition = new Vector3(0f, 1.45f, 1.30f);


    class IngredientDef
    {
        public string Id, DisplayName;
        public string FileName => DisplayName.Replace("ó", "o");
        public float X;        // position along the dispenser row
        public PrimitiveType Shape;
        public Vector3 Size;   // world size in meters (a cylinder primitive is 2 units tall, handled in the prefab)
        public Color Color;
    }

    // Left to right as in the GDD: lechuga, tomate, queso, medallón, lomito, papas, 0.16 m apart.
    // Placeholder primitives until the KayKit models are imported.
    static readonly IngredientDef[] Ingredients =
    {
        new IngredientDef { Id = "lechuga",  DisplayName = "Lechuga",  X = -0.40f, Shape = PrimitiveType.Cylinder, Size = new Vector3(0.08f, 0.015f, 0.08f), Color = new Color(0.45f, 0.78f, 0.30f) },
        new IngredientDef { Id = "tomate",   DisplayName = "Tomate",   X = -0.24f, Shape = PrimitiveType.Cylinder, Size = new Vector3(0.06f, 0.015f, 0.06f), Color = new Color(0.88f, 0.20f, 0.15f) },
        new IngredientDef { Id = "queso",    DisplayName = "Queso",    X = -0.08f, Shape = PrimitiveType.Cube,     Size = new Vector3(0.07f, 0.012f, 0.07f), Color = new Color(1.00f, 0.80f, 0.20f) },
        new IngredientDef { Id = "medallon", DisplayName = "Medallón", X =  0.08f, Shape = PrimitiveType.Cylinder, Size = new Vector3(0.08f, 0.02f, 0.08f),  Color = new Color(0.40f, 0.24f, 0.13f) },
        new IngredientDef { Id = "lomito",   DisplayName = "Lomito",   X =  0.24f, Shape = PrimitiveType.Cube,     Size = new Vector3(0.08f, 0.025f, 0.05f), Color = new Color(0.65f, 0.30f, 0.22f) },
        new IngredientDef { Id = "papas",    DisplayName = "Papas",    X =  0.40f, Shape = PrimitiveType.Cube,     Size = new Vector3(0.06f, 0.03f, 0.05f),  Color = new Color(0.96f, 0.86f, 0.52f) },
    };

    class RecipeDef
    {
        public string FileName, DisplayName;
        public DishBase Base;
        public string[] IngredientIds;
    }

    // The menu confirmed by the author (CLAUDE.md §5, "Menú").
    static readonly RecipeDef[] Recipes =
    {
        new RecipeDef { FileName = "HamburguesaSola",     DisplayName = "Hamburguesa sola",      Base = DishBase.Bun,  IngredientIds = new[] { "medallon" } },
        new RecipeDef { FileName = "TazonDePapas",        DisplayName = "Tazón de papas",        Base = DishBase.Bowl, IngredientIds = new[] { "papas" } },
        new RecipeDef { FileName = "HamburguesaConQueso", DisplayName = "Hamburguesa con queso", Base = DishBase.Bun,  IngredientIds = new[] { "medallon", "queso" } },
        new RecipeDef { FileName = "LomitoConTomate",     DisplayName = "Lomito con tomate",     Base = DishBase.Bun,  IngredientIds = new[] { "lomito", "tomate" } },
        new RecipeDef { FileName = "HamburguesaCompleta", DisplayName = "Hamburguesa completa",  Base = DishBase.Bun,  IngredientIds = new[] { "medallon", "lechuga", "tomate" } },
        new RecipeDef { FileName = "LomitoCompleto",      DisplayName = "Lomito completo",       Base = DishBase.Bun,  IngredientIds = new[] { "lomito", "queso", "tomate" } },
    };

    // ---- Layout math (S1) -----------------------------------------------------------------

    static Vector3 OnFloor(float x, float z) => new Vector3(x, 0f, z);

    public static Vector3 DispenserPosition(string id)
    {
        foreach (var def in Ingredients)
            if (def.Id == id) return OnFloor(def.X, DispenserZ);
        throw new System.ArgumentException("Unknown ingredient " + id);
    }

    public static Vector3 TrayPosition(int seat) => OnFloor(TrayX[seat], TrayZ);
    public static Vector3 SeatPosition(int seat) => OnFloor(SeatX[seat], SeatZ);

    /// <summary>"Frasco de propinas en el extremo derecho de la barra".</summary>
    public static Vector3 TipJarPosition() => OnFloor(TipJarXZ.x, TipJarXZ.y);

    /// <summary>Center of the wall clock face (S1): on the back wall, 2.20 m high.</summary>
    public static Vector3 WallClockPosition() =>
        new Vector3(WallClockX, WallClockHeight, FondaSceneBuilder.BackWallZ - WallClockDepth / 2f);

    /// <summary>Where each seat's customer leaves coins: next to its tray without covering it (tips A2).</summary>
    public static Vector3 CoinSpotPosition(int seat) => OnFloor(CoinSpots[seat].x, CoinSpots[seat].y);

    // Slots run along x, facing the player.
    static Vector3 SlotPosition(int seat, int slot) =>
        TrayPosition(seat) + new Vector3((slot - (SlotCount - 1) / 2f) * SlotSpacing, 0f, 0f);

    /// <summary>True when the point is on the bar top, at least BarEdgeMargin from every edge.</summary>
    static bool OnBarTop(Vector3 p) =>
        Mathf.Abs(p.x) <= FondaSceneBuilder.BarHalfLength - BarEdgeMargin &&
        p.z >= FondaSceneBuilder.BarNearZ + BarEdgeMargin && p.z <= FondaSceneBuilder.BarFarZ - BarEdgeMargin;

    /// <summary>Returns every violated layout rule (empty when the layout is valid).</summary>
    public static List<string> ValidateLayout()
    {
        var problems = new List<string>();
        var dispensers = new List<(string name, Vector3 p)>();
        var slots = new List<(string name, Vector3 p)>();
        foreach (var def in Ingredients) dispensers.Add(("dispenser " + def.Id, DispenserPosition(def.Id)));
        for (int seat = 0; seat < SeatNames.Length; seat++)
            for (int s = 0; s < SlotCount; s++)
                slots.Add(("seat " + SeatNames[seat] + " slot " + (s + 1), SlotPosition(seat, s)));

        float maxReach = FondaSceneBuilder.MaxReach;
        foreach (var (name, p) in dispensers.Concat(slots))
        {
            var xz = new Vector2(p.x, p.z);
            float reach = xz.magnitude;
            if (reach >= maxReach)
                problems.Add(name + " is " + reach.ToString("F3") + " m from the player (limit " + maxReach + " m).");
            if (!OnBarTop(p))
                problems.Add(name + " at (" + p.x.ToString("F2") + ", " + p.z.ToString("F2") + ") is off the bar top.");
        }

        foreach (var (dName, d) in dispensers)
            foreach (var (sName, s) in slots)
            {
                float dist = Vector3.Distance(d, s);
                if (dist < MinDispenserToSlot)
                    problems.Add(dName + " is only " + dist.ToString("F3") + " m from " + sName + " (minimum " + MinDispenserToSlot + " m).");
            }

        // Tips: coin spots and the jar on the bar, within reach and clear of dispensers, slots and each other.
        var tipPoints = new List<(string name, Vector3 p)> { ("tip jar", TipJarPosition()) };
        for (int seat = 0; seat < SeatNames.Length; seat++) tipPoints.Add(("coin spot " + SeatNames[seat], CoinSpotPosition(seat)));
        foreach (var (name, p) in tipPoints)
        {
            float reach = new Vector2(p.x, p.z).magnitude;
            if (reach >= maxReach)
                problems.Add(name + " is " + reach.ToString("F3") + " m from the player (limit " + maxReach + " m).");
            if (!OnBarTop(p))
                problems.Add(name + " at (" + p.x.ToString("F2") + ", " + p.z.ToString("F2") + ") is off the bar top.");
            foreach (var (other, q) in dispensers.Concat(slots).Concat(tipPoints))
            {
                if (other == name) continue;
                float dist = Vector3.Distance(p, q);
                if (dist < MinTipToOthers)
                    problems.Add(name + " is only " + dist.ToString("F3") + " m from " + other + " (minimum " + MinTipToOthers + " m).");
            }
        }

        for (int a = 0; a < slots.Count; a++)
            for (int b = a + 1; b < slots.Count; b++)
            {
                float dist = Vector3.Distance(slots[a].p, slots[b].p);
                if (dist < 2f * SocketRadius)
                    problems.Add(slots[a].name + " and " + slots[b].name + " overlap (" + dist.ToString("F3") + " m apart).");
            }
        return problems;
    }

    // ---- Build ----------------------------------------------------------------------------

    /// <summary>Creates data, prefabs and the scene objects under <paramref name="root"/>.</summary>
    public static void Build(Transform root, List<string> errors)
    {
        EnsureFolderPath(IngredientDataFolder);
        EnsureFolderPath(RecipeFolder);
        EnsureFolderPath(PrefabFolder);

        var data = new Dictionary<string, IngredientData>();
        var prefabs = new Dictionary<string, GameObject>();
        foreach (var def in Ingredients)
        {
            var asset = CreateOrUpdateIngredientData(def);
            var prefab = CreateOrUpdatePrefab(def, asset);
            SetIngredientDataPrefab(asset, prefab);
            data[def.Id] = asset;
            prefabs[def.Id] = prefab;
        }

        var menu = new List<RecipeData>();
        foreach (var def in Recipes) menu.Add(CreateOrUpdateRecipe(def, data));

        var coinPrefab = BuildCoinPrefab();
        var seats = new BarSeat[SeatNames.Length];
        for (int i = 0; i < seats.Length; i++)
            seats[i] = BuildSeat(root, i, BuildTray(root, i), coinPrefab);
        var tipJar = BuildTipJar(root);
        BuildDispensers(root, prefabs);
        var clock = BuildClock(root, errors);
        var spawner = BuildSpawner(root, BuildCustomerPrefab(errors), seats, menu, clock, errors);
        BuildShiftManager(root, clock, spawner, tipJar);
        AssetDatabase.SaveAssets();
    }

    // ---- Data assets (S1) -----------------------------------------------------------------

    static IngredientData CreateOrUpdateIngredientData(IngredientDef def)
    {
        var path = IngredientDataFolder + "/" + def.FileName + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<IngredientData>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<IngredientData>();
            AssetDatabase.CreateAsset(asset, path);
        }
        var so = new SerializedObject(asset);
        so.FindProperty("id").stringValue = def.Id;
        so.FindProperty("displayName").stringValue = def.DisplayName;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    static void SetIngredientDataPrefab(IngredientData asset, GameObject prefab)
    {
        var so = new SerializedObject(asset);
        so.FindProperty("prefab").objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
    }

    static RecipeData CreateOrUpdateRecipe(RecipeDef def, Dictionary<string, IngredientData> data)
    {
        var path = RecipeFolder + "/" + def.FileName + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<RecipeData>();
            AssetDatabase.CreateAsset(asset, path);
        }
        var so = new SerializedObject(asset);
        so.FindProperty("displayName").stringValue = def.DisplayName;
        so.FindProperty("dishBase").enumValueIndex = (int)def.Base;
        var list = so.FindProperty("ingredients");
        list.arraySize = def.IngredientIds.Length;
        for (int i = 0; i < def.IngredientIds.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = data[def.IngredientIds[i]];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    // ---- Ingredient prefabs (S2) ----------------------------------------------------------

    static GameObject CreateOrUpdatePrefab(IngredientDef def, IngredientData data)
    {
        var go = GameObject.CreatePrimitive(def.Shape);
        go.name = def.DisplayName;
        bool cylinder = def.Shape == PrimitiveType.Cylinder;
        go.transform.localScale = cylinder ? new Vector3(def.Size.x, def.Size.y / 2f, def.Size.z) : def.Size;
        go.GetComponent<Renderer>().sharedMaterial = FondaSceneBuilder.GetOrCreateMaterial("Ingredient_" + def.Id, def.Color);
        if (cylinder)
        {
            // The cylinder primitive comes with a capsule collider, which on a flat disc becomes a ball that rolls
            // off the counter. A box matching the disc keeps it lying flat.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);
        }

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        go.AddComponent<XRGrabInteractable>();
        var ingredient = go.AddComponent<Ingredient>();
        var so = new SerializedObject(ingredient);
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        var path = PrefabFolder + "/" + def.FileName + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---- Trays (S2; core-loop S4, S8, S9) -------------------------------------------------

    static Tray BuildTray(Transform root, int seat)
    {
        float top = FondaSceneBuilder.CounterHeight;
        var trayMat = FondaSceneBuilder.GetOrCreateMaterial("Tray", new Color(0.85f, 0.85f, 0.80f));
        var slotMat = FondaSceneBuilder.GetOrCreateMaterial("TraySlotMarker", new Color(0.40f, 0.40f, 0.45f));

        var tray = new GameObject("Tray " + SeatNames[seat]);
        tray.transform.SetParent(root, false);
        tray.transform.localPosition = TrayPosition(seat) + Vector3.up * top;
        // Identity rotation: the slots run along x, facing the player.

        float baseWidth = (SlotCount - 1) * SlotSpacing + 0.10f;
        var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseGo.name = "TrayBase";
        baseGo.transform.SetParent(tray.transform, false);
        baseGo.transform.localScale = new Vector3(baseWidth, TrayBaseHeight, TrayBaseDepth);
        baseGo.transform.localPosition = new Vector3(0f, TrayBaseHeight / 2f, 0f);
        var baseRenderer = baseGo.GetComponent<Renderer>();
        baseRenderer.sharedMaterial = trayMat;

        var slots = new XRSocketInteractor[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var slotGo = new GameObject("Slot " + (i + 1));
            slotGo.transform.SetParent(tray.transform, false);
            slotGo.transform.localPosition = new Vector3((i - (SlotCount - 1) / 2f) * SlotSpacing, SlotLift, 0f);

            var trigger = slotGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = SocketRadius;

            var socket = slotGo.AddComponent<XRSocketInteractor>();
            socket.showInteractableHoverMeshes = false;
            var filter = slotGo.AddComponent<IngredientOnlySocketFilter>(); // S9: sauces never enter a slot
            socket.startingHoverFilters.Add(filter);
            socket.startingSelectFilters.Add(filter);
            slots[i] = socket;

            // Visual marker: a flat disc on the tray surface (no collider).
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "SlotMarker";
            marker.transform.SetParent(slotGo.transform, false);
            marker.transform.localPosition = new Vector3(0f, TrayBaseHeight - SlotLift + 0.002f, 0f);
            marker.transform.localScale = new Vector3(0.08f, 0.002f, 0.08f);
            marker.GetComponent<Renderer>().sharedMaterial = slotMat;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
        }

        // The dish base sits under the slots. Visual only (no collider), it is not an ingredient.
        float baseLength = (SlotCount - 1) * SlotSpacing + 0.08f;
        var bunBase = CreateBaseVisual(tray.transform, "BunBase", new Vector3(baseLength, 0.016f, 0.11f),
            FondaSceneBuilder.GetOrCreateMaterial("DishBase_Bun", new Color(0.86f, 0.62f, 0.32f)));
        var bowlBase = CreateBaseVisual(tray.transform, "BowlBase", new Vector3(baseLength, 0.03f, 0.12f),
            FondaSceneBuilder.GetOrCreateMaterial("DishBase_Bowl", new Color(0.92f, 0.92f, 0.95f)));

        var trayComponent = tray.AddComponent<Tray>();
        var so = new SerializedObject(trayComponent);
        so.FindProperty("order").objectReferenceValue = null; // closed until a customer sits down
        so.FindProperty("bunBase").objectReferenceValue = bunBase;
        so.FindProperty("bowlBase").objectReferenceValue = bowlBase;
        so.FindProperty("flashRenderer").objectReferenceValue = baseRenderer;
        var slotsProp = so.FindProperty("slots");
        slotsProp.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        return trayComponent;
    }

    static GameObject CreateBaseVisual(Transform tray, string name, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(tray, false);
        go.transform.localScale = new Vector3(size.x, size.y / 2f, size.z); // cylinder primitive is 2 units tall
        go.transform.localPosition = new Vector3(0f, TrayBaseHeight + size.y / 2f, 0f);
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.SetActive(false);
        return go;
    }

    // ---- Dispensers (core-loop S3) --------------------------------------------------------

    static void BuildDispensers(Transform root, Dictionary<string, GameObject> prefabs)
    {
        float top = FondaSceneBuilder.CounterHeight;
        var plateMat = FondaSceneBuilder.GetOrCreateMaterial("DispenserPlate", new Color(0.35f, 0.30f, 0.25f));

        foreach (var def in Ingredients)
        {
            var go = new GameObject("Dispenser_" + def.FileName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = DispenserPosition(def.Id) + Vector3.up * top;

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = "Plate";
            plate.transform.SetParent(go.transform, false);
            plate.transform.localPosition = new Vector3(0f, PlateHeight / 2f, 0f);
            plate.transform.localScale = new Vector3(0.11f, PlateHeight / 2f, 0.11f); // cylinder height is 2 units
            plate.GetComponent<Renderer>().sharedMaterial = plateMat;
            Object.DestroyImmediate(plate.GetComponent<Collider>()); // visual only; the bar is the support

            var spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(go.transform, false);
            spawn.localPosition = new Vector3(0f, PlateHeight + SpawnLift, 0f);

            var dispenser = go.AddComponent<IngredientDispenser>();
            var so = new SerializedObject(dispenser);
            so.FindProperty("ingredientPrefab").objectReferenceValue = prefabs[def.Id];
            so.FindProperty("spawnPoint").objectReferenceValue = spawn;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---- Seats (S2) -----------------------------------------------------------------------

    static BarSeat BuildSeat(Transform root, int seat, Tray tray, TipCoin coinPrefab)
    {
        var go = new GameObject("Seat " + SeatNames[seat]);
        go.transform.SetParent(root, false);
        go.transform.localPosition = SeatPosition(seat);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // faces the player

        // KayKit stool from the sketch look (no collider: customers walk through it).
        FondaSketchBuilder.PlaceStool(go.transform, go.transform.position);

        var seatComponent = go.AddComponent<BarSeat>();
        var so = new SerializedObject(seatComponent);
        so.FindProperty("tray").objectReferenceValue = tray;
        so.FindProperty("seatPoint").objectReferenceValue = go.transform;

        var coinSpot = new GameObject("CoinSpot " + SeatNames[seat]).transform;
        coinSpot.SetParent(root, false);
        coinSpot.localPosition = CoinSpotPosition(seat) + Vector3.up * (FondaSceneBuilder.CounterHeight + 0.01f);
        // The coin row runs along the spot's local x, parallel to the bar edge.
        so.FindProperty("coinSpot").objectReferenceValue = coinSpot;
        so.FindProperty("coinPrefab").objectReferenceValue = coinPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        return seatComponent;
    }

    // ---- Tips (tips S1-S3) -----------------------------------------------------------------

    /// <summary>Grabbable coin (A5: larger than a real one so it can be picked up in VR).</summary>
    static TipCoin BuildCoinPrefab()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "TipCoin";
        go.transform.localScale = new Vector3(CoinDiameter, CoinThickness / 2f, CoinDiameter);
        go.GetComponent<Renderer>().sharedMaterial = FondaSceneBuilder.GetOrCreateMaterial("Coin", new Color(0.95f, 0.75f, 0.20f));
        Object.DestroyImmediate(go.GetComponent<Collider>()); // a capsule would make the coin roll like a ball
        go.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.01f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // small and thrown into the jar
        go.AddComponent<XRGrabInteractable>();
        go.AddComponent<TipCoin>();

        EnsureFolderPath("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, CoinPrefabPath);
        Object.DestroyImmediate(go);
        return prefab.GetComponent<TipCoin>();
    }

    /// <summary>Open jar: a ring of wall boxes on a base, a trigger inside that counts coins, a pile and a label.</summary>
    static TipJar BuildTipJar(Transform root)
    {
        var glassMat = FondaSceneBuilder.GetOrCreateMaterial("TipJar", new Color(0.65f, 0.82f, 0.90f));
        var coinMat = FondaSceneBuilder.GetOrCreateMaterial("Coin", new Color(0.95f, 0.75f, 0.20f));

        var jar = new GameObject("TipJar");
        jar.transform.SetParent(root, false);
        jar.transform.localPosition = TipJarPosition() + Vector3.up * FondaSceneBuilder.CounterHeight;

        var bottom = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bottom.name = "Bottom";
        bottom.transform.SetParent(jar.transform, false);
        float outer = (JarInnerRadius + JarWall) * 2f;
        bottom.transform.localScale = new Vector3(outer, JarWall / 2f, outer);
        bottom.transform.localPosition = new Vector3(0f, JarWall / 2f, 0f);
        bottom.GetComponent<Renderer>().sharedMaterial = glassMat;
        Object.DestroyImmediate(bottom.GetComponent<Collider>());
        bottom.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);

        float segment = 2f * Mathf.PI * (JarInnerRadius + JarWall / 2f) / JarWallSegments * 1.05f;
        for (int i = 0; i < JarWallSegments; i++)
        {
            float a = i * 360f / JarWallSegments;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall " + i;
            wall.transform.SetParent(jar.transform, false);
            wall.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            wall.transform.localPosition = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, JarHeight / 2f, JarInnerRadius + JarWall / 2f);
            wall.transform.localScale = new Vector3(segment, JarHeight, JarWall);
            wall.GetComponent<Renderer>().sharedMaterial = glassMat;
        }

        // Trigger filling the inside of the jar: a released coin in here is counted.
        var trigger = jar.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        float inside = JarInnerRadius * 1.4f;
        trigger.size = new Vector3(inside, JarHeight - JarWall, inside);
        trigger.center = new Vector3(0f, JarWall + (JarHeight - JarWall) / 2f, 0f);

        var pile = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pile.name = "Pile";
        pile.transform.SetParent(jar.transform, false);
        pile.transform.localScale = new Vector3(JarInnerRadius * 1.8f, 0.001f, JarInnerRadius * 1.8f);
        pile.transform.localPosition = new Vector3(0f, JarWall, 0f);
        pile.GetComponent<Renderer>().sharedMaterial = coinMat;
        Object.DestroyImmediate(pile.GetComponent<Collider>());
        pile.SetActive(false);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(jar.transform, false);
        labelGo.transform.localPosition = new Vector3(0f, JarHeight + 0.06f, 0f); // forward is away from the player
        var label = labelGo.AddComponent<TextMeshPro>();
        label.rectTransform.sizeDelta = new Vector2(0.3f, 0.06f);
        label.fontSize = 0.5f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.20f, 0.15f, 0.10f);
        label.text = "Propinas: $0";

        var tipJar = jar.AddComponent<TipJar>();
        var so = new SerializedObject(tipJar);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("pile").objectReferenceValue = pile.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
        return tipJar;
    }

    // ---- Customers (S3-S5) ----------------------------------------------------------------

    const string CharacterFolder = "Assets/Art/Characters/";
    // The chibi heads are about twice a real head; at full Blender size a seated customer fills the player's view.
    const float CustomerLookScale = 0.75f;
    static readonly string[] CustomerTypes = { "Office", "Grandpa", "Child", "Regular", "Worker", "Student", "Tourist", "Stingy" };

    /// <summary>
    /// Customer prefab: the 8 Blender characters under one body (one shown at random per customer) and a speech bubble.
    /// Falls back to the blockout primitives when the characters are missing. No colliders.
    /// </summary>
    static Customer BuildCustomerPrefab(List<string> errors)
    {
        var skinMat = FondaSceneBuilder.GetOrCreateMaterial("CustomerSkin", new Color(0.87f, 0.68f, 0.52f));
        var bodyMat = FondaSceneBuilder.GetOrCreateMaterial("CustomerBody", new Color(0.25f, 0.45f, 0.80f));
        var faceMat = FondaSceneBuilder.GetOrCreateMaterial("CustomerMood", new Color(0.30f, 0.80f, 0.35f));
        var bubbleMat = FondaSceneBuilder.GetOrCreateMaterial("SpeechBubble", Color.white);

        var root = new GameObject("Customer");
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);

        var looks = BuildLooks(body, errors);
        if (looks.Count > 0) return SaveCustomerPrefab(root, body, null, null, null, looks, BuildBubble(body, bubbleMat));

        var torso = Part(PrimitiveType.Capsule, "Torso", body, new Vector3(0f, 0.75f, 0f), new Vector3(0.36f, 0.55f, 0.30f), bodyMat);
        var head = Part(PrimitiveType.Sphere, "Head", body, new Vector3(0f, 1.45f, 0f), Vector3.one * 0.24f, skinMat);
        // A nose so the facing direction and the head shake read from the bar.
        Part(PrimitiveType.Sphere, "Nose", head.transform, new Vector3(0f, 0f, 0.5f), Vector3.one * 0.25f, skinMat);
        var mood = Part(PrimitiveType.Sphere, "Mood", body, new Vector3(0f, 1.66f, 0f), Vector3.one * 0.08f, faceMat);
        return SaveCustomerPrefab(root, body, head.transform, torso.GetComponent<Renderer>(), mood.GetComponent<Renderer>(),
            looks, BuildBubble(body, bubbleMat));
    }

    static TextMeshPro BuildBubble(Transform body, Material bubbleMat)
    {
        var bubbleGo = new GameObject("Bubble");
        bubbleGo.transform.SetParent(body, false);
        bubbleGo.transform.localPosition = new Vector3(0f, 1.88f, 0f);
        var text = bubbleGo.AddComponent<TextMeshPro>();
        text.rectTransform.sizeDelta = new Vector2(0.6f, 0.2f);
        text.fontSize = 0.7f;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.15f, 0.12f, 0.10f);
        text.text = "Pedido";
        var back = Part(PrimitiveType.Quad, "Background", bubbleGo.transform, new Vector3(0f, 0f, 0.005f), new Vector3(0.64f, 0.22f, 1f), bubbleMat);
        back.transform.localRotation = Quaternion.identity;
        return text;
    }

    /// <summary>One inactive instance of each Blender character under the body, with the parts Customer drives.</summary>
    static List<CustomerLook> BuildLooks(Transform body, List<string> errors)
    {
        var looks = new List<CustomerLook>();
        foreach (var type in CustomerTypes)
        {
            var path = CharacterFolder + "Customer_" + type + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                errors.Add("Missing character " + path + "; customers use the blockout figure.");
                return new List<CustomerLook>();
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model, body);
            go.name = "Customer_" + type;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * CustomerLookScale;

            var parts = go.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => t);
            Transform Find(string name)
            {
                if (parts.TryGetValue(name, out var t)) return t;
                errors.Add(path + " has no " + name + ".");
                return null;
            }
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);

            looks.Add(new CustomerLook
            {
                Root = go, Hips = Find("Hips"), Head = Find("Head"), LegL = Find("Leg_L"), LegR = Find("Leg_R"),
                Happy = Find("Face_Happy")?.gameObject, Neutral = Find("Face_Neutral")?.gameObject,
                Angry = Find("Face_Angry")?.gameObject,
                Height = bounds.max.y - go.transform.position.y,
            });
            go.SetActive(false);
        }
        return looks;
    }

    static Customer SaveCustomerPrefab(GameObject root, Transform body, Transform head, Renderer bodyRenderer,
        Renderer faceRenderer, List<CustomerLook> looks, TextMeshPro bubble)
    {
        var customer = root.AddComponent<Customer>();
        var so = new SerializedObject(customer);
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("head").objectReferenceValue = head;
        so.FindProperty("bodyRenderer").objectReferenceValue = bodyRenderer;
        so.FindProperty("faceRenderer").objectReferenceValue = faceRenderer;
        so.FindProperty("bubble").objectReferenceValue = bubble;
        so.FindProperty("seatHeight").floatValue = FondaSketchBuilder.StoolHeight;
        var list = so.FindProperty("looks");
        list.arraySize = looks.Count;
        for (int i = 0; i < looks.Count; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var l = looks[i];
            e.FindPropertyRelative("Root").objectReferenceValue = l.Root;
            e.FindPropertyRelative("Hips").objectReferenceValue = l.Hips;
            e.FindPropertyRelative("Head").objectReferenceValue = l.Head;
            e.FindPropertyRelative("LegL").objectReferenceValue = l.LegL;
            e.FindPropertyRelative("LegR").objectReferenceValue = l.LegR;
            e.FindPropertyRelative("Happy").objectReferenceValue = l.Happy;
            e.FindPropertyRelative("Neutral").objectReferenceValue = l.Neutral;
            e.FindPropertyRelative("Angry").objectReferenceValue = l.Angry;
            e.FindPropertyRelative("Height").floatValue = l.Height;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolderPath("Assets/Prefabs");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, CustomerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<Customer>();
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static CustomerSpawner BuildSpawner(Transform root, Customer prefab, BarSeat[] seats, List<RecipeData> menu,
        FondaClock clock, List<string> errors)
    {
        var door = new GameObject("Door").transform;
        door.SetParent(root, false);
        door.localPosition = new Vector3(FondaSceneBuilder.DoorCenterX, 0f, FondaSceneBuilder.BackWallZ - 0.2f);
        door.localRotation = Quaternion.Euler(0f, 180f, 0f);

        var go = new GameObject("CustomerSpawner");
        go.transform.SetParent(root, false);
        var spawner = go.AddComponent<CustomerSpawner>();
        var so = new SerializedObject(spawner);
        so.FindProperty("customerPrefab").objectReferenceValue = prefab;
        so.FindProperty("door").objectReferenceValue = door;
        so.FindProperty("clock").objectReferenceValue = clock;
        var seatsProp = so.FindProperty("seats");
        seatsProp.arraySize = seats.Length;
        for (int i = 0; i < seats.Length; i++) seatsProp.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
        var menuProp = so.FindProperty("menu");
        menuProp.arraySize = menu.Count;
        for (int i = 0; i < menu.Count; i++) menuProp.GetArrayElementAtIndex(i).objectReferenceValue = menu[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        if (prefab == null) errors.Add("Customer prefab could not be saved at " + CustomerPrefabPath + ".");
        if (menu.Count != Recipes.Length) errors.Add("Menu has " + menu.Count + " recipes, expected " + Recipes.Length + ".");
        return spawner;
    }

    // ---- Shift: clock, wall clock and summary (shift S1, S3, S5) ------------------------------

    /// <summary>Balance asset (A7): created with the GDD defaults only when missing, so hand-tuned values survive.</summary>
    static DifficultySettings GetOrCreateDifficulty()
    {
        var asset = AssetDatabase.LoadAssetAtPath<DifficultySettings>(DifficultyPath);
        if (asset != null) return asset;
        EnsureFolderPath("Assets/Data");
        asset = ScriptableObject.CreateInstance<DifficultySettings>();
        AssetDatabase.CreateAsset(asset, DifficultyPath);
        return asset;
    }

    /// <summary>Round wall clock on the back wall with hour and minute hands, driven by <see cref="FondaClock"/>.</summary>
    static FondaClock BuildClock(Transform root, List<string> errors)
    {
        var settings = GetOrCreateDifficulty();
        if (settings == null) errors.Add("DifficultySettings could not be saved at " + DifficultyPath + ".");

        var faceMat = FondaSceneBuilder.GetOrCreateMaterial("ClockFace", new Color(0.97f, 0.95f, 0.88f));
        var handMat = FondaSceneBuilder.GetOrCreateMaterial("ClockHand", new Color(0.15f, 0.12f, 0.10f));

        var go = new GameObject("WallClock");
        go.transform.SetParent(root, false);
        go.transform.localPosition = WallClockPosition();

        // Cylinder lying with its axis along z: a disc facing the player.
        var face = Part(PrimitiveType.Cylinder, "Face", go.transform, Vector3.zero,
            new Vector3(WallClockDiameter, WallClockDepth / 2f, WallClockDiameter), faceMat);
        face.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        float front = -WallClockDepth / 2f - 0.005f;
        for (int i = 0; i < 12; i++)
        {
            float a = i * 30f;
            var tick = Part(PrimitiveType.Cube, "Tick " + i, go.transform, Vector3.zero,
                new Vector3(0.012f, i % 3 == 0 ? 0.04f : 0.02f, 0.005f), handMat);
            tick.transform.localRotation = Quaternion.Euler(0f, 0f, -a);
            tick.transform.localPosition = tick.transform.localRotation * new Vector3(0f, WallClockDiameter * 0.42f, 0f) +
                                           new Vector3(0f, 0f, front);
        }

        var hourHand = Hand(go.transform, "HourHand", 0.10f, 0.016f, front - 0.004f, handMat);
        var minuteHand = Hand(go.transform, "MinuteHand", 0.16f, 0.010f, front - 0.010f, handMat);

        var clock = go.AddComponent<FondaClock>();
        var so = new SerializedObject(clock);
        so.FindProperty("settings").objectReferenceValue = settings;
        so.FindProperty("hourHand").objectReferenceValue = hourHand;
        so.FindProperty("minuteHand").objectReferenceValue = minuteHand;
        so.ApplyModifiedPropertiesWithoutUndo();
        return clock;
    }

    /// <summary>A pivot at the clock center with a bar pointing to 12 o'clock (local +y).</summary>
    static Transform Hand(Transform clock, string name, float length, float width, float z, Material mat)
    {
        var pivot = new GameObject(name).transform;
        pivot.SetParent(clock, false);
        pivot.localPosition = new Vector3(0f, 0f, z);
        Part(PrimitiveType.Cube, "Bar", pivot, new Vector3(0f, length / 2f, 0f), new Vector3(width, length, 0.004f), mat);
        return pivot;
    }

    static void BuildShiftManager(Transform root, FondaClock clock, CustomerSpawner spawner, TipJar tipJar)
    {
        var labelGo = new GameObject("ShiftSummary");
        labelGo.transform.SetParent(root, false);
        labelGo.transform.localPosition = SummaryPosition; // forward is +z: readable from the player
        var label = labelGo.AddComponent<TextMeshPro>();
        label.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);
        label.fontSize = 1.2f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.20f, 0.15f, 0.10f);
        label.text = "¡Se terminó el turno!";

        var go = new GameObject("ShiftManager");
        go.transform.SetParent(root, false);
        var manager = go.AddComponent<ShiftManager>();
        var so = new SerializedObject(manager);
        so.FindProperty("clock").objectReferenceValue = clock;
        so.FindProperty("spawner").objectReferenceValue = spawner;
        so.FindProperty("tipJar").objectReferenceValue = tipJar;
        so.FindProperty("summaryLabel").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
        labelGo.SetActive(false);
    }

    // ---- Helpers --------------------------------------------------------------------------

    static void EnsureFolderPath(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
