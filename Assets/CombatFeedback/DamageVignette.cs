using UnityEngine;

namespace SpellSign.CombatFeedback
{
    // When the player takes damage, the edges of their view pulse red for a
    // moment while the centre stays clear. It's a small sphere around the head
    // drawn with the SpellSign/ViewEdgeGlow shader (the same edge glow the sigil
    // shield uses), coloured by the assigned material.
    //
    // Listens to PlayerHealth.Damaged. Lives on the CombatFeedback prefab.
    public class DamageVignette : MonoBehaviour
    {
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Tooltip("Material using the SpellSign/ViewEdgeGlow shader, coloured red.")]
        [SerializeField] Material vignetteMaterial;
        [Tooltip("Seconds at full strength before fading.")]
        [SerializeField] float holdTime = 0.08f;
        [Tooltip("Seconds to fade out.")]
        [SerializeField] float fadeTime = 0.5f;

        const float Radius = 0.3f; // meters; inside everything except the player's hands

        PlayerHealth player;
        GameObject sphere;
        Material material;
        float age = float.MaxValue;

        void OnEnable()
        {
            if (player == null) player = FindFirstObjectByType<PlayerHealth>();
            if (player != null) player.Damaged += OnDamaged;
            else Debug.LogWarning("[DamageVignette] no PlayerHealth in the scene.");
        }

        void OnDisable()
        {
            if (player != null) player.Damaged -= OnDamaged;
            age = float.MaxValue;
            if (sphere != null) sphere.SetActive(false);
        }

        void OnDestroy()
        {
            if (sphere != null) Destroy(sphere);
            if (material != null) Destroy(material);
        }

        void OnDamaged(int amount)
        {
            if (sphere == null && !BuildSphere()) return;
            age = 0f;
        }

        void LateUpdate()
        {
            if (sphere == null) return;

            age += Time.deltaTime;
            float fade = age < holdTime ? 1f : 1f - (age - holdTime) / fadeTime;
            Camera cam = Camera.main;
            if (fade <= 0f || cam == null)
            {
                if (sphere.activeSelf) sphere.SetActive(false);
                return;
            }

            sphere.SetActive(true);
            sphere.transform.SetPositionAndRotation(cam.transform.position, Quaternion.identity);
            material.SetFloat(FadeId, fade * fade); // ease out
        }

        bool BuildSphere()
        {
            if (vignetteMaterial == null)
            {
                Debug.LogWarning("[DamageVignette] no vignette material assigned.");
                return false;
            }

            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "DamageVignette";
            // A collider around the head would block the player's own spells.
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.localScale = Vector3.one * (Radius * 2f);

            var renderer = sphere.GetComponent<MeshRenderer>();
            material = new Material(vignetteMaterial);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            sphere.SetActive(false);
            return true;
        }
    }
}
