using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Fonda
{
    /// <summary>
    /// Tray with one socket per slot. While an order is open every slot is enabled, more than the longest dish
    /// needs, so a wrong ingredient never leaves the right one without room. The base of the order (bun or bowl)
    /// is shown under them without taking a slot.
    /// Every slot catches over the whole tray, but only the next empty one is open at a time; it places its
    /// ingredient on top of the pile, so the dish is built as a stack on the base (<see cref="TrayStack"/>).
    /// Every change is judged with <see cref="TrayRule"/>. A wrong ingredient is reported as soon as it
    /// enters and stays until the player removes it; a correct tray is delivered and emptied (A1).
    /// </summary>
    public class Tray : MonoBehaviour
    {
        [SerializeField] RecipeData order;
        [Tooltip("One socket per layer of the pile; all are active while an order is open.")]
        [SerializeField] XRSocketInteractor[] slots;

        [Header("Base (S8): shown for the active order, never counted as an ingredient")]
        [SerializeField] GameObject bunBase;
        [SerializeField] GameObject bowlBase;

        [Header("Stack")]
        [Tooltip("Thickness assumed for the ingredient on its way to the pile, before the tray knows which one it is.")]
        [SerializeField] float incomingThickness = 0.02f;

        [Header("Feedback (A2)")]
        [SerializeField] Renderer flashRenderer;
        [SerializeField] Color correctColor = new Color(0.2f, 0.8f, 0.3f);
        [SerializeField] Color wrongColor = new Color(0.85f, 0.2f, 0.2f);
        [SerializeField] float flashSeconds = 0.4f;
        [Tooltip("Delay before the tray is emptied so the player sees the result.")]
        [SerializeField] float clearDelaySeconds = 0.4f;

        [Header("Events")]
        public UnityEvent onOrderCorrect;
        public UnityEvent onOrderWrong;

        public event Action<RecipeData> OrderCorrect;
        public event Action<RecipeData> OrderWrong;
        /// <summary>An ingredient landed in one of the slots of an open order (feedback A1).</summary>
        public event Action<Tray> IngredientPlaced;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<IngredientData> placed = new List<IngredientData>();
        readonly List<XRSocketInteractor> pile = new List<XRSocketInteractor>(); // occupied slots, bottom to top
        readonly List<float> layerThickness = new List<float>();
        MaterialPropertyBlock block;
        bool busy; // true while a correct tray waits to be delivered and emptied
        bool clearing; // true while slots are being emptied (ignores the resulting select-exit events)
        bool subscribed;

        public RecipeData Order => order;

        /// <summary>True while a correct tray is flashing and being emptied (the delivery is in progress).</summary>
        public bool IsDelivering => busy;

        void OnEnable() => Subscribe();

        void Start() => ApplyOrder();

        void OnDisable()
        {
            Unsubscribe();
            busy = false;
        }

        /// <summary>
        /// Sets the active order (e.g. from a customer), emptying the tray, showing its base and enabling the slots.
        /// Null closes the tray: no slots, no base.
        /// </summary>
        public void SetOrder(RecipeData recipe)
        {
            order = recipe;
            ClearSlots();
            ApplyOrder();
        }

        void ApplyOrder()
        {
            int count = order != null ? order.Ingredients.Count : 0;
            if (bunBase != null) bunBase.SetActive(order != null && order.Base == DishBase.Bun);
            if (bowlBase != null) bowlBase.SetActive(order != null && order.Base == DishBase.Bowl);
            if (slots == null) return;
            if (count > slots.Length)
                Debug.LogWarning("[Tray] Order needs " + count + " slots but the tray has " + slots.Length + ".", this);

            // Every slot is open, not just one per ordered ingredient: a wrong ingredient takes a layer of its own
            // and the player can still pile the right ones before taking it out (CLAUDE.md §3).
            foreach (var slot in slots)
                if (slot != null) slot.gameObject.SetActive(order != null);
            Restack();
        }

        /// <summary>
        /// Puts every caught ingredient on the pile, bottom to top in arrival order, and opens only the next empty
        /// slot, waiting on top of the pile. The other empty slots stay closed so they never compete for a drop.
        /// </summary>
        void Restack()
        {
            if (slots == null) return;
            pile.RemoveAll(s => s == null || !s.hasSelection);
            foreach (var slot in slots)
                if (slot != null && slot.hasSelection && !pile.Contains(slot)) pile.Add(slot);

            layerThickness.Clear();
            foreach (var slot in pile) layerThickness.Add(LayerThickness(slot));
            float baseTop = BaseTop();
            var centers = TrayStack.Centers(baseTop, layerThickness);
            for (int i = 0; i < pile.Count; i++) PlaceAttach(pile[i], centers[i]);

            float top = TrayStack.Top(baseTop, layerThickness);
            bool opened = false;
            foreach (var slot in slots)
            {
                if (slot == null || slot.hasSelection) continue; // a slot holding an ingredient stays active
                bool open = !opened && slot.gameObject.activeSelf;
                slot.socketActive = open;
                if (!open) continue;
                opened = true;
                PlaceAttach(slot, top + incomingThickness / 2f);
            }
        }

        static float LayerThickness(XRSocketInteractor slot)
        {
            var selected = slot.firstInteractableSelected as Component;
            var ingredient = selected != null ? selected.GetComponent<Ingredient>() : null;
            return ingredient != null ? ingredient.Thickness : 0f;
        }

        /// <summary>Top of the visible base of the order (bun or bowl) in the tray's space; the pile starts there.</summary>
        float BaseTop()
        {
            var dishBase = order == null ? null : order.Base == DishBase.Bowl ? bowlBase : bunBase;
            if (dishBase == null) return 0f;
            var baseRenderer = dishBase.GetComponent<Renderer>();
            return baseRenderer != null
                ? transform.InverseTransformPoint(baseRenderer.bounds.max).y
                : dishBase.transform.localPosition.y;
        }

        void PlaceAttach(XRSocketInteractor slot, float height)
        {
            var attach = slot.attachTransform;
            if (attach == null) return;
            attach.SetPositionAndRotation(transform.TransformPoint(new Vector3(0f, height, 0f)), transform.rotation);
        }

        void Subscribe()
        {
            if (subscribed || slots == null) return;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                slot.selectEntered.AddListener(OnSlotEntered);
                slot.selectExited.AddListener(OnSlotExited);
            }
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || slots == null) return;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                slot.selectEntered.RemoveListener(OnSlotEntered);
                slot.selectExited.RemoveListener(OnSlotExited);
            }
            subscribed = false;
        }

        void OnSlotEntered(SelectEnterEventArgs _)
        {
            Restack();
            if (!clearing && order != null) IngredientPlaced?.Invoke(this);
            Evaluate(true);
        }

        // Removing an ingredient never reports an error, but can complete the order (the extra one was taken out).
        // The layers above it drop down to close the gap.
        void OnSlotExited(SelectExitEventArgs _)
        {
            if (!clearing) Restack();
            Evaluate(false);
        }

        void Evaluate(bool entered)
        {
            if (busy || clearing || order == null) return;

            placed.Clear();
            foreach (var slot in slots)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy || !slot.hasSelection) continue;
                var selected = slot.firstInteractableSelected as Component;
                var ingredient = selected != null ? selected.GetComponent<Ingredient>() : null;
                placed.Add(ingredient != null ? ingredient.Data : null);
            }

            var result = TrayRule.Evaluate(order.Ingredients, placed);
            if (result == TrayEvaluation.Correct)
            {
                Debug.Log("[Tray] Plato correcto: " + order.DisplayName);
                OrderCorrect?.Invoke(order);
                onOrderCorrect?.Invoke();
                StartCoroutine(FlashAndClear(correctColor));
            }
            else if (result == TrayEvaluation.Wrong && entered)
            {
                Debug.Log("[Tray] Ingrediente que no se pidio en: " + order.DisplayName);
                OrderWrong?.Invoke(order);
                onOrderWrong?.Invoke();
                StartCoroutine(Flash(wrongColor));
            }
        }

        IEnumerator Flash(Color color)
        {
            SetFlash(color);
            yield return new WaitForSeconds(flashSeconds);
            if (!busy) SetFlash(null);
        }

        IEnumerator FlashAndClear(Color color)
        {
            busy = true;
            SetFlash(color);
            // Clear on the next frame at the earliest so we never destroy inside an XRI select callback.
            yield return new WaitForSeconds(Mathf.Max(clearDelaySeconds, 0.02f));
            ClearSlots();
            yield return new WaitForSeconds(Mathf.Max(flashSeconds - clearDelaySeconds, 0f));
            SetFlash(null);
            busy = false;
        }

        void ClearSlots()
        {
            if (slots == null) return;
            clearing = true;
            foreach (var slot in slots)
            {
                if (slot == null || !slot.hasSelection) continue;

                var victims = new List<GameObject>();
                foreach (var interactable in slot.interactablesSelected)
                    if (interactable is Component c) victims.Add(c.gameObject);

                // Release cleanly through the manager, then destroy the ingredients.
                if (slot.interactionManager != null)
                    slot.interactionManager.CancelInteractorSelection((IXRSelectInteractor)slot);
                foreach (var v in victims) Destroy(v);
            }
            clearing = false;
            Restack();
        }

        void SetFlash(Color? color)
        {
            if (flashRenderer == null) return;
            if (color == null)
            {
                flashRenderer.SetPropertyBlock(null);
                return;
            }
            block ??= new MaterialPropertyBlock();
            flashRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color.Value);
            flashRenderer.SetPropertyBlock(block);
        }
    }
}
