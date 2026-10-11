using UnityEngine;

namespace SpellSign.CombatFeedback
{
    // The player's health bar in the HUD, restyled as a sleek bar with a damage
    // trail. It takes over the spot and colour of PlayerHealth's original bar
    // Image (healthBarFill), which is switched off in the scene along with its
    // background, so moving that Image in the editor still moves this bar.
    //
    // Only shown during the fight: hidden while the start menu or the end menu
    // is up.
    //
    // Lives on the CombatFeedback prefab; needs the HealthBar material.
    public class PlayerHudHealthBar : MonoBehaviour
    {
        [Tooltip("Material using the SpellSign/HealthBar shader.")]
        [SerializeField] Material barMaterial;

        PlayerHealth player;
        GameFlow flow;
        HealthBarView bar;

        void Start()
        {
            player = FindFirstObjectByType<PlayerHealth>();
            flow = FindFirstObjectByType<GameFlow>(); // optional: without one the bar always shows
            if (player == null || player.healthBarFill == null || barMaterial == null)
            {
                Debug.LogWarning("[PlayerHudHealthBar] needs a PlayerHealth with a Health Bar Fill image, and the bar material.");
                enabled = false;
                return;
            }

            RectTransform spot = player.healthBarFill.rectTransform;
            var corners = new Vector3[4];
            spot.GetWorldCorners(corners); // bottom-left, top-left, top-right, bottom-right

            Color color = player.healthBarFill.color;
            color.a = 1f;
            bar = HealthBarView.Create("PlayerHudHealthBar", barMaterial, color, false);

            // Parent to the HUD canvas so it moves with the head, then place and
            // size it over the original bar's rectangle.
            Transform canvas = spot.parent;
            bar.transform.SetParent(canvas, false);
            bar.transform.SetPositionAndRotation((corners[0] + corners[2]) * 0.5f, spot.rotation);
            Vector3 parentScale = canvas.lossyScale;
            bar.transform.localScale = new Vector3(
                Vector3.Distance(corners[0], corners[3]) / parentScale.x,
                Vector3.Distance(corners[0], corners[1]) / parentScale.y,
                1f);
        }

        void Update()
        {
            bool fighting = flow == null || flow.Current == GameFlow.State.Playing;
            if (bar.gameObject.activeSelf != fighting) bar.gameObject.SetActive(fighting);

            bar.SetFraction(player.maxHealth > 0 ? (float)player.CurrentHealth / player.maxHealth : 0f);
        }

        void OnDestroy()
        {
            if (bar != null) Destroy(bar.gameObject);
        }
    }
}
