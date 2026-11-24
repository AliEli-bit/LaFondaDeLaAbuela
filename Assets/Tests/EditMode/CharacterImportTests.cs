using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Fonda.Tests
{
    /// <summary>
    /// Checks the Blender characters as Unity imports them (see CharacterImportRules and Tools/Blender/report.json).
    /// </summary>
    public class CharacterImportTests
    {
        // Heights to the top of the figure, in metres, from Tools/Blender/report.json.
        static readonly Dictionary<string, float> CustomerHeights = new Dictionary<string, float>
        {
            { "Office", 1.3863f }, { "Grandpa", 1.3318f }, { "Child", 1.0881f }, { "Regular", 1.488f },
            { "Worker", 1.5773f }, { "Student", 1.3981f }, { "Tourist", 1.5732f }, { "Stingy", 1.5486f },
        };

        static readonly string[] GrandmaPieces = { "Grandma_Sleeve_L", "Grandma_Sleeve_R", "Grandma_Bracelet" };

        static IEnumerable<string> CustomerTypes => CustomerHeights.Keys;

        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        GameObject Spawn(string assetName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImportRules.Folder + assetName + ".fbx");
            Assert.IsNotNull(asset, $"{assetName}.fbx is not imported");
            var go = Object.Instantiate(asset);
            spawned.Add(go);
            return go;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        static int Triangles(GameObject go) =>
            go.GetComponentsInChildren<MeshFilter>(true)
              .Where(f => f.sharedMesh != null)
              .Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(i => (int)f.sharedMesh.GetIndexCount(i)) / 3);

        static Bounds VisibleBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        [Test]
        public void Palette_IsPointFilteredWithoutMipmaps()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(CharacterImportRules.PalettePath);
            Assert.IsNotNull(importer);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsTrue(importer.sRGBTexture);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
        }

        [Test]
        public void SharedMaterial_UsesThePalette()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CharacterImportRules.MaterialPath);
            Assert.IsNotNull(material);
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<Texture2D>(CharacterImportRules.PalettePath), material.mainTexture);
        }

        [Test]
        public void Customer_HasThePivotHierarchy([ValueSource(nameof(CustomerTypes))] string type)
        {
            var go = Spawn("Customer_" + type);
            foreach (var name in new[] { "Hips", "Leg_L", "Leg_R", "Torso", "Arm_L", "Arm_R", "Head",
                                         "Face_Happy", "Face_Neutral", "Face_Angry" })
                Assert.IsNotNull(Find(go.transform, name), $"{type} has no {name}");

            var head = Find(go.transform, "Head");
            foreach (var face in new[] { "Face_Happy", "Face_Neutral", "Face_Angry" })
                Assert.AreEqual(head, Find(go.transform, face).parent, $"{face} must be a child of Head");
        }

        [Test]
        public void Customer_ShowsOnlyTheNeutralFace([ValueSource(nameof(CustomerTypes))] string type)
        {
            var go = Spawn("Customer_" + type);
            Assert.IsTrue(Find(go.transform, "Face_Neutral").gameObject.activeSelf);
            Assert.IsFalse(Find(go.transform, "Face_Happy").gameObject.activeSelf);
            Assert.IsFalse(Find(go.transform, "Face_Angry").gameObject.activeSelf);
        }

        [Test]
        public void Customer_StandsOnTheOriginAtItsBlenderHeight([ValueSource(nameof(CustomerTypes))] string type)
        {
            var go = Spawn("Customer_" + type);
            Assert.That(Quaternion.Angle(Quaternion.identity, go.transform.localRotation), Is.LessThan(0.01f), "root rotation");
            Assert.That(go.transform.localScale, Is.EqualTo(Vector3.one), "root scale");

            var bounds = VisibleBounds(go);
            var shoes = Find(go.transform, "Leg_L").GetComponent<Renderer>().bounds;
            Assert.That(shoes.min.y, Is.EqualTo(0f).Within(0.01f), "shoes on y = 0");
            // report.json measures from the floor to the top; hand props (cane, briefcase) may hang lower.
            Assert.That(bounds.max.y, Is.EqualTo(CustomerHeights[type]).Within(CustomerHeights[type] * 0.02f), "height");
            var torso = Find(go.transform, "Torso").GetComponent<Renderer>().bounds;
            Assert.That(torso.center.x, Is.EqualTo(0f).Within(0.01f), "torso centred on x");
        }

        [Test]
        public void Customer_FacesForward([ValueSource(nameof(CustomerTypes))] string type)
        {
            // Customers use their +Z as facing (Customer copies the seat rotation and walks with LookRotation).
            var go = Spawn("Customer_" + type);
            var head = Find(go.transform, "Head");
            var face = Find(go.transform, "Face_Neutral").GetComponent<Renderer>();
            Assert.That(face.bounds.center.z, Is.GreaterThan(head.position.z + 0.1f), "the face must be on the +Z side of the head");
        }

        [Test]
        public void Customer_LeftArmIsOnItsOwnLeft([ValueSource(nameof(CustomerTypes))] string type)
        {
            // Facing +Z with +X to the right, the figure's own left is -X.
            var go = Spawn("Customer_" + type);
            Assert.That(Find(go.transform, "Arm_L").position.x, Is.LessThan(0f));
            Assert.That(Find(go.transform, "Arm_R").position.x, Is.GreaterThan(0f));
        }

        [Test]
        public void Customer_PivotsHaveNoRotation([ValueSource(nameof(CustomerTypes))] string type)
        {
            var go = Spawn("Customer_" + type);
            foreach (var name in new[] { "Hips", "Leg_L", "Leg_R", "Torso", "Head" })
                Assert.That(Quaternion.Angle(Quaternion.identity, Find(go.transform, name).localRotation), Is.LessThan(0.01f), name);
        }

        [Test]
        public void Customer_StaysWithinTheTriangleBudget([ValueSource(nameof(CustomerTypes))] string type)
        {
            Assert.That(Triangles(Spawn("Customer_" + type)), Is.LessThanOrEqualTo(2500));
        }

        [Test]
        public void GrandmaPiece_StaysWithinTheTriangleBudget([ValueSource(nameof(GrandmaPieces))] string piece)
        {
            Assert.That(Triangles(Spawn(piece)), Is.InRange(1, 600));
        }

        [Test]
        public void GrandmaSleeve_RunsFromTheWristAlongPlusZ([Values("Grandma_Sleeve_L", "Grandma_Sleeve_R")] string piece)
        {
            // Origin at the centre of the wrist opening; the 0.16 m tube runs along +Z toward the elbow.
            var go = Spawn(piece);
            Assert.That(Quaternion.Angle(Quaternion.identity, go.transform.localRotation), Is.LessThan(0.01f), "root rotation");
            var bounds = VisibleBounds(go);
            Assert.That(bounds.min.z, Is.EqualTo(0f).Within(0.01f), "wrist end at z = 0");
            Assert.That(bounds.max.z, Is.EqualTo(0.16f).Within(0.01f), "elbow end at z = 0.16");
            Assert.That(bounds.center.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(bounds.center.y, Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void GrandmaBracelet_IsCentredOnItsOrigin()
        {
            var go = Spawn("Grandma_Bracelet");
            Assert.That(Quaternion.Angle(Quaternion.identity, go.transform.localRotation), Is.LessThan(0.01f), "root rotation");
            Assert.That(VisibleBounds(go).center.magnitude, Is.LessThan(0.005f));
        }

        [Test]
        public void EveryCharacterUsesOnlyTheSharedMaterial(
            [ValueSource(nameof(AllCharacterAssets))] string assetName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(CharacterImportRules.MaterialPath);
            foreach (var renderer in Spawn(assetName).GetComponentsInChildren<Renderer>(true))
                foreach (var shared in renderer.sharedMaterials)
                    Assert.AreEqual(material, shared, $"{assetName}/{renderer.name}");
        }

        static IEnumerable<string> AllCharacterAssets =>
            CustomerTypes.Select(t => "Customer_" + t).Concat(GrandmaPieces);
    }
}
