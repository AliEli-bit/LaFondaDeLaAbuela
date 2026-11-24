using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda
{
    /// <summary>
    /// Holds one ingredient at its spawn point and spawns a fresh copy once that one has been
    /// grabbed and moved clear of the spawn point (or destroyed), so ingredients never run out.
    /// </summary>
    public class IngredientDispenser : MonoBehaviour
    {
        [SerializeField] GameObject ingredientPrefab;
        [Tooltip("Where the ingredient rests. Defaults to this transform.")]
        [SerializeField] Transform spawnPoint;

        [Tooltip("The grabbed ingredient must be this far from the spawn point before a copy appears, " +
                 "so the two never overlap and the physics push the new one off the counter.")]
        [SerializeField] float clearRadius = 0.12f;

        XRGrabInteractable current;
        Transform leaving; // grabbed ingredient still near the spawn point

        void Start()
        {
            if (spawnPoint == null) spawnPoint = transform;
            Spawn();
        }

        void Update()
        {
            if (leaving == null)
            {
                // A destroyed ingredient also compares equal to null: refill only if one was pending.
                if (!ReferenceEquals(leaving, null)) { leaving = null; Spawn(); }
                return;
            }
            if ((leaving.position - spawnPoint.position).sqrMagnitude > clearRadius * clearRadius)
            {
                leaving = null;
                Spawn();
            }
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

            leaving = current.transform;
            Unwatch();
        }

        void Unwatch()
        {
            if (current == null) return;
            current.selectEntered.RemoveListener(OnSelectEntered);
            current = null;
        }
    }
}
