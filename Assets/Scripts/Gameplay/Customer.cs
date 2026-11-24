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
        [Tooltip("Blender characters in FondaCoreLoopBuilder.CustomerTypes order; the profile picks one, or one at random. Empty: the blockout parts above are used.")]
        [SerializeField] CustomerLook[] looks = Array.Empty<CustomerLook>();
        [Tooltip("The customer's body for physics: thrown ingredients and coins bump into it. Fitted to the look.")]
        [SerializeField] CapsuleCollider hitBox;
        [SerializeField, Min(0.05f)] float hitRadius = 0.22f;

        /// <summary>The collider thrown things hit; it follows the body and fits the look's height.</summary>
        public CapsuleCollider HitBox => hitBox;

        // Height of the hit box when there is no Blender look (the capsule fallback figure).
        const float FallbackHeight = 1.7f;

        [Header("Motion")]
        [SerializeField] float walkSpeed = 1.0f;
        [Tooltip("How much the body drops when sitting on the stool.")]
        [SerializeField] float sitDrop = 0.3f;
        [Tooltip("Height of the stool cushion; a Blender character sits with its thighs on it.")]
        [SerializeField] float seatHeight = 0.62f;
        [Tooltip("Gap between the top of the character and its speech bubble.")]
        [SerializeField] float bubbleGap = 0.18f;
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
        /// <summary>The customer took the plate.</summary>
        public event Action<Customer> Served;
        /// <summary>The player put something on the tray that was not ordered.</summary>
        public event Action<Customer> Refused;
        /// <summary>Patience ran out and the customer stood up without eating.</summary>
        public event Action<Customer> LeftAngry;

        public RecipeData Order { get; private set; }
        public BarSeat Seat { get; private set; }
        public CustomerCycle Cycle { get; private set; }
        /// <summary>The type of customer (customer types S1); null for a plain customer.</summary>
        public CustomerProfile Profile { get; private set; }
        /// <summary>Extra coins earned by the speed combo when served (combo S2).</summary>
        public int TipBonus { get; private set; }

        Vector3 door;
        bool seated;
        bool released;
        CustomerLook look;
        float sitOffset;
        MaterialPropertyBlock block;
        Coroutine shake;

        /// <summary>Sets the customer on its way to a seat it has already claimed.</summary>
        public void Init(RecipeData order, BarSeat seat, Vector3 doorPosition, float patienceSeconds, float eatSeconds,
            CustomerProfile profile = null)
        {
            Order = order;
            Profile = profile;
            Seat = seat;
            door = doorPosition;
            Cycle = new CustomerCycle(patienceSeconds, eatSeconds);
            transform.position = doorPosition;
            PickLook();
            if (look == null && bodyRenderer != null && bodyColors.Length > 0)
                SetColor(bodyRenderer, bodyColors[UnityEngine.Random.Range(0, bodyColors.Length)]);
            ShowBubble(null);
            UpdateFace();
            Debug.Log("[Customer] Entra " + (profile != null ? profile.DisplayName : "un cliente") + " y va al asiento " +
                      seat.name + " (pide " + order.DisplayName + ")");
        }

        /// <summary>Called by the seat when the tray matches the order. False if not waiting for it.</summary>
        public bool Serve()
        {
            if (Cycle == null || !Cycle.Serve()) return false;
            Debug.Log("[Customer] Recibe su plato: " + Order.DisplayName);
            ShowBubble(ThanksText);
            Served?.Invoke(this);
            return true;
        }

        const string ThanksText = "¡Gracias, doña!";

        /// <summary>The shift's speed combo after this delivery: shown in the bubble from x2, with its extra coins.</summary>
        public void ApplyCombo(int combo, int tipBonus)
        {
            TipBonus = Mathf.Max(0, tipBonus);
            ShowBubble(combo >= 2 ? ThanksText + "\n¡Combo ×" + combo + "!" : ThanksText);
        }

        /// <summary>The player put something that was not ordered on the tray.</summary>
        public void ShakeHead()
        {
            if (Cycle == null || Cycle.State != CustomerState.Waiting) return;
            Refused?.Invoke(this);
            if (head == null) return;
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
            sitOffset = look != null ? look.SeatedLift(seatHeight) : -sitDrop;
            if (body != null) body.localPosition += Vector3.up * sitOffset;
            look?.Sit(true);
            seated = true;
            Cycle.Sit();
            Seat.OpenOrder(Order);
            ShowBubble(OrderText(Order, Profile));
            Debug.Log("[Customer] Se sienta en " + Seat.name + " y pide " + Order.DisplayName);
        }

        void StandUp()
        {
            if (seated && body != null) body.localPosition -= Vector3.up * sitOffset;
            if (seated) look?.Sit(false);
            seated = false;
            if (Cycle.LeftAngry)
            {
                ShowBubble("¡Me voy!");
                Debug.Log("[Customer] Se va enojado de " + Seat.name + " sin comer");
                LeftAngry?.Invoke(this);
            }
            else
            {
                ShowBubble(null);
                Debug.Log("[Customer] Terminó de comer y se va de " + Seat.name);
            }
            int coins = Seat.LeaveTip(Cycle, TipBonus, Profile).Count;
            if (coins > 0)
                Debug.Log("[Customer] Deja " + coins + " moneda(s) junto a la bandeja de " + Seat.name +
                          (TipBonus > 0 ? " (+" + TipBonus + " por el combo)" : ""));
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

        /// <summary>Shows the profile's Blender character (or one at random) and puts the bubble above its head.</summary>
        void PickLook()
        {
            look = null;
            ChooseLook();
            FitHitBox();
        }

        void ChooseLook()
        {
            if (looks == null || looks.Length == 0) return;
            int index = Profile != null ? Profile.LookIndex : -1;
            look = index >= 0 && index < looks.Length ? looks[index] : looks[UnityEngine.Random.Range(0, looks.Length)];
            foreach (var l in looks)
                if (l?.Root != null) l.Root.SetActive(l == look);
            if (look.Head != null) head = look.Head;
            if (bubble != null)
            {
                var p = bubble.transform.localPosition;
                bubble.transform.localPosition = new Vector3(p.x, look.Height + bubbleGap, p.z);
            }
        }

        /// <summary>Fits the hit box to the look's height so a short child and a tall worker are hit where they are.</summary>
        void FitHitBox()
        {
            if (hitBox == null) return;
            float height = look != null ? look.Height : FallbackHeight;
            hitBox.direction = 1; // along the body's up
            hitBox.height = height;
            hitBox.radius = Mathf.Min(hitRadius, height / 2f);
            hitBox.center = new Vector3(0f, height / 2f, 0f);
        }

        void UpdateFace()
        {
            if (Cycle == null) return;
            if (look != null)
            {
                look.ShowMood(Cycle.Mood);
                return;
            }
            if (faceRenderer == null) return;
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

        /// <summary>The order bubble: the dish name, or the type's own title (A6), and the ingredients below.</summary>
        public static string OrderText(RecipeData order, CustomerProfile profile = null)
        {
            var title = profile != null && !string.IsNullOrEmpty(profile.OrderTitle) ? profile.OrderTitle : order.DisplayName;
            var sb = new StringBuilder(title);
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
