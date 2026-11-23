using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Fonda;

/// <summary>
/// Creates the core-loop content for the Fonda scene: ingredient/recipe data assets, ingredient prefabs,
/// a tray with sockets and one dispenser per ingredient, all on the counter top.
/// Called from <see cref="FondaSceneBuilder.Build"/>. Assets are created or updated in place (idempotent).
/// </summary>
public static class FondaCoreLoopBuilder
{
    // ---- Paths ----------------------------------------------------------------------------
    const string IngredientDataFolder = "Assets/Data/Ingredients";
    const string RecipeFolder = "Assets/Data/Recipes";
    const string PrefabFolder = "Assets/Prefabs/Ingredients";
    const string RecipeAssetPath = RecipeFolder + "/ArrozConHuevo.asset";

    // ---- Layout (meters; x centered on the player, z measured from the player, y world) ----
    // Everything must stay on the counter top and within MaxReach horizontally of the origin.
    const float DispenserZ = 0.38f;
    const float DispenserSpacing = 0.18f;
    const float SlotZ = 0.55f;
    const float SlotSpacing = 0.15f;
    const int SlotCount = 3;                 // longest menu dish has 3 ingredients
    const float MinDispenserToSlot = 0.15f;  // so a socket never grabs a resting ingredient
    const float CounterTopMargin = 0.0f;

    const float PlateHeight = 0.01f;
    const float SpawnLift = 0.045f;          // spawn point above the counter top
    const float TrayBaseHeight = 0.02f;
    const float SlotLift = 0.055f;           // socket center above the counter top
    const float SocketRadius = 0.05f;

    class IngredientDef
    {
        public string Id, DisplayName;
        public PrimitiveType Shape;
        public Vector3 Scale;
        public Color Color;
    }

    static readonly IngredientDef[] Ingredients =
    {
        new IngredientDef { Id = "arroz",    DisplayName = "Arroz",    Shape = PrimitiveType.Cylinder, Scale = new Vector3(0.08f, 0.025f, 0.08f), Color = new Color(0.95f, 0.95f, 0.88f) },
        new IngredientDef { Id = "huevo",    DisplayName = "Huevo",    Shape = PrimitiveType.Sphere,   Scale = new Vector3(0.05f, 0.07f, 0.05f),  Color = new Color(1.00f, 0.90f, 0.45f) },
        new IngredientDef { Id = "papa",     DisplayName = "Papa",     Shape = PrimitiveType.Sphere,   Scale = new Vector3(0.08f, 0.06f, 0.07f),  Color = new Color(0.60f, 0.45f, 0.25f) },
        new IngredientDef { Id = "carne",    DisplayName = "Carne",    Shape = PrimitiveType.Cube,     Scale = new Vector3(0.08f, 0.04f, 0.06f),  Color = new Color(0.70f, 0.20f, 0.20f) },
        new IngredientDef { Id = "ensalada", DisplayName = "Ensalada", Shape = PrimitiveType.Capsule,  Scale = new Vector3(0.05f, 0.04f, 0.05f),  Color = new Color(0.30f, 0.70f, 0.25f) },
    };

    // ---- Layout math (S6) -----------------------------------------------------------------

    static Vector2 DispenserXZ(int i) => new Vector2((i - (Ingredients.Length - 1) / 2f) * DispenserSpacing, DispenserZ);
    static Vector2 SlotXZ(int i) => new Vector2((i - (SlotCount - 1) / 2f) * SlotSpacing, SlotZ);

    /// <summary>Returns every violated layout rule (empty when the layout is valid).</summary>
    public static List<string> ValidateLayout()
    {
        var problems = new List<string>();
        var points = new List<(string name, Vector2 xz)>();
        for (int i = 0; i < Ingredients.Length; i++) points.Add(("dispenser " + Ingredients[i].Id, DispenserXZ(i)));
        for (int i = 0; i < SlotCount; i++) points.Add(("slot " + i, SlotXZ(i)));

        float minZ = FondaSceneBuilder.CounterFrontZ + CounterTopMargin;
        float maxZ = FondaSceneBuilder.CounterFrontZ + FondaSceneBuilder.CounterDepth - CounterTopMargin;
        float halfWidth = FondaSceneBuilder.CounterWidth / 2f;

        foreach (var (name, xz) in points)
        {
            float reach = xz.magnitude;
            if (reach >= FondaSceneBuilder.MaxReach)
                problems.Add(name + " is " + reach.ToString("F3") + " m from the player (limit " + FondaSceneBuilder.MaxReach + " m).");
            if (Mathf.Abs(xz.x) > halfWidth || xz.y < minZ || xz.y > maxZ)
                problems.Add(name + " at " + xz + " is outside the counter top.");
        }

        for (int d = 0; d < Ingredients.Length; d++)
            for (int s = 0; s < SlotCount; s++)
            {
                float dist = Vector2.Distance(DispenserXZ(d), SlotXZ(s));
                if (dist < MinDispenserToSlot)
                    problems.Add("dispenser " + Ingredients[d].Id + " is only " + dist.ToString("F3") + " m from slot " + s +
                                 " (minimum " + MinDispenserToSlot + " m).");
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

        var recipe = CreateOrUpdateRecipe(new[] { data["arroz"], data["huevo"] });
        BuildTray(root, recipe, errors);
        BuildDispensers(root, prefabs);
        AssetDatabase.SaveAssets();
    }

    // ---- Data assets (S1) -----------------------------------------------------------------

    static IngredientData CreateOrUpdateIngredientData(IngredientDef def)
    {
        var path = IngredientDataFolder + "/" + def.DisplayName + ".asset";
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

    static RecipeData CreateOrUpdateRecipe(IngredientData[] required)
    {
        var asset = AssetDatabase.LoadAssetAtPath<RecipeData>(RecipeAssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<RecipeData>();
            AssetDatabase.CreateAsset(asset, RecipeAssetPath);
        }
        var so = new SerializedObject(asset);
        so.FindProperty("displayName").stringValue = "Arroz con huevo";
        var list = so.FindProperty("ingredients");
        list.arraySize = required.Length;
        for (int i = 0; i < required.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = required[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        return asset;
    }

    // ---- Ingredient prefabs (S2) ----------------------------------------------------------

    static GameObject CreateOrUpdatePrefab(IngredientDef def, IngredientData data)
    {
        var go = GameObject.CreatePrimitive(def.Shape);
        go.name = def.DisplayName;
        go.transform.localScale = def.Scale;
        go.GetComponent<Renderer>().sharedMaterial = FondaSceneBuilder.GetOrCreateMaterial("Ingredient_" + def.Id, def.Color);

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        go.AddComponent<XRGrabInteractable>();
        var ingredient = go.AddComponent<Ingredient>();
        var so = new SerializedObject(ingredient);
        so.FindProperty("data").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        var path = PrefabFolder + "/" + def.DisplayName + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ---- Tray (S4, S5) --------------------------------------------------------------------

    static void BuildTray(Transform root, RecipeData recipe, List<string> errors)
    {
        float top = FondaSceneBuilder.CounterHeight;
        var trayMat = FondaSceneBuilder.GetOrCreateMaterial("Tray", new Color(0.85f, 0.85f, 0.80f));
        var slotMat = FondaSceneBuilder.GetOrCreateMaterial("TraySlotMarker", new Color(0.40f, 0.40f, 0.45f));

        var tray = new GameObject("Tray");
        tray.transform.SetParent(root, false);
        tray.transform.localPosition = new Vector3(0f, top, SlotZ);

        float baseWidth = (SlotCount - 1) * SlotSpacing + 0.18f;
        var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseGo.name = "TrayBase";
        baseGo.transform.SetParent(tray.transform, false);
        baseGo.transform.localScale = new Vector3(baseWidth, TrayBaseHeight, 0.2f);
        baseGo.transform.localPosition = new Vector3(0f, TrayBaseHeight / 2f, 0f);
        var baseRenderer = baseGo.GetComponent<Renderer>();
        baseRenderer.sharedMaterial = trayMat;

        var slots = new XRSocketInteractor[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var slotGo = new GameObject("Slot " + (i + 1));
            slotGo.transform.SetParent(tray.transform, false);
            slotGo.transform.localPosition = new Vector3(SlotXZ(i).x, SlotLift, 0f);

            var trigger = slotGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = SocketRadius;

            var socket = slotGo.AddComponent<XRSocketInteractor>();
            socket.showInteractableHoverMeshes = false;
            slots[i] = socket;

            // Visual marker: a flat disc on the tray surface (no collider).
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "SlotMarker";
            marker.transform.SetParent(slotGo.transform, false);
            marker.transform.localPosition = new Vector3(0f, TrayBaseHeight - SlotLift + 0.002f, 0f);
            marker.transform.localScale = new Vector3(0.09f, 0.002f, 0.09f);
            marker.GetComponent<Renderer>().sharedMaterial = slotMat;
            Object.DestroyImmediate(marker.GetComponent<Collider>());
        }

        var trayComponent = tray.AddComponent<Tray>();
        var so = new SerializedObject(trayComponent);
        so.FindProperty("order").objectReferenceValue = recipe;
        so.FindProperty("flashRenderer").objectReferenceValue = baseRenderer;
        var slotsProp = so.FindProperty("slots");
        slotsProp.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        if (recipe == null || recipe.Ingredients.Count > SlotCount)
            errors.Add("Tray recipe is missing or needs more than " + SlotCount + " slots.");
    }

    // ---- Dispensers (S3) ------------------------------------------------------------------

    static void BuildDispensers(Transform root, Dictionary<string, GameObject> prefabs)
    {
        float top = FondaSceneBuilder.CounterHeight;
        var plateMat = FondaSceneBuilder.GetOrCreateMaterial("DispenserPlate", new Color(0.35f, 0.30f, 0.25f));

        for (int i = 0; i < Ingredients.Length; i++)
        {
            var def = Ingredients[i];
            var go = new GameObject("Dispenser_" + def.DisplayName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(DispenserXZ(i).x, top, DispenserXZ(i).y);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = "Plate";
            plate.transform.SetParent(go.transform, false);
            plate.transform.localPosition = new Vector3(0f, PlateHeight / 2f, 0f);
            plate.transform.localScale = new Vector3(0.12f, PlateHeight / 2f, 0.12f); // cylinder height is 2 units
            plate.GetComponent<Renderer>().sharedMaterial = plateMat;
            Object.DestroyImmediate(plate.GetComponent<Collider>()); // visual only; the counter is the support

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
