using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

namespace Fonda
{
    /// <summary>
    /// A customer at the bar (S3): walks from the door to its seat, orders (speech bubble and face), waits with
    /// running patience, eats once served and walks back out. Leaves angry if patience runs out (S4).
    /// The rules live in <see cref="CustomerCycle"/>; this component only moves and shows them.
    /// </summary>
    public class Customer : MonoBehaviour
    {
        [Header("Parts")]
        [SerializeField] Transform body;
        [SerializeField] Transform head;
        [SerializeField] Renderer bodyRenderer;
        [SerializeField] Renderer faceRenderer;
        [SerializeField] TextMeshPro bubble;

        [Header("Motion")]
        [SerializeField] float walkSpeed = 1.0f;
        [Tooltip("How much the body drops when sitting on the stool.")]
        [SerializeField] float sitDrop = 0.3f;
        [SerializeField] float shakeSeconds = 0.6f;
        [SerializeField] float shakeDegrees = 25f;

        [Header("Look")]
        [SerializeField] Color happyColor = new Color(0.30f, 0.80f, 0.35f);
        [SerializeField] Color neutralColor = new Color(0.95f, 0.80f, 0.25f);
        [SerializeField] Color angryColor = new Color(0.90f, 0.25f, 0.20f);
        [SerializeField] Color[] bodyColors =
        {
            new Color(0.25f, 0.45f, 0.80f), new Color(0.75f, 0.35f, 0.55f), new Color(0.35f, 0.60f, 0.45f),
            new Color(0.85f, 0.55f, 0.25f), new Color(0.50f, 0.40f, 0.70f),
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public event Action<Customer> Left;

        public RecipeData Order { get; private set; }
        public BarSeat Seat { get; private set; }
        public CustomerCycle Cycle { get; private set; }

        Vector3 door;
        bool seated;
        bool released;
        MaterialPropertyBlock block;
        Coroutine shake;

        /// <summary>Sets the customer on its way to a seat it has already claimed.</summary>
        public void Init(RecipeData order, BarSeat seat, Vector3 doorPosition, float patienceSeconds, float eatSeconds)
        {
            Order = order;
            Seat = seat;
            door = doorPosition;
            Cycle = new CustomerCycle(patienceSeconds, eatSeconds);
            transform.position = doorPosition;
            if (bodyRenderer != null && bodyColors.Length > 0)
                SetColor(bodyRenderer, bodyColors[UnityEngine.Random.Range(0, bodyColors.Length)]);
            ShowBubble(null);
            UpdateFace();
            Debug.Log("[Customer] Entra y va al asiento " + seat.name + " (pide " + order.DisplayName + ")");
        }

        /// <summary>Called by the seat when the tray matches the order. False if not waiting for it.</summary>
        public bool Serve()
        {
            if (Cycle == null || !Cycle.Serve()) return false;
            Debug.Log("[Customer] Recibe su plato: " + Order.DisplayName);
            ShowBubble("¡Gracias, doña!");
            return true;
        }

        /// <summary>The player put something that was not ordered on the tray.</summary>
        public void ShakeHead()
        {
            if (Cycle == null || Cycle.State != CustomerState.Waiting || head == null) return;
            if (shake != null) StopCoroutine(shake);
            shake = StartCoroutine(Shake());
        }

        void Update()
        {
            if (Cycle == null || Seat == null) return;
            switch (Cycle.State)
            {
                case CustomerState.Arriving:
                    if (MoveTowards(Seat.SeatPoint.position)) SitDown();
                    break;
                case CustomerState.Waiting:
                case CustomerState.Eating:
                    Cycle.Tick(Time.deltaTime);
                    UpdateFace();
                    if (Cycle.State == CustomerState.Leaving) StandUp();
                    break;
                case CustomerState.Leaving:
                    if (MoveTowards(door))
                    {
                        Left?.Invoke(this);
                        Destroy(gameObject);
                    }
                    break;
            }
            FaceBubbleToPlayer();
        }

        void SitDown()
        {
            transform.rotation = Seat.SeatPoint.rotation;
            if (body != null) body.localPosition += Vector3.down * sitDrop;
            seated = true;
            Cycle.Sit();
            Seat.OpenOrder(Order);
            ShowBubble(OrderText(Order));
            Debug.Log("[Customer] Se sienta en " + Seat.name + " y pide " + Order.DisplayName);
        }

        void StandUp()
        {
            if (seated && body != null) body.localPosition -= Vector3.down * sitDrop;
            seated = false;
            if (Cycle.LeftAngry)
            {
                ShowBubble("¡Me voy!");
                Debug.Log("[Customer] Se va enojado de " + Seat.name + " sin comer");
            }
            else
            {
                ShowBubble(null);
                Debug.Log("[Customer] Terminó de comer y se va de " + Seat.name);
            }
            int coins = Seat.LeaveTip(Cycle).Count;
            if (coins > 0) Debug.Log("[Customer] Deja " + coins + " moneda(s) junto a la bandeja de " + Seat.name);
            ReleaseSeat();
        }

        void ReleaseSeat()
        {
            if (released || Seat == null) return;
            released = true;
            Seat.Release(this);
        }

        void OnDestroy()
        {
            // Covers the scene being unloaded mid-visit.
            ReleaseSeat();
        }

        /// <summary>Walks on the floor towards a point; returns true on arrival.</summary>
        bool MoveTowards(Vector3 target)
        {
            target.y = transform.position.y;
            var to = target - transform.position;
            if (to.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.position = Vector3.MoveTowards(transform.position, target, walkSpeed * Time.deltaTime);
            return (transform.position - target).sqrMagnitude < 1e-4f;
        }

        IEnumerator Shake()
        {
            var rest = head.localRotation;
            for (float t = 0f; t < shakeSeconds; t += Time.deltaTime)
            {
                float angle = Mathf.Sin(t / shakeSeconds * Mathf.PI * 4f) * shakeDegrees * (1f - t / shakeSeconds);
                head.localRotation = rest * Quaternion.Euler(0f, angle, 0f);
                yield return null;
            }
            head.localRotation = rest;
            shake = null;
        }

        void UpdateFace()
        {
            if (faceRenderer == null || Cycle == null) return;
            var mood = Cycle.Mood;
            SetColor(faceRenderer, mood == CustomerMood.Happy ? happyColor : mood == CustomerMood.Angry ? angryColor : neutralColor);
        }

        void SetColor(Renderer r, Color c)
        {
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }

        void ShowBubble(string text)
        {
            if (bubble == null) return;
            bubble.gameObject.SetActive(!string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) bubble.text = text;
        }

        void FaceBubbleToPlayer()
        {
            if (bubble == null || !bubble.gameObject.activeSelf) return;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : Vector3.up * 1.2f;
            var away = bubble.transform.position - eye;
            if (away.sqrMagnitude > 1e-6f) bubble.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        static string OrderText(RecipeData order)
        {
            var sb = new StringBuilder(order.DisplayName);
            sb.Append('\n');
            for (int i = 0; i < order.Ingredients.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(order.Ingredients[i] != null ? order.Ingredients[i].DisplayName : "?");
            }
            return sb.ToString();
        }
    }
}
