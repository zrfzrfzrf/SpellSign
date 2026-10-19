using UnityEngine;

namespace SpellSign.Sigils
{
    // Scanning any shape puts up a shield: a see-through blue bubble around the
    // player's head that fades in, holds, flickers when it's about to run out,
    // then fades away. While it's up, boss shots are stopped at its edge
    // (BossProjectile asks Blocks() every frame).
    //
    // Add it next to SigilScanner. It follows Camera.main (the Camera Rig's
    // CenterEyeAnchor, which is also BossProjectile's player target). Needs the
    // SigilShield material (SpellSign/ViewEdgeGlow shader) assigned, otherwise the
    // shader may be stripped from device builds.
    public class SigilShield : MonoBehaviour
    {


        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Tooltip("Bubble radius around the head, in meters. Shots are stopped here.")]
        [SerializeField] float radius = 0.8f;

        [Header("Timing")]
        [Tooltip("How long the shield protects after a scan, in seconds (fade-out included).")]
        [SerializeField] float duration = 5f;
        [SerializeField] float fadeInTime = 0.25f;
        [SerializeField] float fadeOutTime = 0.4f;
        [Tooltip("The bubble flickers for this long before it runs out, in seconds.")]
        [SerializeField] float warnTime = 1.2f;
        [SerializeField] float flickerRate = 12f;

        [Header("Look")]
        [Tooltip("Material using the SpellSign/ViewEdgeGlow shader.")]
        [SerializeField] Material bubbleMaterial;

        static SigilShield active;

        GameObject bubble;
        Material material;
        float age;
        float remaining;

        ColorChanger bubbleVisual;
        public ColorChanger bubbleVisualPrefab;

        public Element element = Element.Fire;

        public static bool IsUp => active != null && active.remaining > 0f;

        // Bubble radius in meters, for effects that should stop at its edge.
        public static float Radius => active != null ? active.radius : 0f;

		public static SigilShield Instance { get; private set; }

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
            if (!IsUp) return false;

            //Debug.Log("Element comparison: " + Instance.element + " == " + element);

            if (Instance.element != element) return false;

            Camera cam = Camera.main;
            if (cam == null) return false;

            if ((point - cam.transform.position).sqrMagnitude > active.radius * active.radius) return false;

            Debug.Log("[SigilShield] blocked a shot");
            return true;
        }

        void OnEnable()
        {
            active = this;
            SigilScanner.Scanned += OnScanned;
        }

        void OnDisable()
        {
            SigilScanner.Scanned -= OnScanned;
            if (active == this) active = null;
            remaining = 0f;
            if (bubble != null) bubble.SetActive(false);
        }

        void OnDestroy()
        {
            if (bubble != null) Destroy(bubble);
            if (material != null) Destroy(material);
        }

        void OnScanned(SigilResult result) => Raise(Element.Fire);

        public void RaiseFire()
        {
            Raise(Element.Fire);
        }

		public void RaiseLightning()
		{
			Raise(Element.Lightning);
		}

		public void RaiseWater()
		{
			Raise(Element.Water);
		}

		// Puts the shield up, or tops it back up to full duration if it already is.
		[ContextMenu("Raise shield")]
        public void Raise(Element element)
        {
            if (!AttackWarningUI.Instance.visible)
            {
                Debug.Log("Can't raise shield now");
                return;
            }

            if (bubble == null && !BuildBubble()) return;

            if (remaining <= 0f) age = 0f;
            remaining = duration;
            this.element = element;
            Debug.Log($"[SigilShield] up for {duration:0.#}s");
        }

        void LateUpdate()
        {
            if (bubble == null) return;

            if (remaining <= 0f)
            {
                if (bubble.activeSelf) bubble.SetActive(false);
                return;
            }

            age += Time.deltaTime;
            remaining -= Time.deltaTime;

            Camera cam = Camera.main;
            if (cam == null || remaining <= 0f)
            {
                bubble.SetActive(false);
                bubbleVisual.SetVisibility(false);
                return;
            }

            bubble.SetActive(true);
			bubbleVisual.SetVisibility(true);
            SetBubbleVisualColor();
            
			bubble.transform.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
            bubble.transform.localScale = Vector3.one * (radius * 2f);

            /*
            float fade = Mathf.Clamp01(age / fadeInTime) * Mathf.Clamp01(remaining / fadeOutTime);
            if (remaining < warnTime && Mathf.Repeat(age * flickerRate, 1f) < 0.5f)
                fade *= 0.35f;
            material.SetFloat(FadeId, fade);
            */
        }

        private void SetBubbleVisualColor()
        {
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

                default:
                    throw new System.Exception("Unknown or incorrect element");
			}
        }

        bool BuildBubble()
        {
            if (bubbleMaterial == null)
            {
                Shader shader = Shader.Find("SpellSign/ViewEdgeGlow");
                if (shader == null)
                {
                    Debug.LogWarning("[SigilShield] no bubble material assigned and SpellSign/ViewEdgeGlow shader not found -- shield can't be shown.");
                    return false;
                }
                bubbleMaterial = new Material(shader);
            }

            if(bubbleVisual  == null)
            {
                bubbleVisual = Instantiate(bubbleVisualPrefab);
			}

            bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bubble.name = "SigilShieldBubble";
            // A collider around the head would block the player's own spells.
            Destroy(bubble.GetComponent<Collider>());

            var renderer = bubble.GetComponent<MeshRenderer>();

            //material = new Material(bubbleMaterial);
            //renderer.sharedMaterial = material;
            //renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            //renderer.receiveShadows = false;

            renderer.enabled = false;

            bubble.SetActive(false);
            return true;
        }
    }
}
