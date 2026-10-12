using UnityEngine;

namespace SpellSign.CombatFeedback
{
    // One sleek health bar: a quad drawn by the SpellSign/HealthBar shader.
    // Feed it the health fraction every frame with SetFraction(). When health
    // drops, the lost part stays behind as a white trail for a moment, then
    // drains away; when health goes up (e.g. the demo refill) it snaps.
    //
    // Created from code by EnemyHealthBars / PlayerHudHealthBar.
    public class HealthBarView : MonoBehaviour
    {
        const float TrailDelay = 0.45f;  // seconds the trail waits before draining
        const float TrailSpeed = 0.6f;   // fraction of the bar per second

        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int TrailId = Shader.PropertyToID("_Trail");
        static readonly int SectionsId = Shader.PropertyToID("_Sections");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");

        Material material;
        float fill = 1f;
        float trail = 1f;
        float trailWait;

        // `template` is a material using SpellSign/HealthBar. With alwaysOnTop
        // the bar is never hidden behind geometry (e.g. the dragon's wings).
        public static HealthBarView Create(string name, Material template, Color fillColor, bool alwaysOnTop)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());

            var view = go.AddComponent<HealthBarView>();
            view.material = new Material(template);
            view.material.SetColor(FillColorId, fillColor);
            view.material.SetFloat(ZTestId, (float)(alwaysOnTop
                ? UnityEngine.Rendering.CompareFunction.Always
                : UnityEngine.Rendering.CompareFunction.LessEqual));

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = view.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return view;
        }

        // Splits the bar into equal parts with a thin mark between them. 1 = no marks.
        public void SetSections(int sections)
        {
            material.SetFloat(SectionsId, Mathf.Max(1, sections));
        }

        public void SetFraction(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (fraction < fill)
            {
                // Keep the trail where it was (or at the old fill) and restart the wait.
                trail = Mathf.Max(trail, fill);
                trailWait = TrailDelay;
            }
            else if (fraction > fill)
            {
                trail = fraction;
            }
            fill = fraction;
        }

        void Update()
        {
            if (trail > fill)
            {
                if (trailWait > 0f) trailWait -= Time.deltaTime;
                else trail = Mathf.MoveTowards(trail, fill, TrailSpeed * Time.deltaTime);
            }

            Vector3 size = transform.lossyScale;
            material.SetFloat(AspectId, size.y > 0f ? size.x / size.y : 1f);
            material.SetFloat(FillId, fill);
            material.SetFloat(TrailId, trail);
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
