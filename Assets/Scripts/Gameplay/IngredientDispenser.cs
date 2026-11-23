using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda
{
    /// <summary>
    /// Holds one ingredient at its spawn point and spawns a fresh copy as soon as that one is grabbed,
    /// so ingredients never run out.
    /// </summary>
    public class IngredientDispenser : MonoBehaviour
    {
        [SerializeField] GameObject ingredientPrefab;
        [Tooltip("Where the ingredient rests. Defaults to this transform.")]
        [SerializeField] Transform spawnPoint;

        XRGrabInteractable current;

        void Start()
        {
            if (spawnPoint == null) spawnPoint = transform;
            Spawn();
        }

        void OnDestroy() => Unwatch();

        void Spawn()
        {
            if (ingredientPrefab == null)
            {
                Debug.LogError("[Dispenser] No ingredient prefab assigned on " + name, this);
                return;
            }

            var instance = Instantiate(ingredientPrefab, spawnPoint.position, spawnPoint.rotation);
            current = instance.GetComponent<XRGrabInteractable>();
            if (current == null)
            {
                Debug.LogError("[Dispenser] Prefab '" + ingredientPrefab.name + "' has no XRGrabInteractable.", this);
                return;
            }
            current.selectEntered.AddListener(OnSelectEntered);
        }

        void OnSelectEntered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            // A socket picking up the resting ingredient is not a player grab.
            if (args.interactorObject is XRSocketInteractor) return;

            Unwatch();
            Spawn();
        }

        void Unwatch()
        {
            if (current == null) return;
            current.selectEntered.RemoveListener(OnSelectEntered);
            current = null;
        }
    }
}
