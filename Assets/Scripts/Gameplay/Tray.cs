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
    /// Tray with one socket per slot. Only as many slots as the active order has ingredients are enabled,
    /// and the base of the order (bun or bowl) is shown under them without taking a slot.
    /// Every change is judged with <see cref="TrayRule"/>. A wrong ingredient is reported as soon as it
    /// enters and stays until the player removes it; a correct tray is delivered and emptied (A1).
    /// </summary>
    public class Tray : MonoBehaviour
    {
        [SerializeField] RecipeData order;
        [Tooltip("One socket per slot; the first N are active for an order with N ingredients.")]
        [SerializeField] XRSocketInteractor[] slots;

        [Header("Base (S8): shown for the active order, never counted as an ingredient")]
        [SerializeField] GameObject bunBase;
        [SerializeField] GameObject bowlBase;

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

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly List<IngredientData> placed = new List<IngredientData>();
        MaterialPropertyBlock block;
        bool busy; // true while a correct tray waits to be delivered and emptied
        bool clearing; // true while slots are being emptied (ignores the resulting select-exit events)
        bool subscribed;

        public RecipeData Order => order;

        void OnEnable() => Subscribe();

        void Start() => ApplyOrder();

        void OnDisable()
        {
            Unsubscribe();
            busy = false;
        }

        /// <summary>Sets the active order (e.g. from a customer), emptying the tray, showing its base and enabling N slots.</summary>
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

            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) slots[i].gameObject.SetActive(i < count);
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

        void OnSlotEntered(SelectEnterEventArgs _) => Evaluate(true);

        // Removing an ingredient never reports an error, but can complete the order (the extra one was taken out).
        void OnSlotExited(SelectExitEventArgs _) => Evaluate(false);

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
