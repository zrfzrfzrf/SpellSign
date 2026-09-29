using UnityEngine;

namespace SpellSign.CombatFeedback
{
    // "That did nothing": shown where a spell hits an enemy that's immune to its
    // element. A few pale-blue rings ripple out from the hit point, facing the
    // player, while the spell fizzles into grey sparks that bounce back off it.
    //
    // Built entirely from code (Sprites/Default, which is always in builds), so
    // it needs no prefab: EnemyTarget calls DeflectRipple.Spawn(point).
    public class DeflectRipple : MonoBehaviour
    {
        const int RingCount = 3;
        const float RingStagger = 0.08f;     // seconds between rings
        const float RingTime = 0.45f;        // seconds each ring lives
        const float StartRadius = 0.05f;     // meters
        const float EndRadius = 0.25f;
        const float RingWidth = 0.015f;
        const int RingSegments = 40;
        const float TowardViewer = 0.08f;    // lift off the surface so it isn't buried in the model
        const int SparkCount = 14;

        static readonly Color RingColor = new Color(0.52f, 0.72f, 0.92f);  // pale blue
        static readonly Color SparkColor = new Color(0.71f, 0.70f, 0.66f); // grey

        static Material sharedMaterial;

        LineRenderer[] rings;
        float age;

        public static void Spawn(Vector3 point)
        {
            Camera cam = Camera.main;
            Vector3 toViewer = cam != null ? (cam.transform.position - point).normalized : Vector3.back;

            var go = new GameObject("DeflectRipple");
            go.transform.SetPositionAndRotation(point + toViewer * TowardViewer, Quaternion.LookRotation(-toViewer));

            var ripple = go.AddComponent<DeflectRipple>();
            ripple.BuildRings();
            ripple.EmitSparks(toViewer);
        }

        static Material SharedMaterial()
        {
            if (sharedMaterial == null) sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            return sharedMaterial;
        }

        void BuildRings()
        {
            rings = new LineRenderer[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                var ringGo = new GameObject("Ring" + i);
                ringGo.transform.SetParent(transform, false);

                LineRenderer line = ringGo.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = RingSegments;
                line.material = SharedMaterial();
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.enabled = false;
                for (int s = 0; s < RingSegments; s++)
                {
                    float a = s * Mathf.PI * 2f / RingSegments;
                    line.SetPosition(s, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
                }
                rings[i] = line;
            }
        }

        void EmitSparks(Vector3 toViewer)
        {
            var sparksGo = new GameObject("Sparks");
            sparksGo.transform.SetParent(transform, false);
            // Particles fly along the system's forward: back toward the player,
            // spread out in a wide cone, then fall.
            sparksGo.transform.rotation = Quaternion.LookRotation(toViewer);

            ParticleSystem sparks = sparksGo.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = sparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.016f);
            main.startColor = SparkColor;
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = sparks.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule fade = sparks.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = sparksGo.GetComponent<ParticleSystemRenderer>();
            renderer.material = SharedMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            sparks.Play();
            sparks.Emit(SparkCount);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= RingStagger * (RingCount - 1) + RingTime + 0.3f) // + spark lifetime
            {
                Destroy(gameObject);
                return;
            }

            for (int i = 0; i < RingCount; i++)
            {
                float t = (age - i * RingStagger) / RingTime;
                LineRenderer line = rings[i];
                line.enabled = t >= 0f && t < 1f;
                if (!line.enabled) continue;

                float easeOut = 1f - (1f - t) * (1f - t);
                float radius = Mathf.Lerp(StartRadius, EndRadius, easeOut);
                line.transform.localScale = Vector3.one * radius;
                line.widthMultiplier = RingWidth * (1f - 0.6f * t);

                Color c = RingColor;
                c.a = (1f - t) * (i == 0 ? 1f : 0.7f);
                line.startColor = c;
                line.endColor = c;
            }
        }
    }
}
