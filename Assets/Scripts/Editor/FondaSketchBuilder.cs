using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds "FondaBoceto": a static visual sketch of the curved-bar station using KayKit Restaurant Bits.
/// It follows the station layout in CLAUDE.md (concept v6). Nothing in it is interactive; gameplay lives in
/// the Fonda scene (FondaSceneBuilder / FondaCoreLoopBuilder), which this builder never touches.
/// Run from the menu: Tools/Fonda/Build Sketch Scene (Boceto). The scene is built additively, saved and closed,
/// so the scene currently open in the editor is left as it was. Two preview renders are written to Temp/.
/// Units are meters; the player is at the origin facing +Z; angles are measured from +Z, positive to the right.
/// </summary>
public static class FondaSketchBuilder
{
    const string ScenePath = "Assets/Scenes/FondaBoceto.unity";
    const string ModelsFolder = "Assets/ThirdParty/KayKit_RestaurantBits";
    const string AtlasPath = ModelsFolder + "/restaurantbits_texture.png";
    const string GeneratedFolder = "Assets/Art/Sketch";
    const string FrontPreviewPath = "Temp/FondaBoceto_front.png";
    const string TopPreviewPath = "Temp/FondaBoceto_top.png";
    const string DiningPreviewPath = "Temp/FondaBoceto_dining.png";
    const string KitchenPreviewPath = "Temp/FondaBoceto_kitchen.png";
    const string WallsPreviewPath = "Temp/FondaBoceto_walls.png";

    // ---- Layout (straight bar, chosen by the author over the GDD's curved bar) -------------
    // Player at the origin facing +z. The bar runs along x; the side trays are pulled toward the
    // centre (x = ±0.34) so their outer slot stays within MaxReach although the seats are 0.60 m apart.
    // Same positions as FondaCoreLoopBuilder, so the sketch shows the playable layout.
    // Bar and room sizes are shared with the playable scene (FondaSceneBuilder owns them).
    const float BarHeight = FondaSceneBuilder.CounterHeight;
    const float BarHalfLength = FondaSceneBuilder.BarHalfLength;
    const float BarTopNearZ = FondaSceneBuilder.BarNearZ;   // edge on the grandma's side
    const float BarTopFarZ = FondaSceneBuilder.BarFarZ;     // edge on the customers' side
    const float BarBodyNearZ = 0.18f;
    const float BarBodyFarZ = 0.58f;   // the top overhangs the body by 6 cm toward the customers
    const float MaxReach = FondaSceneBuilder.MaxReach;

    const float IngredientZ = 0.24f;
    static readonly (string model, float x)[] Ingredients =
    {
        ("food_ingredient_lettuce_slice", -0.40f),
        ("food_ingredient_tomato_slice", -0.24f),
        ("food_ingredient_cheese_slice", -0.08f),
        ("food_ingredient_burger_cooked", 0.08f),
        ("food_ingredient_steak_pieces", 0.24f),
        ("food_ingredient_potato_chopped", 0.40f),
    };

    const float TrayZ = 0.40f;
    static readonly float[] TrayX = { -0.34f, 0f, 0.34f };
    static readonly float[] SeatX = { -0.60f, 0f, 0.60f };
    const float SeatZ = 0.95f;
    internal const float StoolHeight = 0.62f;   // cushion top; high enough for the shrunk chibi customers to clear the bar
    static readonly Vector2 WaitingSpot = new Vector2(-0.35f, 1.65f);
    // Room: 8 m wide, 5.2 m deep (kitchen wall behind the grandma to the dining wall behind the customers).
    const float BackWallZ = FondaSceneBuilder.BackWallZ;
    const float KitchenWallZ = FondaSceneBuilder.KitchenWallZ;
    const float RoomHalfWidth = FondaSceneBuilder.RoomHalfWidth;
    const float WallHeight = 2.80f;
    // KayKit kitchen pieces face +Z in model space (toward the grandma when yaw is 0); flip here if they face away.
    const float KitchenYaw = 0f;
    const float ClockHeight = 2.20f;

    // Bar ends: sauces on the left and tip jar on the right within reach; recipe book out of reach on purpose.
    static readonly Vector2[] SauceSpots = { new Vector2(-0.50f, 0.20f), new Vector2(-0.50f, 0.30f) };
    static readonly Vector2 TipJarSpot = new Vector2(0.53f, 0.24f);
    static readonly Vector2 RecipeBookSpot = new Vector2(-0.85f, 0.40f);

    // No ceiling for now (author's request): the camera clears to a light sky so the room reads as open.
    static readonly Color SkyColor = new Color(0.72f, 0.85f, 0.95f);

    enum Fit { Height, MaxXZ }

    static List<string> errors;
    static Material atlas;
    static Material wallAtlas;

    // Wall colors (chosen by the author): warm white wall over a blue skirting.
    static readonly Color WallColor = new Color(0.96f, 0.94f, 0.89f);
    static readonly Color SkirtingColor = new Color(0.17f, 0.36f, 0.64f);
    // Atlas swatches the KayKit walls use (read from the OBJ UVs): green lower band and beige upper band.
    // The door and window frames use u 0.40-0.46, outside both rectangles.
    static readonly Rect SkirtingSwatch = Rect.MinMaxRect(0.26f, 0.52f, 0.37f, 0.70f);
    static readonly Rect WallSwatch = Rect.MinMaxRect(0.51f, 0.52f, 0.62f, 0.70f);
    const string WallAtlasPath = GeneratedFolder + "/Wall_Atlas.png";
    static ulong sceneMask;

    static void LoadMaterials()
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Sketch");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (texture == null) errors.Add("Atlas not found: " + AtlasPath);
        atlas = GetOrCreateMaterial("KayKit_Atlas", Color.white, texture);
        wallAtlas = texture != null ? GetOrCreateMaterial("Sketch_WallAtlas", Color.white, RecolorWallAtlas(texture)) : atlas;
    }

    /// <summary>
    /// Dresses the playable Fonda scene with the sketch look: lighting, room, kitchen, dining props and the bar body.
    /// Gameplay pieces (dispensers, trays, seats, customers, clock, tip jar) and colliders come from the Fonda builders.
    /// </summary>
    internal static void BuildDressing(Transform root, List<string> problems)
    {
        errors = new List<string>();
        LoadMaterials();
        SetupLighting();
        var dressing = Group("Dressing (KayKit)", root);
        BuildRoom(dressing, false); // the playable scene has its own working wall clock
        BuildKitchen(dressing);
        BuildDining(dressing);
        BuildBar(dressing);
        foreach (var e in errors) problems.Add("[dressing] " + e);
    }

    /// <summary>KayKit stool with a bar-stool footprint at counter-stool height; returns the seat height.</summary>
    internal static float PlaceStool(Transform parent, Vector3 at)
    {
        if (atlas == null) { errors = new List<string>(); LoadMaterials(); }
        var stool = Place("chair_stool", parent, at, 0f, 0.36f, Fit.MaxXZ);
        if (stool == null) return StoolHeight;
        StretchY(stool, StoolHeight);
        stool.name = "Stool";
        return Measure(stool).max.y;
    }

    [MenuItem("Tools/Fonda/Build Sketch Scene (Boceto)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Fonda", "Sal del modo Play antes de construir el boceto.", "OK");
            return;
        }

        // The sketch is generated. If it is the scene being viewed, or the editor only has an untitled scene,
        // the sketch is built in place of it and stays open (framed); otherwise it is built additively and closed.
        var openSketch = SceneManager.GetSceneByPath(ScenePath);
        bool replaceOpen = openSketch.isLoaded || string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
        if (openSketch.isLoaded && openSketch.isDirty &&
            !EditorUtility.DisplayDialog("Fonda", "El boceto abierto tiene cambios a mano. Se pierden al reconstruirlo.", "Reconstruir", "Cancelar"))
            return;
        if (replaceOpen && SceneManager.sceneCount > 1 && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        errors = new List<string>();
        LoadMaterials();

        var previousActive = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, replaceOpen ? NewSceneMode.Single : NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        // The previews must show only this scene, not whatever else is open in the editor.
        sceneMask = EditorSceneManager.CalculateAvailableSceneCullingMask();
        EditorSceneManager.SetSceneCullingMask(scene, sceneMask);

        SetupLighting();
        var root = new GameObject("FondaBoceto").transform;

        BuildRoom(root);
        BuildKitchen(root);
        BuildDining(root);
        BuildBar(root);
        BuildIngredients(root);
        BuildTrays(root);
        BuildSeatsAndCustomers(root);
        BuildBarProps(root);
        var camera = CreateSketchCamera();
        CheckReach(root);

        Directory.CreateDirectory("Temp");
        RenderPreview(camera, FrontPreviewPath);
        RenderTopPreview(TopPreviewPath);
        RenderFromPoint(DiningPreviewPath, new Vector3(0.8f, 1.75f, 3.2f), new Vector3(0f, 0.9f, -0.3f));
        RenderFromPoint(WallsPreviewPath, new Vector3(0f, 1.3f, -1.0f), new Vector3(0f, 1.9f, BackWallZ));
        RenderFromPoint(KitchenPreviewPath, new Vector3(0.2f, 1.1f, 0.1f), new Vector3(0f, 0.5f, -1.4f));

        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        if (replaceOpen)
        {
            FrameSceneView();
        }
        else
        {
            if (previousActive.IsValid()) SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(scene, true);
        }

        Debug.Log("[FondaSketchBuilder] Scene " + (saved ? "saved: " : "NOT saved: ") + ScenePath +
                  "\n  previews: " + FrontPreviewPath + ", " + TopPreviewPath + ", " + DiningPreviewPath +
                  "\n  errors: " + errors.Count + (errors.Count > 0 ? "\n  - " + string.Join("\n  - ", errors) : ""));
    }

    // ---- Room -------------------------------------------------------------------------------

    static void SetupLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.58f, 0.52f, 0.46f);

        // Warm key light without real-time shadows (art direction: baked, no real-time shadows).
        var go = new GameObject("Warm Light");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.9f, 0.76f);
        light.intensity = 1.15f;
        light.shadows = LightShadows.None;
        go.transform.rotation = Quaternion.Euler(50f, -25f, 0f);

        // Soft fill from the opposite side so the kitchen fronts facing the customers are not black.
        var fill = new GameObject("Fill Light").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(1f, 0.95f, 0.88f);
        fill.intensity = 0.45f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
    }

    static void BuildRoom(Transform root, bool withClock = true)
    {
        var room = Group("Room", root);

        // Floor tiles, 1.2 m each, over the whole room (kitchen behind the grandma up to the dining wall).
        // The tiles are 0.15 m thick, so they are sunk until their top is at y = 0, where everything else rests.
        for (float x = -RoomHalfWidth + 0.4f; x <= RoomHalfWidth; x += 1.2f)
        for (float z = KitchenWallZ + 0.6f; z <= BackWallZ + 0.6f; z += 1.2f)
        {
            var tile = Place("floor_kitchen", room, new Vector3(x, 0f, z), 0f, 1.2f, Fit.MaxXZ, true);
            if (tile != null) tile.transform.position -= Vector3.up * Measure(tile).max.y;
        }

        // Dining wall behind the customers: doorway on the left, plain in the middle (clock), window on the right.
        // Front and back walls are four 2 m pieces each.
        WallPiece("wall_doorway", room, new Vector3(-3f, 0f, BackWallZ), true, Inward.MinusZ);
        WallPiece("wall", room, new Vector3(-1f, 0f, BackWallZ), true, Inward.MinusZ);
        WallPiece("wall", room, new Vector3(1f, 0f, BackWallZ), true, Inward.MinusZ);
        WallPiece("wall_window_closed", room, new Vector3(3f, 0f, BackWallZ), true, Inward.MinusZ);

        // Kitchen wall behind the grandma, and the two side walls (windows on the dining half).
        for (float x = -3f; x <= 3f; x += 2f)
            WallPiece("wall", room, new Vector3(x, 0f, KitchenWallZ), true, Inward.PlusZ);
        var sideModels = new[] { "wall", "wall_window_closed", "wall_window_closed" };
        float sideLength = (BackWallZ - KitchenWallZ) / sideModels.Length;
        for (int i = 0; i < sideModels.Length; i++)
        {
            float z = KitchenWallZ + sideLength * (i + 0.5f);
            WallPiece(sideModels[i], room, new Vector3(-RoomHalfWidth, 0f, z), false, Inward.PlusX, sideLength);
            WallPiece(sideModels[i], room, new Vector3(RoomHalfWidth, 0f, z), false, Inward.MinusX, sideLength);
        }

        if (!withClock) return;

        // Wall clock: a flat disc facing the player with two hands.
        var white = GetOrCreateMaterial("Sketch_ClockFace", new Color(0.96f, 0.93f, 0.85f), null);
        var dark = GetOrCreateMaterial("Sketch_Dark", new Color(0.18f, 0.14f, 0.12f), null);
        var clock = Primitive(PrimitiveType.Cylinder, "Clock", room, new Vector3(0f, ClockHeight, BackWallZ - 0.02f),
            new Vector3(0.34f, 0.015f, 0.34f), white);
        clock.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        Primitive(PrimitiveType.Cube, "Clock Hour Hand", room, new Vector3(0f, ClockHeight + 0.05f, BackWallZ - 0.04f),
            new Vector3(0.02f, 0.10f, 0.01f), dark);
        var minute = Primitive(PrimitiveType.Cube, "Clock Minute Hand", room, new Vector3(0.05f, ClockHeight, BackWallZ - 0.045f),
            new Vector3(0.12f, 0.015f, 0.01f), dark);
        minute.transform.localPosition = new Vector3(0.06f, ClockHeight, BackWallZ - 0.045f);
    }

    enum Inward { PlusX, MinusX, PlusZ, MinusZ }

    /// <summary>
    /// Places a wall piece <paramref name="length"/> long (2 m by default) and WallHeight tall. <paramref name="at"/> is a point on the room-facing surface:
    /// the piece is centered on it along the wall and its room-facing side is moved onto that plane.
    /// </summary>
    static void WallPiece(string model, Transform parent, Vector3 at, bool alongX, Inward inward, float length = 2f)
    {
        var wall = Place(model, parent, at, 0f, 2f, Fit.MaxXZ);
        if (wall == null) return;
        var b = Measure(wall);
        if (alongX != (b.size.x >= b.size.z))
            wall.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * wall.transform.rotation;
        StretchAxis(wall, Vector3.up, WallHeight);
        StretchAxis(wall, alongX ? Vector3.right : Vector3.forward, length);
        UseMaterial(wall, wallAtlas);

        b = Measure(wall);
        var shift = new Vector3(at.x - b.center.x, -b.min.y, at.z - b.center.z);
        switch (inward)
        {
            case Inward.PlusX: shift.x = at.x - b.max.x; break;
            case Inward.MinusX: shift.x = at.x - b.min.x; break;
            case Inward.PlusZ: shift.z = at.z - b.max.z; break;
            case Inward.MinusZ: shift.z = at.z - b.min.z; break;
        }
        wall.transform.position += shift;
    }

    static void UseMaterial(GameObject go, Material mat)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>
    /// Writes a copy of the KayKit atlas where the two wall swatches are tinted to the chosen wall colors.
    /// Each pixel keeps its brightness relative to the swatch, so the atlas gradient (shading) is preserved.
    /// Only the wall material uses this copy; every other model keeps the original atlas.
    /// </summary>
    static Texture2D RecolorWallAtlas(Texture2D source)
    {
        var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        Tint(copy, SkirtingSwatch, SkirtingColor);
        Tint(copy, WallSwatch, WallColor);
        copy.Apply();

        File.WriteAllBytes(WallAtlasPath, copy.EncodeToPNG());
        Object.DestroyImmediate(copy);
        AssetDatabase.ImportAsset(WallAtlasPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(WallAtlasPath);
    }

    static void Tint(Texture2D tex, Rect uvRect, Color target)
    {
        int x0 = Mathf.FloorToInt(uvRect.xMin * tex.width), x1 = Mathf.CeilToInt(uvRect.xMax * tex.width);
        int y0 = Mathf.FloorToInt(uvRect.yMin * tex.height), y1 = Mathf.CeilToInt(uvRect.yMax * tex.height);
        var reference = tex.GetPixel((x0 + x1) / 2, (y0 + y1) / 2).grayscale;
        if (reference <= 1e-4f) return;
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            var p = tex.GetPixel(x, y);
            float k = Mathf.Clamp(p.grayscale / reference, 0.6f, 1.3f);
            tex.SetPixel(x, y, new Color(Mathf.Clamp01(target.r * k), Mathf.Clamp01(target.g * k), Mathf.Clamp01(target.b * k), p.a));
        }
    }

    [MenuItem("Tools/Fonda/Open Sketch Scene (Boceto)")]
    public static void OpenAndFrame()
    {
        if (!File.Exists(ScenePath))
        {
            EditorUtility.DisplayDialog("Fonda", "Todavia no existe " + ScenePath + ". Usa primero Build Sketch Scene (Boceto).", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        FrameSceneView();
    }

    static void FrameSceneView()
    {
        // Look down at the whole room from above the dining side, so the bar, kitchen and walls all fit.
        var view = SceneView.lastActiveSceneView;
        if (view == null) view = EditorWindow.GetWindow<SceneView>();
        view.orthographic = false;
        view.LookAt(new Vector3(0f, 0.9f, 1.0f), Quaternion.Euler(40f, 0f, 0f), 5.5f);
        view.Repaint();
    }

    /// <summary>Scales only along world Y until the object is <paramref name="height"/> tall, keeping its base in place.</summary>
    static void StretchY(GameObject go, float height)
    {
        float baseY = Measure(go).min.y;
        StretchAxis(go, Vector3.up, height);
        go.transform.position += Vector3.up * (baseY - Measure(go).min.y);
    }

    /// <summary>Scales the object along one world axis until its bounds measure <paramref name="size"/> on it.</summary>
    static void StretchAxis(GameObject go, Vector3 worldAxis, float size)
    {
        var b = Measure(go);
        float current = Mathf.Abs(Vector3.Dot(b.size, worldAxis));
        if (current <= 1e-5f) return;
        // The model may carry an import rotation, so map the world axis to its local axis.
        var local = go.transform.InverseTransformDirection(worldAxis);
        var axis = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        float k = size / current;
        go.transform.localScale = Vector3.Scale(go.transform.localScale, Vector3.one + axis * (k - 1f));
    }

    /// <summary>Places a model stretched to fill a box of <paramref name="size"/> (world axes), resting on at.y.</summary>
    static GameObject PlaceBox(string model, Transform parent, Vector3 at, float yaw, Vector3 size)
    {
        var go = Place(model, parent, at, yaw, size.y, Fit.Height);
        if (go == null) return null;
        StretchAxis(go, Vector3.right, size.x);
        StretchAxis(go, Vector3.forward, size.z);
        var b = Measure(go);
        go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        return go;
    }

    // ---- Kitchen behind the grandma and dining room dressing (decoration only) ----------------

    static void BuildKitchen(Transform root)
    {
        var group = Group("Kitchen (behind the grandma)", root);
        const float depth = 0.65f, counterTop = 0.90f;
        float rowZ = KitchenWallZ + depth * 0.5f + 0.02f;

        // Plain KayKit cabinetry (1 unit = 0.9 m of height), squeezed to 0.9 m wide modules and a real
        // 0.65 m depth. The decorated versions are avoided: their props are stylised and far too big
        // once stretched, so the props are placed separately below at real-world sizes.
        var row = new (string model, float x, float height)[]
        {
            ("fridge_A_decorated", -2.25f, 1.85f),
            ("kitchencounter_straight_A_backsplash", -1.35f, 1.2f * counterTop),
            ("kitchencounter_sink_backsplash", -0.45f, 1.8f * counterTop),
            ("stove_multi", 0.45f, 1.2f * counterTop),
            ("kitchencounter_straight_A_backsplash", 1.35f, 1.2f * counterTop),
            ("oven", 2.25f, 2.02f * counterTop),
        };
        foreach (var (model, x, height) in row)
            PlaceBox(model, group, new Vector3(x, 0f, rowZ), KitchenYaw, new Vector3(0.90f, height, depth));

        // Props at real sizes (meters, largest horizontal side).
        float propZ = rowZ + 0.05f;
        Place("dishrack_plates", group, new Vector3(1.20f, counterTop, propZ), KitchenYaw, 0.40f, Fit.MaxXZ);
        for (int i = 0; i < 3; i++)
            Place("plate", group, new Vector3(1.62f, counterTop + 0.022f * i, propZ), KitchenYaw, 0.24f, Fit.MaxXZ);
        Place("pot_A", group, new Vector3(0.27f, 1.2f * counterTop, propZ), KitchenYaw, 0.30f, Fit.MaxXZ);
        Place("pan_A", group, new Vector3(0.65f, 1.2f * counterTop, propZ), KitchenYaw, 0.30f, Fit.MaxXZ);

        // Wall units share one line: hood over the stove, cabinets over the counters and the sink.
        const float wallUnitBottom = 1.50f, wallUnitDepth = 0.36f;
        float wallUnitZ = KitchenWallZ + wallUnitDepth * 0.5f + 0.02f;
        PlaceBox("extractorhood", group, new Vector3(0.45f, wallUnitBottom, KitchenWallZ + 0.30f), KitchenYaw, new Vector3(0.90f, 0.85f, 0.55f));
        foreach (float x in new[] { -1.35f, -0.45f, 1.35f })
            PlaceBox("kitchencabinet", group, new Vector3(x, wallUnitBottom, wallUnitZ), KitchenYaw, new Vector3(0.90f, 0.70f, wallUnitDepth));
    }

    static void BuildDining(Transform root)
    {
        var group = Group("Dining room", root);

        foreach (float x in new[] { -RoomHalfWidth + 0.22f, RoomHalfWidth - 0.22f })
        {
            var pillar = Place("pillar_A", group, new Vector3(x, 0f, BackWallZ - 0.22f), 0f, WallHeight, Fit.Height);
            if (pillar != null) StretchAxis(pillar, Vector3.right, 0.4f);
        }

        // Produce crates by the right window, a menu board next to the door.
        float cx = RoomHalfWidth - 0.55f, cz = BackWallZ - 0.45f;
        var crate = Place("crate_tomatoes", group, new Vector3(cx, 0f, cz), 12f, 0.45f, Fit.MaxXZ);
        Place("crate_potatoes", group, OnTop(crate, new Vector3(cx, 0.4f, cz)), -8f, 0.45f, Fit.MaxXZ);
        Place("crate_lettuce", group, new Vector3(cx - 0.5f, 0f, cz + 0.13f), 4f, 0.45f, Fit.MaxXZ);
        Place("crate_onions", group, new Vector3(cx + 0.15f, 0f, cz - 0.5f), -15f, 0.42f, Fit.MaxXZ);
        Place("menu", group, new Vector3(-1.9f, 0f, BackWallZ - 0.3f), 180f, 1.1f, Fit.Height);
    }

    // ---- Straight bar -----------------------------------------------------------------------

    static void BuildBar(Transform root)
    {
        var group = Group("Bar (straight)", root);
        var wood = GetOrCreateMaterial("Sketch_BarWood", new Color(0.55f, 0.33f, 0.20f), null);
        var top = GetOrCreateMaterial("Sketch_BarTop", new Color(0.86f, 0.74f, 0.55f), null);
        var metal = GetOrCreateMaterial("Sketch_Metal", new Color(0.62f, 0.62f, 0.60f), null);
        const float topThickness = 0.04f;
        float bodyHeight = BarHeight - topThickness;

        // Body: three KayKit counters with their drawers facing the grandma (yaw 180).
        const int modules = 3;
        float moduleWidth = BarHalfLength * 2f / modules;
        float bodyDepth = BarBodyFarZ - BarBodyNearZ;
        for (int i = 0; i < modules; i++)
        {
            float x = -BarHalfLength + moduleWidth * (i + 0.5f);
            PlaceBox("kitchencounter_straight_A", group, new Vector3(x, 0f, (BarBodyNearZ + BarBodyFarZ) * 0.5f),
                KitchenYaw + 180f, new Vector3(moduleWidth, bodyHeight, bodyDepth));
        }

        // Closed wooden front on the customers' side (the back of a KayKit counter is meant to face a wall).
        const float panel = 0.025f;
        Primitive(PrimitiveType.Cube, "Bar Front", group,
            new Vector3(0f, bodyHeight * 0.5f, BarBodyFarZ + panel * 0.5f),
            new Vector3(BarHalfLength * 2f, bodyHeight, panel), wood);

        // Thick top overhanging toward the customers, and a footrest rail under it.
        Primitive(PrimitiveType.Cube, "Bar Top", group,
            new Vector3(0f, BarHeight - topThickness * 0.5f, (BarTopNearZ + BarTopFarZ) * 0.5f),
            new Vector3(BarHalfLength * 2f + 0.04f, topThickness, BarTopFarZ - BarTopNearZ), top);
        Primitive(PrimitiveType.Cube, "Footrest Rail", group,
            new Vector3(0f, 0.22f, BarBodyFarZ + 0.14f),
            new Vector3(BarHalfLength * 2f - 0.10f, 0.03f, 0.03f), metal);
    }

    // ---- Counter contents -------------------------------------------------------------------

    static void BuildIngredients(Transform root)
    {
        var group = Group("Ingredients (6)", root);
        foreach (var (model, x) in Ingredients)
        {
            var at = new Vector3(x, BarHeight, IngredientZ);
            var plate = Place("plate_small", group, at, 0f, 0.13f, Fit.MaxXZ);
            Place(model, group, OnTop(plate, at), 0f, 0.08f, Fit.MaxXZ);
        }
    }

    static void BuildTrays(Transform root)
    {
        // Same example orders as concept v6: bun + medallón, bowl of fries, bare bun base.
        var group = Group("Trays (3)", root);
        for (int i = 0; i < TrayX.Length; i++)
        {
            const float angle = 0f;
            var at = new Vector3(TrayX[i], BarHeight, TrayZ);
            var tray = Place("plate", group, at, angle, 0.22f, Fit.MaxXZ);
            var onTray = OnTop(tray, at);
            if (i == 1)
            {
                var bowl = Place("bowl", group, onTray, angle, 0.13f, Fit.MaxXZ);
                Place("food_ingredient_potato_chopped", group, onTray + Vector3.up * 0.02f, angle, 0.08f, Fit.MaxXZ);
                if (bowl != null) bowl.name = "Base: tazón";
            }
            else
            {
                var bun = Place("food_ingredient_bun_bottom", group, onTray, angle, 0.10f, Fit.MaxXZ);
                if (bun != null) bun.name = "Base: pan";
                if (i == 0) Place("food_ingredient_burger_cooked", group, OnTop(bun, onTray), angle, 0.09f, Fit.MaxXZ);
            }
        }
    }

    static void BuildBarProps(Transform root)
    {
        var group = Group("Bar Props", root);
        Place("ketchup", group, OnBar(SauceSpots[0]), 0f, 0.18f, Fit.Height);
        Place("mustard", group, OnBar(SauceSpots[1]), 0f, 0.18f, Fit.Height);
        var jar = Place("jar_A_medium", group, OnBar(TipJarSpot), 0f, 0.15f, Fit.Height);
        if (jar != null) jar.name = "Tip Jar";
        // The book faces the grandma.
        float bookYaw = Mathf.Atan2(RecipeBookSpot.x, RecipeBookSpot.y) * Mathf.Rad2Deg + 180f;
        var book = Place("menu", group, OnBar(RecipeBookSpot), bookYaw, 0.20f, Fit.MaxXZ);
        if (book != null) book.name = "Recipe Book";
    }

    // ---- Seats and placeholder customers ----------------------------------------------------

    static void BuildSeatsAndCustomers(Transform root)
    {
        var group = Group("Seats (3)", root);
        var customers = Group("Customers (sketch)", root);

        // One of each look to show the visual variety: worker with helmet, grandpa, child; office worker waiting.
        var looks = new[]
        {
            (name: "Trabajador de obra", shirt: new Color(0.95f, 0.55f, 0.15f), skin: new Color(0.55f, 0.38f, 0.26f), scale: 1.05f, hat: new Color(0.98f, 0.82f, 0.1f)),
            (name: "Abuelito", shirt: new Color(0.45f, 0.55f, 0.45f), skin: new Color(0.93f, 0.76f, 0.62f), scale: 0.95f, hat: new Color(0.85f, 0.85f, 0.85f)),
            (name: "Niño", shirt: new Color(0.25f, 0.5f, 0.9f), skin: new Color(0.78f, 0.58f, 0.42f), scale: 0.7f, hat: Color.clear),
        };

        for (int i = 0; i < SeatX.Length; i++)
        {
            const float angle = 0f;
            var at = new Vector3(SeatX[i], 0f, SeatZ);
            // The KayKit stool is low and wide; give it a bar-stool footprint, then raise it to counter-stool
            // height (cushion top at StoolHeight).
            var stool = Place("chair_stool", group, at, angle, 0.36f, Fit.MaxXZ);
            if (stool != null) StretchY(stool, StoolHeight);
            float seatY = stool != null ? Measure(stool).max.y : StoolHeight;
            var l = looks[i];
            Figure(customers, l.name, at, angle + 180f, seatY, true, l.scale, l.shirt, l.skin, l.hat);
        }

        var wait = new Vector3(WaitingSpot.x, 0f, WaitingSpot.y);
        Figure(customers, "Oficinista (esperando de pie)", wait, 180f, 0f, false, 1f,
            new Color(0.2f, 0.22f, 0.3f), new Color(0.88f, 0.7f, 0.56f), Color.clear);
    }

    static void Figure(Transform parent, string name, Vector3 feet, float yaw, float baseY, bool seated, float scale,
        Color shirt, Color skin, Color hat)
    {
        var go = Group(name, parent);
        go.position = feet;
        go.rotation = Quaternion.Euler(0f, yaw, 0f);

        float bodyH = (seated ? 0.62f : 1.30f) * scale;
        float bodyW = 0.38f * scale;
        float head = 0.22f * scale;
        var shirtMat = GetOrCreateMaterial("Sketch_Shirt_" + ColorUtility.ToHtmlStringRGB(shirt), shirt, null);
        var skinMat = GetOrCreateMaterial("Sketch_Skin_" + ColorUtility.ToHtmlStringRGB(skin), skin, null);

        var body = Primitive(PrimitiveType.Capsule, "Body", go, Vector3.zero, new Vector3(bodyW, bodyH * 0.5f, bodyW * 0.8f), shirtMat);
        body.transform.localPosition = new Vector3(0f, baseY + bodyH * 0.5f, 0f);
        var h = Primitive(PrimitiveType.Sphere, "Head", go, Vector3.zero, Vector3.one * head, skinMat);
        h.transform.localPosition = new Vector3(0f, baseY + bodyH + head * 0.45f, 0f);

        if (hat.a > 0f)
        {
            var hatMat = GetOrCreateMaterial("Sketch_Hat_" + ColorUtility.ToHtmlStringRGB(hat), hat, null);
            var cap = Primitive(PrimitiveType.Sphere, "Hat", go, Vector3.zero, new Vector3(head * 1.1f, head * 0.55f, head * 1.1f), hatMat);
            cap.transform.localPosition = new Vector3(0f, baseY + bodyH + head * 0.8f, 0f);
        }
    }

    // ---- Camera, previews and checks --------------------------------------------------------

    static Camera CreateSketchCamera()
    {
        // Roughly the grandma's eye height, looking at the trays.
        var go = new GameObject("Sketch Camera (grandma's eyes)") { tag = "MainCamera" };
        go.transform.position = new Vector3(0f, 1.55f, -0.10f);
        go.transform.LookAt(new Vector3(0f, 0.95f, 0.9f));
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = SkyColor;
        return cam;
    }

    static void RenderPreview(Camera cam, string path)
    {
        var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        cam.overrideSceneCullingMask = sceneMask;

        var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
        if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
        {
            request.destination = rt;
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);
        }
        else
        {
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = previousTarget;
        }
        cam.overrideSceneCullingMask = 0;

        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = previous;

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
    }

    static void RenderTopPreview(string path)
    {
        var go = new GameObject("Top Preview Camera (temporary)");
        go.transform.SetPositionAndRotation(new Vector3(0f, 8f, (KitchenWallZ + BackWallZ) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = (BackWallZ - KitchenWallZ) * 0.5f + 0.1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        RenderPreview(cam, path);
        Object.DestroyImmediate(go);
    }

    /// <summary>Renders a preview from a temporary camera at <paramref name="eye"/> looking at <paramref name="target"/>.</summary>
    static void RenderFromPoint(string path, Vector3 eye, Vector3 target)
    {
        var go = new GameObject("Preview Camera (temporary)");
        go.transform.position = eye;
        go.transform.LookAt(target);
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = SkyColor;
        RenderPreview(cam, path);
        Object.DestroyImmediate(go);
    }

    static void CheckReach(Transform root)
    {
        foreach (var name in new[] { "Ingredients (6)", "Trays (3)" })
        {
            var group = root.Find(name);
            if (group == null) continue;
            foreach (Transform child in group) CheckOne(child);
        }
        var props = root.Find("Bar Props");
        if (props == null) return;
        foreach (Transform child in props)
            if (child.name != "Recipe Book") CheckOne(child); // the book is out of reach on purpose

        void CheckOne(Transform t)
        {
            var c = Measure(t.gameObject).center;
            float d = new Vector2(c.x, c.z).magnitude;
            if (d >= MaxReach) errors.Add("Out of reach: " + t.name + " at " + d.ToString("F2") + " m");
        }
    }

    // ---- Helpers ----------------------------------------------------------------------------

    static Vector3 OnBar(Vector2 xz) => new Vector3(xz.x, BarHeight, xz.y);

    static Vector3 OnTop(GameObject below, Vector3 fallback)
    {
        if (below == null) return fallback;
        return new Vector3(fallback.x, Measure(below).max.y, fallback.z);
    }

    /// <summary>
    /// Instantiates a KayKit model, scales it uniformly so the chosen dimension equals <paramref name="size"/>,
    /// centers it on <paramref name="at"/> in XZ and rests its lowest point on at.y.
    /// </summary>
    static GameObject Place(string model, Transform parent, Vector3 at, float yaw, float size, Fit fit, bool longSideOnX = false)
    {
        var path = ModelsFolder + "/" + model + ".fbx";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { errors.Add("Model not found: " + path); return null; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, true);
        go.transform.position = Vector3.zero;
        var baseRotation = prefab.transform.rotation;
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * baseRotation;

        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = atlas;
            r.sharedMaterials = mats;
        }

        var b = Measure(go);
        if (longSideOnX && b.size.z > b.size.x)
        {
            go.transform.rotation = Quaternion.Euler(0f, yaw + 90f, 0f) * baseRotation;
            b = Measure(go);
        }

        float dim = fit == Fit.Height ? b.size.y : Mathf.Max(b.size.x, b.size.z);
        if (dim <= 1e-5f) { errors.Add("Model has no size: " + model); return go; }
        go.transform.localScale = prefab.transform.localScale * (size / dim);

        b = Measure(go);
        go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        return go;
    }

    static Bounds Measure(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static Material GetOrCreateMaterial(string name, Color color, Texture2D texture)
    {
        var path = GeneratedFolder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        if (texture != null) mat.SetTexture("_BaseMap", texture);
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
    }
}
