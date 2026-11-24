using UnityEngine;

namespace Fonda
{
    /// <summary>Marks a grabbable object as an ingredient and links it to its data.</summary>
    public class Ingredient : MonoBehaviour
    {
        [SerializeField] IngredientData data;

        public IngredientData Data => data;

        /// <summary>How much this ingredient adds to the height of a dish when it lies flat on the pile.</summary>
        public float Thickness
        {
            get
            {
                var box = GetComponent<BoxCollider>();
                if (box != null) return box.size.y * transform.lossyScale.y;
                var meshFilter = GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                    return meshFilter.sharedMesh.bounds.size.y * transform.lossyScale.y;
                return 0f;
            }
        }
    }
}
