using System.Collections.Generic;
using UnityEngine;

namespace SpellSign.CombatFeedback
{
    // Puts a health bar above every active EnemyTarget (the dragon), facing the
    // player. It sits just over the enemy's head (or over the top of its model,
    // if it has no Head Collider) and follows it as it moves or animates. Drawn
    // on top of everything so wings can't hide it.
    //
    // Replaces the boss bar that used to be stuck in the HUD. Lives on the
    // CombatFeedback prefab; needs the HealthBar material.
    public class EnemyHealthBars : MonoBehaviour
    {
        [Tooltip("Material using the SpellSign/HealthBar shader.")]
        [SerializeField] Material barMaterial;
        [SerializeField] Color fillColor = new Color(0.89f, 0.29f, 0.29f);

        [Tooltip("Bar size in meters.")]
        [SerializeField] Vector2 size = new Vector2(0.8f, 0.08f);
        [Tooltip("Gap between the top of the enemy's head (or model) and the bar, in meters.")]
        [SerializeField] float gap = 0.05f;
        [Tooltip("How quickly the bar follows the model's height as it animates, in seconds.")]
        [SerializeField] float heightSmoothing = 0.25f;

        class Tracked
        {
            public EnemyTarget enemy;
            public HealthBarView bar;
            public float height;
            public float heightVelocity;
        }

        readonly List<Tracked> tracked = new List<Tracked>();

        void Start()
        {
            if (barMaterial == null)
            {
                Debug.LogWarning("[EnemyHealthBars] no bar material assigned -- no enemy health bars.");
                return;
            }

            // Inactive ones too: the dragon is switched off until Play is pressed.
            foreach (EnemyTarget enemy in FindObjectsByType<EnemyTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                HealthBarView bar = HealthBarView.Create(enemy.name + " HealthBar", barMaterial, fillColor, true);
                bar.transform.localScale = new Vector3(size.x, size.y, 1f);
                tracked.Add(new Tracked
                {
                    enemy = enemy,
                    bar = bar,
                    height = float.NaN
                });
            }
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;

            foreach (Tracked t in tracked)
            {
                if (t.bar == null) continue;

                Bounds bounds = default;
                bool show = t.enemy != null && t.enemy.isActiveAndEnabled && t.enemy.TryGetVisibleBounds(out bounds);
                t.bar.gameObject.SetActive(show);
                if (!show) continue;

                if (t.enemy.TryGetHeadBounds(out Bounds head)) bounds = head;

                float top = bounds.max.y + gap;
                t.height = float.IsNaN(t.height) ? top
                    : Mathf.SmoothDamp(t.height, top, ref t.heightVelocity, heightSmoothing);

                Vector3 position = new Vector3(bounds.center.x, t.height, bounds.center.z);
                t.bar.transform.position = position;
                if (cam != null)
                    t.bar.transform.rotation = Quaternion.LookRotation(position - cam.transform.position);

                t.bar.SetFraction((float)t.enemy.CurrentHealth / t.enemy.maxHealth);
            }
        }

        void OnDestroy()
        {
            foreach (Tracked t in tracked)
                if (t.bar != null) Destroy(t.bar.gameObject);
        }
    }
}
