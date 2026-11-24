using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// Builds the "Fonda" blockout scene: fixed XR rig inside a curved bar, no locomotion, customers' wall behind the bar.
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

    // Curved bar around the player (GDD §5, "Distribución de la estación"): angles measured from +Z, left negative.
    internal const float CounterHeight = 0.9f;
    internal const float BarInnerRadius = 0.22f;
    internal const float BarOuterRadius = 0.78f;
    internal const float BarHalfAngle = 80f;
    const int BarSegments = 32;
    internal const float MaxReach = 0.6f;

    // Back wall behind the customers, with a door on the left.
    internal const float BackWallZ = 2.40f;
    const float WallWidth = 4f;
    const float WallHeight = 2.6f;
    const float WallThickness = 0.1f;
    internal const float DoorCenterX = -1.0f;
    const float DoorWidth = 0.9f;
    const float DoorHeight = 2.1f;

    const string MeshesFolder = "Assets/Meshes/Blockout";

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

        var root = new GameObject("Fonda").transform;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(root, false);
        floor.transform.localScale = new Vector3(FloorSize / 10f, 1f, FloorSize / 10f); // plane is 10x10 at scale 1
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        // Curved bar: a solid arc whose top is the work surface (ingredients rest on its collider).
        var bar = new GameObject("Bar");
        bar.transform.SetParent(root, false);
        var mesh = GetOrCreateArcMesh("FondaBar", BarInnerRadius, BarOuterRadius, BarHalfAngle, CounterHeight, BarSegments);
        bar.AddComponent<MeshFilter>().sharedMesh = mesh;
        bar.AddComponent<MeshRenderer>().sharedMaterial = counterMat;
        bar.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Back wall with a door gap on the left: two side panels and a lintel.
        var wall = new GameObject("BackWall").transform;
        wall.SetParent(root, false);
        float left = -WallWidth / 2f, right = WallWidth / 2f;
        float doorLeft = DoorCenterX - DoorWidth / 2f, doorRight = DoorCenterX + DoorWidth / 2f;
        WallPanel(wall, "WallLeft", left, doorLeft, 0f, WallHeight, wallMat);
        WallPanel(wall, "WallRight", doorRight, right, 0f, WallHeight, wallMat);
        WallPanel(wall, "Lintel", doorLeft, doorRight, DoorHeight, WallHeight, wallMat);

        return root;
    }

    static void WallPanel(Transform parent, string name, float x0, float x1, float y0, float y1, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, WallThickness);
        go.transform.localPosition = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, BackWallZ + WallThickness / 2f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    /// <summary>Closed arc solid (top, outer and inner faces, end caps) saved as an asset so the scene can reference it.</summary>
    static Mesh GetOrCreateArcMesh(string name, float rIn, float rOut, float halfAngle, float height, int segments)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Vector3 P(float r, float deg, float y) =>
            new Vector3(r * Mathf.Sin(deg * Mathf.Deg2Rad), y, r * Mathf.Cos(deg * Mathf.Deg2Rad));
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // a-b-c-d in clockwise order as seen from the outside (Unity front faces are clockwise).
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        for (int s = 0; s < segments; s++)
        {
            float a0 = Mathf.Lerp(-halfAngle, halfAngle, s / (float)segments);
            float a1 = Mathf.Lerp(-halfAngle, halfAngle, (s + 1) / (float)segments);
            Quad(P(rIn, a0, height), P(rOut, a0, height), P(rOut, a1, height), P(rIn, a1, height)); // top
            Quad(P(rOut, a0, 0f), P(rOut, a1, 0f), P(rOut, a1, height), P(rOut, a0, height));       // outer
            Quad(P(rIn, a1, 0f), P(rIn, a0, 0f), P(rIn, a0, height), P(rIn, a1, height));           // inner
        }
        Quad(P(rIn, -halfAngle, 0f), P(rOut, -halfAngle, 0f), P(rOut, -halfAngle, height), P(rIn, -halfAngle, height)); // left cap
        Quad(P(rOut, halfAngle, 0f), P(rIn, halfAngle, 0f), P(rIn, halfAngle, height), P(rOut, halfAngle, height));     // right cap

        EnsureFolder("Assets", "Meshes");
        EnsureFolder("Assets/Meshes", "Blockout");
        var path = MeshesFolder + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
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
