using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The rule of the elements as a picture: the three elements on a ring --
// Fire at the top, Lightning bottom right, Water bottom left -- with curved
// arrows running clockwise from each to the one it beats (Water -> Fire ->
// Lightning -> Water), like a rock-paper-scissors cycle.
//
// The tutorial lights the arrows one by one as the player learns them and
// pulses one as a hint. In the fight a small one sits beside the dragon's
// health bar (EnemyHealthBars): the dragon's element is drawn big, and the
// arrow into it -- from the element that beats it -- is lit.
//
// Each element's icon is a colored badge with a white symbol, drawn from code
// (ElementIcons), unless a picture for it is in Resources/ElementIcons.
//
// Built from code. Whoever owns it places and turns it; it faces along its
// own +Z, like the menus.
public class ElementWheel : MonoBehaviour
{
    const float IconShare = 0.62f;      // icon width, as a share of the ring's radius
    const float ArcShare = 0.07f;       // arrow thickness, as a share of the radius
    const float HeadShare = 0.2f;       // arrowhead length, as a share of the radius
    const float GapDegrees = 6f;        // between an arrow's ends and the icons
    const float BackingShare = 1.62f;   // the dark disc behind it all, as a share of the radius
    const float PulseTime = 1.6f;       // seconds a hint pulse lasts
    const float PulseRate = 4f;         // flashes per second while pulsing
    const float CurrentGrow = 1.4f;     // how much bigger the dragon's own element is drawn
    const int ArcSegments = 24;
    static readonly Color UnlitArrow = new Color(1f, 1f, 1f, 0.25f);
    static readonly Color BackingColor = new Color(0.04f, 0.05f, 0.09f, 0.55f);
    static readonly Color LabelColor = new Color(0.95f, 0.95f, 1f);

    // Where each element sits on the ring, in degrees from the right, counterclockwise.
    static readonly Dictionary<Element, float> Angles = new Dictionary<Element, float>
    {
        { Element.Fire, 90f }, { Element.Lightning, -30f }, { Element.Water, 210f },
    };

    class Arrow
    {
        public Element from;
        public Material material;
        public bool lit;
        public float pulse;
    }

    class Icon
    {
        public Element element;
        public Transform transform;
        public List<Material> materials = new List<Material>();
    }

    readonly List<Arrow> arrows = new List<Arrow>();
    readonly List<Icon> icons = new List<Icon>();
    readonly List<Material> materials = new List<Material>();
    readonly List<Mesh> meshes = new List<Mesh>();
    float radius;
    bool hasCurrent;
    Element current;
    float age;

    // radius: meters from the middle to each icon. labels: the element's name
    // beside its icon. sortingOrder: the lowest layer; it uses five.
    public static ElementWheel Create(string name, float radius, bool labels, int sortingOrder)
    {
        var go = new GameObject(name);
        var wheel = go.AddComponent<ElementWheel>();
        wheel.radius = radius;
        wheel.Build(labels, sortingOrder);
        return wheel;
    }

    // An arrow is lit (in its element's color) or faint.
    public void SetArrowLit(Element from, bool lit)
    {
        foreach (Arrow arrow in arrows)
            if (arrow.from == from) arrow.lit = lit;
    }

    public void SetAllArrowsLit(bool lit)
    {
        foreach (Arrow arrow in arrows) arrow.lit = lit;
    }

    // Flashes the arrow from `from` for a moment, as a hint.
    public void PulseArrow(Element from)
    {
        foreach (Arrow arrow in arrows)
            if (arrow.from == from) arrow.pulse = 1f;
    }

    // How far the wheel reaches from its middle (its dark backing disc), for a
    // wheel of this radius. For keeping it clear of things beside it.
    public static float OuterRadius(float radius) => radius * BackingShare;

    // For the fight: the dragon's element drawn big, the arrow into it lit.
    public void ShowCurrent(Element element)
    {
        hasCurrent = true;
        current = element;
        foreach (Arrow arrow in arrows) arrow.lit = arrow.from == ElementRules.Counter(element);
    }

    void Update()
    {
        age += Time.deltaTime;

        foreach (Arrow arrow in arrows)
        {
            arrow.pulse = Mathf.MoveTowards(arrow.pulse, 0f, Time.deltaTime / PulseTime);
            Color color = arrow.lit || arrow.pulse > 0f ? ElementLook.Color(arrow.from) : UnlitArrow;
            if (arrow.pulse > 0f)
            {
                float flash = 0.5f + 0.5f * Mathf.Sin(age * PulseRate * 2f * Mathf.PI);
                color = Color.Lerp(color, Color.white, 0.6f * flash * arrow.pulse);
            }
            arrow.material.color = color;
        }

        foreach (Icon icon in icons)
        {
            bool big = hasCurrent && icon.element == current;
            bool dim = hasCurrent && !big;
            icon.transform.localScale = Vector3.one * (big ? CurrentGrow : 1f);
            foreach (Material material in icon.materials)
            {
                Color c = material.color;
                c.a = dim ? 0.55f : 1f;
                material.color = c;
            }
        }
    }

    void Build(bool labels, int order)
    {
        float iconSize = radius * IconShare;

        // A dark disc behind it all, so it reads over whatever passthrough shows.
        MenuParts.NewQuad(transform, "Backing", NewMaterial(BackingColor, ElementIcons.Disc()),
                          Vector2.one * radius * BackingShare * 2f, Vector3.forward * 0.004f, order);

        // The arrows run clockwise, from each element to the one it beats.
        float iconGap = Mathf.Rad2Deg * (iconSize * 0.5f / radius) + GapDegrees;
        foreach (Element from in new[] { Element.Water, Element.Fire, Element.Lightning })
        {
            Element to = ElementRules.Counter(ElementRules.Counter(from));
            float start = Angles[from], end = Angles[to];
            if (end > start) end -= 360f; // clockwise: the angle only goes down
            arrows.Add(BuildArrow(from, start - iconGap, end + iconGap, order + 1));
        }

        foreach (Element element in Angles.Keys)
        {
            Vector3 home = OnRing(Angles[element], radius);
            var iconRoot = new GameObject(ElementLook.Name(element)).transform;
            iconRoot.SetParent(transform, false);
            iconRoot.localPosition = home + Vector3.back * 0.002f;
            var icon = new Icon { element = element, transform = iconRoot };

            Texture2D picture = ElementIcons.Picture(element);
            if (picture != null)
            {
                Material art = NewMaterial(Color.white, picture);
                MenuParts.NewQuad(iconRoot, "Picture", art, Vector2.one * iconSize, Vector3.zero, order + 2);
                icon.materials.Add(art);
            }
            else
            {
                Material badge = NewMaterial(ElementLook.Color(element), ElementIcons.Disc());
                Material symbol = NewMaterial(Color.white, ElementIcons.Symbol(element));
                MenuParts.NewQuad(iconRoot, "Badge", badge, Vector2.one * iconSize, Vector3.zero, order + 2);
                MenuParts.NewQuad(iconRoot, "Symbol", symbol, Vector2.one * iconSize * 0.68f, Vector3.back * 0.001f, order + 3);
                icon.materials.Add(badge);
                icon.materials.Add(symbol);
            }
            icons.Add(icon);

            if (labels)
            {
                // Outside the ring: above the top icon, below the bottom two.
                float away = (iconSize * 0.5f + radius * 0.16f) * (home.y > 0f ? 1f : -1f);
                MenuParts.NewText(iconRoot, ElementLook.DisplayName(element).ToUpperInvariant(), radius * 1.4f, LabelColor,
                                  new Vector2(radius * 2f, radius * 0.4f), new Vector3(0f, away, -0.004f), order + 4);
            }
        }
    }

    // A curved arrow along the ring from `fromDegrees` down to `toDegrees`,
    // its head at the `toDegrees` end.
    Arrow BuildArrow(Element from, float fromDegrees, float toDegrees, int order)
    {
        float thickness = radius * ArcShare;
        float headDegrees = Mathf.Rad2Deg * (radius * HeadShare / radius);
        float shaftEnd = toDegrees + headDegrees;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int i = 0; i <= ArcSegments; i++)
        {
            float a = Mathf.Lerp(fromDegrees, shaftEnd, (float)i / ArcSegments);
            vertices.Add(OnRing(a, radius - thickness * 0.5f));
            vertices.Add(OnRing(a, radius + thickness * 0.5f));
            if (i == 0) continue;
            int v = vertices.Count - 4;
            triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
        }
        // The head: wide at the end of the shaft, coming to a point on the ring.
        int h = vertices.Count;
        vertices.Add(OnRing(shaftEnd, radius - thickness * 1.6f));
        vertices.Add(OnRing(shaftEnd, radius + thickness * 1.6f));
        vertices.Add(OnRing(toDegrees, radius));
        triangles.AddRange(new[] { h, h + 1, h + 2 });

        // Drawn from both sides.
        int count = triangles.Count;
        for (int i = 0; i < count; i += 3)
            triangles.AddRange(new[] { triangles[i], triangles[i + 2], triangles[i + 1] });

        var mesh = new Mesh { name = ElementLook.Name(from) + " Arrow" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        var uv = new Vector2[vertices.Count];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f);
        mesh.uv = uv;
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        Material material = NewMaterial(UnlitArrow, null);
        var go = new GameObject(mesh.name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = go.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingOrder = order;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        return new Arrow { from = from, material = material };
    }

    static Vector3 OnRing(float degrees, float distance)
    {
        float a = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * distance;
    }

    Material NewMaterial(Color color, Texture texture)
    {
        Material material = MenuParts.NewMaterial(color);
        if (texture != null) material.mainTexture = texture;
        materials.Add(material);
        return material;
    }

    void OnDestroy()
    {
        foreach (Material material in materials) if (material != null) Destroy(material);
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
    }
}

// The pictures for the elements. A PNG at Resources/ElementIcons/Water,
// Fire or Lightning (square, transparent background) is used as that
// element's whole icon. Without one, the icon is drawn from code: a white
// symbol -- a drop, a flame, a bolt -- on a badge of the element's color.
public static class ElementIcons
{
    const int Size = 128;
    const int Supersample = 3; // samples across each pixel, for smooth edges

    static readonly Dictionary<Element, Texture2D> symbols = new Dictionary<Element, Texture2D>();
    static readonly Dictionary<Element, Texture2D> pictures = new Dictionary<Element, Texture2D>();
    static readonly HashSet<Element> looked = new HashSet<Element>();
    static Texture2D disc;

    // The PNG for this element, or null when there is none.
    public static Texture2D Picture(Element element)
    {
        if (looked.Add(element))
            pictures[element] = Resources.Load<Texture2D>("ElementIcons/" + ElementLook.Name(element));
        return pictures[element];
    }

    // A white symbol for the element on a transparent background.
    public static Texture2D Symbol(Element element)
    {
        if (symbols.TryGetValue(element, out Texture2D texture) && texture != null) return texture;
        System.Func<float, float, bool> inside;
        switch (element)
        {
            case Element.Water: inside = Drop; break;
            case Element.Fire:  inside = Flame; break;
            default:            inside = Bolt; break;
        }
        texture = Draw(inside);
        symbols[element] = texture;
        return texture;
    }

    // A white disc with a smooth edge.
    public static Texture2D Disc()
    {
        if (disc == null) disc = Draw((x, y) => x * x + y * y <= 0.96f);
        return disc;
    }

    // Each shape is drawn in a square from -1 to 1, y up.

    static bool Drop(float x, float y)
    {
        // A round bottom, narrowing to a point at the top.
        const float cy = -0.3f, r = 0.55f, tip = 0.85f;
        if (x * x + (y - cy) * (y - cy) <= r * r) return true;
        if (y < cy || y > tip) return false;
        float t = (tip - y) / (tip - cy);
        return Mathf.Abs(x) <= r * Mathf.Pow(t, 1.3f);
    }

    static bool Flame(float x, float y)
    {
        // An outer flame with a smaller one cut out of its lower half.
        return FlameShape(x, y, 1f) && !FlameShape(x, (y + 0.42f) / 0.5f - 0.42f, 0.45f);
    }

    static bool FlameShape(float x, float y, float width)
    {
        const float bottom = -0.8f, top = 0.9f;
        if (y < bottom || y > top) return false;
        float t = (y - bottom) / (top - bottom);                   // 0 at the bottom, 1 at the tip
        float half = 0.62f * width * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.75f)) * (1f - 0.35f * t);
        float lean = 0.18f * Mathf.Sin(t * Mathf.PI * 1.2f) * t;    // the tip curls to one side
        return Mathf.Abs(x - lean) <= half;
    }

    static readonly Vector2[] BoltOutline =
    {
        new Vector2(0.2f, 0.95f), new Vector2(-0.5f, -0.05f), new Vector2(-0.05f, -0.05f),
        new Vector2(-0.25f, -0.95f), new Vector2(0.55f, 0.18f), new Vector2(0.08f, 0.18f),
    };

    static bool Bolt(float x, float y)
    {
        // Point in polygon: count the edges a ray to the right crosses.
        bool inside = false;
        for (int i = 0, j = BoltOutline.Length - 1; i < BoltOutline.Length; j = i++)
        {
            Vector2 a = BoltOutline[i], b = BoltOutline[j];
            if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    static Texture2D Draw(System.Func<float, float, bool> inside)
    {
        var pixels = new Color32[Size * Size];
        for (int py = 0; py < Size; py++)
        {
            for (int px = 0; px < Size; px++)
            {
                int hits = 0;
                for (int sy = 0; sy < Supersample; sy++)
                {
                    for (int sx = 0; sx < Supersample; sx++)
                    {
                        float x = (px + (sx + 0.5f) / Supersample) / Size * 2f - 1f;
                        float y = (py + (sy + 0.5f) / Supersample) / Size * 2f - 1f;
                        if (inside(x, y)) hits++;
                    }
                }
                byte alpha = (byte)(255 * hits / (Supersample * Supersample));
                pixels[py * Size + px] = new Color32(255, 255, 255, alpha);
            }
        }
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}
