using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Builds the "Fonda" scene: fixed XR rig behind a straight bar, no locomotion, dressed with the KayKit look of the
/// sketch scene (room, kitchen behind the grandma, dining wall behind the customers).
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

    // ---- Layout (GDD §5, "Distribución de la estación") ------------------------------------
    // Straight bar in front of the player, who stands at the origin facing +Z.
    internal const float CounterHeight = 0.9f;
    internal const float BarHalfLength = 1.05f;   // the bar runs from x = -1.05 to 1.05
    internal const float BarNearZ = 0.16f;        // edge on the grandma's side
    internal const float BarFarZ = 0.64f;         // edge on the customers' side
    internal const float MaxReach = 0.6f;

    // Room: 8 m wide, from the kitchen wall behind the grandma to the dining wall behind the customers.
    internal const float RoomHalfWidth = 4.0f;
    internal const float KitchenWallZ = -1.60f;
    internal const float BackWallZ = 3.60f;
    internal const float DoorCenterX = -3.0f;    // the doorway piece of the dining wall

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


        var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
        rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // identity faces +Z

        EnsureInteractionManager();
        DisableLocomotion(rig, errors);
        var fondaRoot = BuildBlockout();
        FondaSketchBuilder.BuildDressing(fondaRoot, errors);
        FondaCoreLoopBuilder.Build(fondaRoot, errors);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        if (saved) AddToBuildSettingsAtIndexZero(ScenePath);

        Debug.Log("[FondaSceneBuilder] Scene " + (saved ? "saved: " : "NOT saved: ") + ScenePath +
                  "\n  rig prefab: " + rigPath +
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

    // ---- S6/S7: physics blockout ----------------------------------------------------------
    // The visible room and bar come from FondaSketchBuilder.BuildDressing (KayKit pieces have no colliders).
    // Here only the invisible colliders the gameplay needs: a floor and the bar top that ingredients rest on.

    static Transform BuildBlockout()
    {
        var root = new GameObject("Fonda").transform;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor Collider";
        floor.transform.SetParent(root, false);
        float depth = BackWallZ - KitchenWallZ;
        floor.transform.localScale = new Vector3(RoomHalfWidth * 2f, 0.1f, depth);
        floor.transform.localPosition = new Vector3(0f, -0.05f, (KitchenWallZ + BackWallZ) / 2f);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<MeshFilter>());

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar Collider";
        bar.transform.SetParent(root, false);
        bar.transform.localScale = new Vector3(BarHalfLength * 2f, CounterHeight, BarFarZ - BarNearZ);
        bar.transform.localPosition = new Vector3(0f, CounterHeight / 2f, (BarNearZ + BarFarZ) / 2f);
        UnityEngine.Object.DestroyImmediate(bar.GetComponent<MeshRenderer>());
        UnityEngine.Object.DestroyImmediate(bar.GetComponent<MeshFilter>());
        return root;
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
