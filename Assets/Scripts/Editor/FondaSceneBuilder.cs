using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Builds the "Fonda" blockout scene: fixed XR rig facing a counter, no locomotion.
/// Run from the menu: Tools/Fonda/Build Fonda Scene. Safe to re-run (asks before overwriting).
/// All dimensions are in meters; the player origin is (0,0,0) facing +Z.
/// </summary>
public static class FondaSceneBuilder
{
    // ---- Paths ----------------------------------------------------------------------------
    const string ScenePath = "Assets/Scenes/Fonda.unity";
    const string MaterialsFolder = "Assets/Materials/Blockout";
    const string SamplesRoot = "Assets/Samples/XR Interaction Toolkit";
    const string RigPrefabName = "XR Origin (XR Rig)";

    // ---- Blockout dimensions (tune here) --------------------------------------------------
    const float FloorSize = 6f;

    internal const float CounterWidth = 1.6f;   // x
    internal const float CounterHeight = 0.9f;  // y
    internal const float CounterDepth = 0.6f;   // z
    internal const float CounterFrontZ = 0.3f;  // front edge (player side) distance from origin

    const float WallZ = -1.0f;
    const float WallWidth = 3f;
    const float WallHeight = 2.5f;
    const float WallThickness = 0.1f;

    const float CustomerDistanceZ = 1.3f;
    const float CustomerMarkerSize = 0.6f;

    // Ingredient zone on the counter top (x centered on the player; z measured from the player).
    // Reach check (S7, must stay < MaxReach horizontally from the origin):
    //   farthest corner = (IngredientHalfWidth, IngredientFarZ) = (0.25, 0.5)
    //   sqrt(0.25^2 + 0.5^2) = sqrt(0.3125) ~= 0.559 m < 0.6 m
    const float IngredientHalfWidth = 0.25f;
    const float IngredientNearZ = CounterFrontZ; // 0.3
    const float IngredientFarZ = 0.5f;
    internal const float MaxReach = 0.6f;

    const float MarkerLift = 0.003f; // avoids z-fighting with the surface below

    // Locomotion providers to deactivate under the "Locomotion" child of the rig (Gravity stays on).
    // "Climb Teleport" is a child of "Climb", so deactivating "Climb" covers it.
    static readonly string[] LocomotionChildrenToDisable =
    {
        "Move", "Turn", "Teleportation", "Climb", "Grab Move", "Jump"
    };

    [MenuItem("Tools/Fonda/Build Fonda Scene")]
    public static void Build()
    {
        // Scene creation is not allowed in Play mode (NewScene throws InvalidOperationException).
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Fonda", "Sal del modo Play antes de construir la escena.", "OK");
            return;
        }

        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Fonda", "Ya existe " + ScenePath + ". Quieres sobrescribirla?", "Sobrescribir", "Cancelar"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string rigPath = FindRigPrefabPath();
        if (rigPath == null)
        {
            const string msg = "No se encontro el prefab '" + RigPrefabName + "' en " + SamplesRoot +
                               "/*/Starter Assets/Prefabs/. Importa el sample Starter Assets del XR Interaction Toolkit y vuelve a intentar.";
            Debug.LogError("[FondaSceneBuilder] " + msg);
            EditorUtility.DisplayDialog("Fonda", msg, "OK");
            return;
        }

        var reach = Mathf.Sqrt(IngredientHalfWidth * IngredientHalfWidth + IngredientFarZ * IngredientFarZ);
        if (reach >= MaxReach)
        {
            var msg = "La zona de ingredientes excede el alcance del brazo: " + reach.ToString("F3") + " m >= " + MaxReach + " m.";
            Debug.LogError("[FondaSceneBuilder] " + msg);
            EditorUtility.DisplayDialog("Fonda", msg, "OK");
            return;
        }

        var layoutProblems = FondaCoreLoopBuilder.ValidateLayout();
        if (layoutProblems.Count > 0)
        {
            var msg = "El layout del mostrador no cumple el alcance:\n- " + string.Join("\n- ", layoutProblems);
            Debug.LogError("[FondaSceneBuilder] " + msg);
            EditorUtility.DisplayDialog("Fonda", msg, "OK");
            return;
        }

        var errors = new List<string>();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateDirectionalLight();

        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // identity faces +Z

        EnsureInteractionManager();
        DisableLocomotion(rig, errors);
        var fondaRoot = BuildBlockout();
        FondaCoreLoopBuilder.Build(fondaRoot, errors);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        if (saved) AddToBuildSettingsAtIndexZero(ScenePath);

        Debug.Log("[FondaSceneBuilder] Scene " + (saved ? "saved: " : "NOT saved: ") + ScenePath +
                  "\n  rig prefab: " + rigPath +
                  "\n  ingredient zone farthest point: " + reach.ToString("F3") + " m (limit " + MaxReach + " m)" +
                  "\n  build settings: " + ScenePath + " at index 0" +
                  "\n  errors: " + errors.Count);

        if (!saved || errors.Count > 0)
            EditorUtility.DisplayDialog("Fonda", "Terminado con problemas. Revisa la consola:\n- " + string.Join("\n- ", errors), "OK");
    }

    // ---- Rig lookup -----------------------------------------------------------------------

    static string FindRigPrefabPath()
    {
        var regex = new Regex("^" + Regex.Escape(SamplesRoot) + "/([^/]+)/Starter Assets/Prefabs/" + Regex.Escape(RigPrefabName) + @"\.prefab$");
        string best = null;
        Version bestVersion = null;

        foreach (var guid in AssetDatabase.FindAssets("XR Origin t:Prefab", new[] { SamplesRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var m = regex.Match(path);
            if (!m.Success) continue;

            // Drop any suffix such as "-pre.1" so System.Version can parse "3.6.1".
            var numeric = Regex.Match(m.Groups[1].Value, @"^\d+(\.\d+){1,3}").Value;
            if (!Version.TryParse(numeric, out var version)) version = new Version(0, 0);

            if (bestVersion == null || version > bestVersion)
            {
                best = path;
                bestVersion = version;
            }
        }
        return best;
    }

    // ---- Scene objects --------------------------------------------------------------------

    static void CreateDirectionalLight()
    {
        var go = new GameObject("Directional Light");
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void EnsureInteractionManager()
    {
        // The Starter Assets rig does not ship an XRInteractionManager; add exactly one if none exists.
        if (UnityEngine.Object.FindObjectsByType<XRInteractionManager>().Length == 0)
            new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
    }

    // ---- S5: no locomotion ----------------------------------------------------------------

    static void DisableLocomotion(GameObject rig, List<string> errors)
    {
        var locomotion = rig.transform.Find("Locomotion");
        if (locomotion == null)
        {
            Fail(errors, "Child 'Locomotion' not found under the rig; locomotion NOT disabled.");
        }
        else
        {
            foreach (var childName in LocomotionChildrenToDisable)
            {
                var child = locomotion.Find(childName);
                if (child == null) { Fail(errors, "Locomotion child '" + childName + "' not found."); continue; }
                child.gameObject.SetActive(false);
            }
        }

        // Teleport interactors live on each controller and are driven by ControllerInputActionManager.
        // That manager re-activates its teleport interactor (and deactivates the near-far grab interactor)
        // when the teleport-mode input fires, so deactivating the object alone is not enough.
        // On the scene instance we deactivate the interactor and clear the manager's teleport references
        // (interactor, mode, cancel). Every use of those fields in the manager is null-guarded, so this
        // causes no NullReferenceException and grabbing is never interrupted by a teleport aim.
        int managers = 0;
        foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb.GetType().Name != "ControllerInputActionManager") continue;
            managers++;

            var so = new SerializedObject(mb);
            var interactorProp = so.FindProperty("m_TeleportInteractor");
            var modeProp = so.FindProperty("m_TeleportMode");
            var cancelProp = so.FindProperty("m_TeleportModeCancel");
            if (interactorProp == null || modeProp == null || cancelProp == null)
            {
                Fail(errors, "ControllerInputActionManager on '" + mb.gameObject.name + "' lacks expected teleport fields.");
                continue;
            }

            var teleport = interactorProp.objectReferenceValue as Component;
            if (teleport == null) Fail(errors, "No teleport interactor referenced on '" + mb.gameObject.name + "'.");
            else teleport.gameObject.SetActive(false);

            interactorProp.objectReferenceValue = null;
            modeProp.objectReferenceValue = null;
            cancelProp.objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (managers != 2)
            Fail(errors, "Expected 2 ControllerInputActionManager components (left/right), found " + managers + ".");
    }

    static void Fail(List<string> errors, string message)
    {
        errors.Add(message);
        Debug.LogError("[FondaSceneBuilder] " + message);
    }

    // ---- S6/S7: blockout ------------------------------------------------------------------

    static Transform BuildBlockout()
    {
        var floorMat = GetOrCreateMaterial("Floor", new Color(0.55f, 0.42f, 0.30f));
        var counterMat = GetOrCreateMaterial("Counter", new Color(0.72f, 0.52f, 0.30f));
        var wallMat = GetOrCreateMaterial("Wall", new Color(0.93f, 0.88f, 0.78f));
        var customerMat = GetOrCreateMaterial("CustomerMarker", new Color(0.25f, 0.55f, 0.90f));
        var ingredientMat = GetOrCreateMaterial("IngredientZoneMarker", new Color(0.30f, 0.75f, 0.35f));

        var root = new GameObject("Fonda").transform;

        // Floor (keeps its collider).
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root, false);
        floor.transform.localScale = new Vector3(FloorSize / 10f, 1f, FloorSize / 10f); // plane is 10x10 at scale 1
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        // Counter: front edge at z = CounterFrontZ, top at y = CounterHeight (keeps its collider).
        var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        counter.name = "Counter";
        counter.transform.SetParent(root, false);
        counter.transform.localScale = new Vector3(CounterWidth, CounterHeight, CounterDepth);
        counter.transform.localPosition = new Vector3(0f, CounterHeight / 2f, CounterFrontZ + CounterDepth / 2f);
        counter.GetComponent<Renderer>().sharedMaterial = counterMat;

        // Back wall behind the player.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "BackWall";
        wall.transform.SetParent(root, false);
        wall.transform.localScale = new Vector3(WallWidth, WallHeight, WallThickness);
        wall.transform.localPosition = new Vector3(0f, WallHeight / 2f, WallZ - WallThickness / 2f);
        wall.GetComponent<Renderer>().sharedMaterial = wallMat;

        // Customer spot: empty anchor + flat marker on the floor.
        var customerSpot = new GameObject("CustomerSpot").transform;
        customerSpot.SetParent(root, false);
        customerSpot.localPosition = new Vector3(0f, 0f, CustomerDistanceZ);
        CreateFlatMarker("CustomerSpotMarker", customerSpot, Vector3.up * MarkerLift,
                         new Vector2(CustomerMarkerSize, CustomerMarkerSize), customerMat);

        // Ingredient zone: empty anchor at the zone center on the counter top + flat marker.
        float zoneCenterZ = (IngredientNearZ + IngredientFarZ) / 2f;
        var zone = new GameObject("IngredientZone").transform;
        zone.SetParent(root, false);
        zone.localPosition = new Vector3(0f, CounterHeight, zoneCenterZ);
        CreateFlatMarker("IngredientZoneMarker", zone, Vector3.up * MarkerLift,
                         new Vector2(IngredientHalfWidth * 2f, IngredientFarZ - IngredientNearZ), ingredientMat);

        return root;
    }

    static void CreateFlatMarker(string name, Transform parent, Vector3 localOffset, Vector2 sizeXZ, Material mat)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localOffset;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lay the quad flat, facing up
        quad.transform.localScale = new Vector3(sizeXZ.x, sizeXZ.y, 1f);
        quad.GetComponent<Renderer>().sharedMaterial = mat;
        UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>()); // markers are visual only
    }

    // ---- Materials (A3) -------------------------------------------------------------------

    internal static Material GetOrCreateMaterial(string name, Color color)
    {
        var path = MaterialsFolder + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "Blockout");

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("[FondaSceneBuilder] Shader 'Universal Render Pipeline/Lit' not found; using Standard fallback.");
            shader = Shader.Find("Standard");
        }
        var mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            AssetDatabase.CreateFolder(parent, child);
    }

    // ---- Build settings (A2) --------------------------------------------------------------

    static void AddToBuildSettingsAtIndexZero(string scenePath)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == scenePath); // no duplicates on re-runs; other entries are kept
        list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }
}
