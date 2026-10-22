using UnityEngine;

namespace SpellSign.Sigils
{
    // An elemental shield around the player's head, against the dragon's breath.
    //
    // While the breath warning shows (AttackWarningUI, from the start of the
    // breath to its end), holding up a drawn sigil raises a shield of the
    // shape's element: circle = Water, triangle = Fire, line = Lightning. A
    // board already held up when the warning appears counts too.
    //
    // Only the right shield is taken: one of the breath's element (the
    // dragon's own). A wrong shape raises nothing and the warning says "Wrong
    // shield!" -- keep trying until the right one is shown. Once it is up it
    // stays up, board or no board, until the attack is over.
    //
    // Protects() is the one rule for what a shield stops, used for the dragon's
    // projectiles (Blocks), its breath damage, and where the breath stops in the air.
    //
    // Add it next to SigilScanner. It follows Camera.main (the Camera Rig's
    // CenterEyeAnchor, which is also BossProjectile's player target). The shield
    // the player sees is Bubble Visual Prefab, recolored per element (ColorChanger).
    public class SigilShield : MonoBehaviour
    {
        [Tooltip("Bubble radius around the head, in meters. Shots are stopped here.")]
        [SerializeField] float radius = 0.8f;

        public ColorChanger bubbleVisualPrefab;

        public Element element = Element.Fire;

        public static SigilShield Instance { get; private set; }

        ColorChanger bubbleVisual;
        Vector3 visualScale;       // the visual's scale that makes it as big as Radius
        bool up;
        SigilScanner scanner;      // for the shape held up right now

        public static bool IsUp => Instance != null && Instance.isActiveAndEnabled && Instance.up;

        // Bubble radius in meters, for effects that should stop at its edge.
        public static float Radius => Instance != null ? Instance.radius : 0f;

        // True while a shield is up that stops attacks of this element.
        public static bool Protects(Element attack) => IsUp && Instance.element == attack;

        // The shield each drawn shape raises.
        public static Element ElementFor(SigilType shape)
        {
            switch (shape)
            {
                case SigilType.Circle:   return Element.Water;
                case SigilType.Triangle: return Element.Fire;
                default:                 return Element.Lightning; // the line
            }
        }

        // The shape to draw for a shield of this element, as players are told it
        // (in their language: Translations.csv).
        public static string ShapeName(Element shield) => Loc.T("shape." + shield);

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject); // optional: survive scene loads
        }

        // True while the shield is up and `point` has reached its edge. The shot
        // should then be destroyed without doing damage.
        public static bool Blocks(Vector3 point, Element element)
        {
            if (!Protects(element)) return false;

            Camera cam = Camera.main;
            if (cam == null) return false;

            if ((point - cam.transform.position).sqrMagnitude > Instance.radius * Instance.radius) return false;

            Debug.Log("[SigilShield] blocked a shot");
            return true;
        }

        void OnEnable()
        {
            scanner = GetComponent<SigilScanner>();
            SigilScanner.Scanned += OnScanned;
        }

        void OnDisable()
        {
            SigilScanner.Scanned -= OnScanned;
            Drop();
        }

        void OnDestroy()
        {
            if (bubbleVisual != null) Destroy(bubbleVisual.gameObject);
            if (Instance == this) Instance = null;
        }

        void OnScanned(SigilResult result)
        {
            if (result.Type != SigilType.None) Raise(ElementFor(result.Type));
        }

        [ContextMenu("Raise Fire shield")]
        public void RaiseFire()
        {
            Raise(Element.Fire);
        }

        [ContextMenu("Raise Lightning shield")]
        public void RaiseLightning()
        {
            Raise(Element.Lightning);
        }

        [ContextMenu("Raise Water shield")]
        public void RaiseWater()
        {
            Raise(Element.Water);
        }

        // Puts up a shield of this element, if a breath is coming and it is the
        // right one for it. A wrong one is turned down, with "Wrong shield!".
        public void Raise(Element element)
        {
            if (AttackWarningUI.Instance == null || !AttackWarningUI.Instance.visible)
            {
                Debug.Log("Can't raise shield now");
                return;
            }
            if (up) return; // already protected for this attack

            if (element != AttackWarningUI.Needed)
            {
                AttackWarningUI.SayWrongShield();
                return;
            }

            if (bubbleVisual == null && !BuildVisual()) return;

            up = true;
            this.element = element;
            SetBubbleVisualColor(); // once per raise: recoloring makes new materials
            AttackWarningUI.SayShielded();
            Debug.Log($"[SigilShield] {element} shield up until the attack is over");
        }

        void LateUpdate()
        {
            // The attack is over: so is the shield.
            if (up && (AttackWarningUI.Instance == null || !AttackWarningUI.Instance.visible)) Drop();

            RaiseForBoardHeldUp();
            if (!up) return;

            // What the player sees goes where the protection is: around the head.
            Camera cam = Camera.main;
            if (cam == null || bubbleVisual == null) return;
            bubbleVisual.transform.position = cam.transform.position;
            bubbleVisual.transform.localScale = visualScale;
            bubbleVisual.SetVisibility(true);
        }

        void Drop()
        {
            up = false;
            if (bubbleVisual != null) bubbleVisual.SetVisibility(false);
        }

        // The scanner only reports a shape when it first recognizes it. A board
        // already held up before the warning was reported too early to count,
        // so while the warning shows and no shield is up, the shape in view now
        // is tried -- and keeps getting "Wrong shield!" for as long as a wrong
        // one is held up.
        void RaiseForBoardHeldUp()
        {
            if (up || scanner == null || scanner.Recognizer == null) return;
            if (AttackWarningUI.Instance == null || !AttackWarningUI.Instance.visible) return;

            SigilType held = scanner.Recognizer.Declared;
            if (held != SigilType.None) Raise(ElementFor(held));
        }

        private void SetBubbleVisualColor()
        {
            if (bubbleVisual == null) return;
            switch (element)
            {
                case Element.Fire:
                    bubbleVisual.ChangeColorFromIndex(0);
                    break;

                case Element.Lightning:
                    bubbleVisual.ChangeColorFromIndex(1);
                    break;

                case Element.Water:
                    bubbleVisual.ChangeColorFromIndex(2);
                    break;
            }
        }

        bool BuildVisual()
        {
            if (bubbleVisualPrefab == null)
            {
                Debug.LogWarning("[SigilShield] no Bubble Visual Prefab assigned -- the shield works but can't be seen.");
                return true; // it still protects
            }

            bubbleVisual = Instantiate(bubbleVisualPrefab);

            // Scaled so its outside is the protected radius around the head.
            // Measured from its meshes: its renderers start switched off.
            float modelRadius = 0f;
            foreach (MeshFilter mesh in bubbleVisual.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh == null) continue;
                Vector3 extents = Vector3.Scale(mesh.sharedMesh.bounds.extents, mesh.transform.lossyScale);
                modelRadius = Mathf.Max(modelRadius, extents.x, extents.y, extents.z);
            }
            if (modelRadius <= 0f) modelRadius = radius;
            visualScale = bubbleVisual.transform.localScale * (radius / modelRadius);
            return true;
        }
    }
}
