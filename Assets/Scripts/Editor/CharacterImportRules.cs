using UnityEditor;
using UnityEngine;

/// <summary>
/// Import rules for the Blender-generated characters in Assets/Art/Characters
/// (source of truth: Tools/Blender/fonda_figure.py).
/// - The palette is a grid of flat swatches: point filter, no mipmaps, no compression, so colours never bleed.
/// - Every model uses the one shared material Fonda_Characters (no per-file material copies).
/// - Rotations are baked into the meshes and the figure is turned to face +Z, so the root and every
///   pivot keep identity rotations for code-driven animation.
/// - Only Face_Neutral starts active; code toggles the other faces.
/// </summary>
public class CharacterImportRules : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Characters/";
    public const string PalettePath = Folder + "Fonda_Palette.png";
    public const string MaterialPath = Folder + "Fonda_Characters.mat";

    static readonly string[] HiddenFaces = { "Face_Happy", "Face_Angry" };

    public override uint GetVersion() => 3;

    static bool IsCharacterAsset(string path) => path.StartsWith(Folder);

    void OnPreprocessTexture()
    {
        if (assetPath != PalettePath) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaSource = TextureImporterAlphaSource.None;
    }

    void OnPreprocessModel()
    {
        if (!IsCharacterAsset(assetPath)) return;

        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importVisibility = false;
        importer.isReadable = false;

        // Re-import the models when the shared material changes.
        context.DependsOnSourceAsset(MaterialPath);
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!IsCharacterAsset(assetPath)) return;

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Debug.LogWarning($"[CharacterImportRules] Missing {MaterialPath}; {assetPath} keeps the default material.");
        }
        else
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        // The export comes out turned 180 degrees: customers face -Z and the sleeves run along -Z.
        BakeTransforms(root.transform, Quaternion.Euler(0f, 180f, 0f));

        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(HiddenFaces, child.name) >= 0) child.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The Blender export leaves a 90 degree axis rotation on the root and faces -Z. Bake every node's
    /// rotation and scale into its mesh, turned 180 degrees on Y (customers face +Z as Customer expects,
    /// sleeves run along +Z toward the elbow), so every pivot keeps its position with an identity rotation and unit scale.
    /// </summary>
    static void BakeTransforms(Transform root, Quaternion facing)
    {
        var nodes = root.GetComponentsInChildren<Transform>(true);
        var worldPositions = new Vector3[nodes.Length];
        var worldLinear = new Matrix4x4[nodes.Length];

        for (int i = 0; i < nodes.Length; i++)
        {
            worldPositions[i] = facing * (nodes[i].position - root.position);
            worldLinear[i] = Matrix4x4.Rotate(facing) * Matrix4x4.TRS(Vector3.zero, nodes[i].rotation, nodes[i].lossyScale);
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i].localRotation = Quaternion.identity;
            nodes[i].localScale = Vector3.one;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i].position = root.position + worldPositions[i];

            var filter = nodes[i].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;

            var mesh = filter.sharedMesh;
            var linear = worldLinear[i];
            var normalMatrix = linear.inverse.transpose;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            for (int v = 0; v < vertices.Length; v++) vertices[v] = linear.MultiplyVector(vertices[v]);
            for (int n = 0; n < normals.Length; n++) normals[n] = normalMatrix.MultiplyVector(normals[n]).normalized;
            mesh.vertices = vertices;
            mesh.normals = normals;

            // A mirroring scale flips the winding; keep faces pointing outward.
            if (linear.determinant < 0f)
            {
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var triangles = mesh.GetTriangles(s);
                    for (int t = 0; t < triangles.Length; t += 3)
                        (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                    mesh.SetTriangles(triangles, s);
                }
            }

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }
}
