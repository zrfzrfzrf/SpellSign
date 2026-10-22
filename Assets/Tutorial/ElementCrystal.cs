using System;
using UnityEngine;

// A floating crystal of one element, for the tutorial. Only a spell of the
// element that beats it breaks it: it shatters with "Super effective!". Any
// other spell bounces off (the dragon's deflect ripple) with "No effect" (its
// own element) or "Too weak" (the third one, which hurts the dragon only a
// little) -- the same rules as the fight, made all-or-nothing.
//
// Built entirely from code (Create). Spells find it through ISpellTarget.
public class ElementCrystal : MonoBehaviour, ISpellTarget
{
    const float Height = 0.44f;          // meters, tip to tip
    const float Width = 0.24f;
    const float HitRadius = 0.22f;       // the collider spells hit
    const float SpinSpeed = 40f;         // degrees per second
    const float BobHeight = 0.04f;       // meters up and down
    const float BobRate = 0.5f;          // bobs per second
    const float HaloSize = 0.9f;         // meters across, the glow behind it
    const float AppearTime = 0.35f;      // seconds to grow in
    const float WobbleTime = 0.35f;      // seconds it shakes after a spell bounces off
    const float PulseTime = 0.8f;        // seconds a hint pulse lasts
    const float ShatterTime = 0.12f;     // seconds of white flash before it bursts
    const int Shards = 40;

    static readonly Color WeakColor = new Color(0.85f, 0.85f, 0.9f);
    static readonly Color EffectiveColor = new Color(0.94f, 0.62f, 0.15f); // as the dragon's damage numbers
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int HaloColorId = Shader.PropertyToID("_Color");

    public Element Element { get; private set; }
    public bool Broken { get; private set; }

    // Raised once, when the right spell breaks it.
    public event Action<ElementCrystal> Shattered;
    // Raised when a spell bounces off, with that spell's element.
    public event Action<ElementCrystal, Element> Bounced;

    Transform body;
    Material bodyMaterial;
    Transform halo;
    Material haloMaterial;
    Mesh mesh;
    Vector3 home;
    float age;
    float wobble;   // 1 right after a bounce, fades to 0
    float pulse;    // 1 at the start of a hint pulse, fades to 0

    public static ElementCrystal Create(Element element, Vector3 position)
    {
        var go = new GameObject(ElementLook.Name(element) + " Crystal");
        go.transform.position = position;
        var crystal = go.AddComponent<ElementCrystal>();
        crystal.Element = element;
        crystal.home = position;
        crystal.Build();
        return crystal;
    }

    // A short glow and swell, to point the player at this crystal.
    public void Pulse() => pulse = 1f;

    public void TakeHit(Element attack, Vector3? hitPoint = null, Collider struck = null)
    {
        if (Broken) return;

        Vector3 point = hitPoint ?? transform.position;
        int damage = ElementRules.Damage(attack, Element);
        if (damage == ElementRules.Effective)
        {
            Broken = true;
            DamageNumber.Spawn(TowardPlayer(point), Loc.T("crystal.effective"), EffectiveColor, 1.2f);
            StartCoroutine(Shatter());
            Shattered?.Invoke(this);
            return;
        }

        SpellSign.CombatFeedback.DeflectRipple.Spawn(point);
        DamageNumber.Spawn(TowardPlayer(point), Loc.T(damage == ElementRules.Immune ? "crystal.no_effect" : "crystal.too_weak"), WeakColor, 0.8f);
        wobble = 1f;
        Bounced?.Invoke(this, attack);
    }

    void Update()
    {
        if (body == null) return;
        age += Time.deltaTime;
        wobble = Mathf.MoveTowards(wobble, 0f, Time.deltaTime / WobbleTime);
        pulse = Mathf.MoveTowards(pulse, 0f, Time.deltaTime / PulseTime);

        if (!Broken)
        {
            float grow = Mathf.SmoothStep(0f, 1f, age / AppearTime);
            float swell = 1f + 0.25f * Mathf.Sin(pulse * Mathf.PI);
            transform.position = home + Vector3.up * (BobHeight * Mathf.Sin(age * BobRate * 2f * Mathf.PI));
            body.localScale = Vector3.one * grow * swell;
            body.localRotation = Quaternion.Euler(0f, age * SpinSpeed, 25f * wobble * Mathf.Sin(age * 40f));
        }

        Camera cam = Camera.main;
        if (cam != null && halo != null)
            halo.rotation = Quaternion.LookRotation(halo.position - cam.transform.position);
        if (haloMaterial != null)
        {
            Color c = ElementLook.Color(Element);
            c.a = Broken ? 0f : Mathf.Lerp(0.45f, 1f, Mathf.Max(pulse, 0.25f + 0.25f * Mathf.Sin(age * 3f)));
            haloMaterial.SetColor(HaloColorId, c);
            halo.localScale = Vector3.one * HaloSize * (1f + 0.4f * pulse);
        }
    }

    // A white flash, then it bursts into shards of its color and is gone.
    System.Collections.IEnumerator Shatter()
    {
        GetComponent<Collider>().enabled = false;
        for (float t = 0f; t < ShatterTime; t += Time.deltaTime)
        {
            bodyMaterial.SetColor(BaseColorId, Color.Lerp(ElementLook.Color(Element), Color.white, t / ShatterTime));
            body.localScale = Vector3.one * (1f + 0.4f * t / ShatterTime);
            yield return null;
        }

        bodyMaterial.SetColor(BaseColorId, ElementLook.Color(Element)); // the shards share this material
        BurstShards();
        body.gameObject.SetActive(false);
        Destroy(gameObject, 1.5f);
    }

    void BurstShards()
    {
        var go = new GameObject(name + " Shards");
        go.transform.position = body.position;
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = new ParticleSystem.MinMaxGradient(ElementLook.Color(Element), Color.white);
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Width * 0.5f;
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = bodyMaterial; // lit like the crystal, so the shards look like pieces of it
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        system.Emit(Shards);
        Destroy(go, 1.5f);
    }

    // Damage numbers sit a little in front of the crystal, toward the player.
    static Vector3 TowardPlayer(Vector3 point)
    {
        Camera cam = Camera.main;
        return cam != null ? point + (cam.transform.position - point).normalized * 0.15f : point;
    }

    void Build()
    {
        var hit = gameObject.AddComponent<SphereCollider>();
        hit.radius = HitRadius;

        // The crystal: a stretched octahedron, flat-shaded so each face catches the light.
        mesh = Octahedron(Width * 0.5f, Height * 0.5f);
        var bodyObject = new GameObject("Body");
        bodyObject.transform.SetParent(transform, false);
        bodyObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var bodyRenderer = bodyObject.AddComponent<MeshRenderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        bodyMaterial = new Material(lit != null ? lit : Shader.Find("Sprites/Default"));
        bodyMaterial.SetColor(BaseColorId, ElementLook.Color(Element));
        bodyMaterial.color = ElementLook.Color(Element);
        if (bodyMaterial.HasProperty("_Smoothness")) bodyMaterial.SetFloat("_Smoothness", 0.85f);
        bodyRenderer.sharedMaterial = bodyMaterial;
        bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        body = bodyObject.transform;
        body.localScale = Vector3.zero;

        // A soft glow behind it (Resources/SummonHalo, as the hands' glow).
        Shader haloShader = Resources.Load<Shader>("SummonHalo");
        if (haloShader != null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Halo";
            Destroy(quad.GetComponent<Collider>()); // spells hit the crystal, not its glow
            quad.transform.SetParent(transform, false);
            haloMaterial = new Material(haloShader);
            var quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = haloMaterial;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            halo = quad.transform;
        }
    }

    static Mesh Octahedron(float halfWidth, float halfHeight)
    {
        Vector3 top = new Vector3(0f, halfHeight, 0f), bottom = new Vector3(0f, -halfHeight, 0f);
        Vector3[] ring = { new Vector3(halfWidth, 0f, 0f), new Vector3(0f, 0f, halfWidth),
                           new Vector3(-halfWidth, 0f, 0f), new Vector3(0f, 0f, -halfWidth) };
        var vertices = new Vector3[24];
        var triangles = new int[24];
        int v = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = ring[i], b = ring[(i + 1) % 4];
            // Own vertices per face, so the normals stay flat.
            vertices[v] = top; vertices[v + 1] = b; vertices[v + 2] = a; v += 3;
            vertices[v] = bottom; vertices[v + 1] = a; vertices[v + 2] = b; v += 3;
        }
        for (int i = 0; i < 24; i++) triangles[i] = i;

        var mesh = new Mesh { name = "Crystal" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (bodyMaterial != null) Destroy(bodyMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
        if (mesh != null) Destroy(mesh);
    }
}
