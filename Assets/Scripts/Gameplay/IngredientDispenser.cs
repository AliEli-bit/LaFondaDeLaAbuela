using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// Keeps one ingredient at its spawn point. As soon as that ingredient is no longer there
    /// (grabbed and carried away, knocked or rolled off, or destroyed) a fresh copy appears,
    /// so ingredients never run out and the dispenser never looks empty.
    /// </summary>
    public class IngredientDispenser : MonoBehaviour
    {
        [SerializeField] GameObject ingredientPrefab;
        [Tooltip("Where the ingredient rests. Defaults to this transform.")]
        [SerializeField] Transform spawnPoint;

        [Tooltip("The current ingredient must be this far from the spawn point before a copy appears, " +
                 "so the two never overlap and the physics push the new one off the counter.")]
        [SerializeField] float clearRadius = 0.09f;

        Transform current;

        void Start()
        {
            if (spawnPoint == null) spawnPoint = transform;
            Spawn();
        }

        void Update()
        {
            // A destroyed ingredient compares equal to null, so this also covers consumed ones.
            if (current == null || (current.position - spawnPoint.position).sqrMagnitude > clearRadius * clearRadius)
                Spawn();
        }

        void Spawn()
        {
            if (ingredientPrefab == null)
            {
                Debug.LogError("[Dispenser] No ingredient prefab assigned on " + name, this);
                enabled = false;
                return;
            }
            current = Instantiate(ingredientPrefab, spawnPoint.position, spawnPoint.rotation).transform;
        }
    }
}
