using TMPro;
using UnityEngine;

// Small builders for the end menu, which is made entirely from code: flat
// unlit quads and TMP labels. Sorting orders keep the layers in a fixed
// back-to-front order (transparent objects are otherwise sorted by distance,
// which flips when you look at the panel from the side).
public static class MenuParts
{
    public static Material NewMaterial(Color color)
    {
        // Sprites/Default is always included in builds; Unlit is the fallback.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError("[MenuParts] No unlit shader found -- the menu won't draw.");
            return null;
        }

        var material = new Material(shader);
        material.color = color;
        return material;
    }

    public static Transform NewQuad(Transform parent, string name, Material material,
                                    Vector2 size, Vector3 localPosition, int sortingOrder)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(quad.GetComponent<Collider>()); // only the buttons' own colliders count
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        var meshRenderer = quad.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return quad.transform;
    }

    // fontSize is in TMP's world-text units: about 0.1 m of line height per 1.0.
    public static TextMeshPro NewText(Transform parent, string text, float fontSize, Color color,
                                      Vector2 area, Vector3 localPosition, int sortingOrder)
    {
        var go = new GameObject(text + " Text");
        go.transform.SetParent(parent, false);

        var tmp = go.AddComponent<TextMeshPro>(); // swaps the Transform for a RectTransform
        go.transform.localPosition = localPosition;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = area;
        go.GetComponent<Renderer>().sortingOrder = sortingOrder;
        return tmp;
    }
}
