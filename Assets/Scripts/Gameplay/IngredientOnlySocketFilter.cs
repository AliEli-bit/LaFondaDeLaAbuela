using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda
{
    /// <summary>
    /// Lets a tray slot hover and select only objects that carry an <see cref="Ingredient"/>.
    /// Anything else (the sauce bottles) never enters a slot, so it never counts as an ingredient or as an error (S9).
    /// </summary>
    public class IngredientOnlySocketFilter : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        public bool canProcess => isActiveAndEnabled;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => IsIngredient(interactable);

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => IsIngredient(interactable);

        static bool IsIngredient(IXRInteractable interactable) =>
            interactable is Component c && c != null && c.GetComponent<Ingredient>() != null;
    }
}
