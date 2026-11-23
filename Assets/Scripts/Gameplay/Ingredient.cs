using UnityEngine;

namespace Fonda
{
    /// <summary>Marks a grabbable object as an ingredient and links it to its data.</summary>
    public class Ingredient : MonoBehaviour
    {
        [SerializeField] IngredientData data;

        public IngredientData Data => data;
    }
}
