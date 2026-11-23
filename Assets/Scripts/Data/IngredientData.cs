using UnityEngine;

namespace Fonda
{
    /// <summary>Static description of an ingredient (id, display name, icon, world prefab).</summary>
    [CreateAssetMenu(fileName = "Ingredient", menuName = "Fonda/Ingredient Data")]
    public class IngredientData : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Sprite icon;
        [SerializeField] GameObject prefab;

        public string Id => id;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public GameObject Prefab => prefab;
    }
}
