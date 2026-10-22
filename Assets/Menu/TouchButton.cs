using System;
using TMPro;
using UnityEngine;

// A button you press by touching it with a fingertip and holding there for
// PressTime -- no pinch, no ray. A bar along the bottom fills while you hold
// and drains when you let go; one touch is one press, so keeping your finger
// on it doesn't repeat.
//
// Built from code (see Create), so the menu needs no prefabs or scene setup.
[RequireComponent(typeof(BoxCollider))]
public class TouchButton : MonoBehaviour
{
    const float PressTime = 0.4f;      // seconds of continuous touch to press
    const float DrainSpeed = 3f;       // charge lost per second when not touched
    const float TouchMargin = 0.015f;  // meters from the surface that still counts
    const float BarHeight = 0.012f;

    static readonly Color DisabledPlate = new Color(0.25f, 0.25f, 0.28f, 0.9f);
    static readonly Color DisabledLabel = new Color(0.55f, 0.55f, 0.58f);
    static readonly Color BarColor = new Color(1f, 1f, 1f, 0.9f);
    static readonly Color DimPicture = new Color(0.5f, 0.5f, 0.5f, 1f); // a picture button that isn't Highlighted

    public event Action Pressed;

    // Grey and ignores touches while false.
    public bool Interactable { get; set; } = true;
    // Drawn brighter, to mark the option that is currently chosen.
    public bool Highlighted { get; set; }
    // The fingertips that can press it (both index fingers).
    public Transform[] Fingertips { get; set; } = new Transform[0];

    // The label, which can change (the language does).
    public string Text
    {
        get => label.text;
        set => label.text = value;
    }

    Collider body;
    Material plateMaterial;
    Material barMaterial;
    Transform bar;
    TextMeshPro label;
    Vector2 size;
    Color plateColor;
    Color labelColor;
    bool picture;             // shows a picture instead of a colored plate (SetPicture)
    float progress;           // 0..1
    bool waitForRelease;      // pressed already; finger must leave before the next press

    public static TouchButton Create(Transform parent, string text, Vector2 size, Color color, int sortingOrder)
    {
        var go = new GameObject(text + " Button");
        go.transform.SetParent(parent, false);

        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, 0.04f);
        box.isTrigger = true; // only fingertips press it; spells fly through (the sandbox menu is up while casting)

        var button = go.AddComponent<TouchButton>();
        button.body = box;
        button.size = size;
        button.plateColor = color;
        button.labelColor = Color.white;

        button.plateMaterial = MenuParts.NewMaterial(color);
        MenuParts.NewQuad(go.transform, "Plate", button.plateMaterial, size, Vector3.zero, sortingOrder);

        button.barMaterial = MenuParts.NewMaterial(BarColor);
        button.bar = MenuParts.NewQuad(go.transform, "Progress", button.barMaterial,
                                       new Vector2(0.0001f, BarHeight), Vector3.zero, sortingOrder + 1);

        button.label = MenuParts.NewText(go.transform, text, 0.35f, Color.white, size,
                                         new Vector3(0f, 0.004f, -0.004f), sortingOrder + 2);
        // A longer word (another language) shrinks to fit instead of running off the plate.
        button.label.enableAutoSizing = true;
        button.label.fontSizeMax = 0.35f;
        button.label.fontSizeMin = 0.2f;
        button.label.rectTransform.sizeDelta = new Vector2(size.x - 0.02f, size.y);
        return button;
    }

    // The plate shows this picture, the whole of it (a flag), with no label.
    // Drawn dimmed unless Highlighted.
    public void SetPicture(Texture2D texture)
    {
        picture = true;
        Text = "";
        if (plateMaterial != null) plateMaterial.mainTexture = texture;
    }

    void Update()
    {
        bool touched = IsTouched();
        if (!touched) waitForRelease = false;

        bool charging = Interactable && touched && !waitForRelease;
        progress += charging ? Time.deltaTime / PressTime : -Time.deltaTime * DrainSpeed;
        progress = Mathf.Clamp01(progress);

        if (charging && progress >= 1f)
        {
            waitForRelease = true;
            Pressed?.Invoke();
        }

        UpdateVisuals();
    }

    bool IsTouched()
    {
        foreach (Transform tip in Fingertips)
        {
            if (tip == null) continue;
            Vector3 p = tip.position;
            if ((body.ClosestPoint(p) - p).sqrMagnitude <= TouchMargin * TouchMargin)
                return true;
        }
        return false;
    }

    void UpdateVisuals()
    {
        if (plateMaterial == null) return; // no unlit shader (MenuParts logged it); buttons still work

        if (picture)
            plateMaterial.color = Color.Lerp(Highlighted ? Color.white : DimPicture, Color.white, progress * 0.6f);
        else
            plateMaterial.color = Interactable
                ? Color.Lerp(plateColor, Color.white, Mathf.Max(progress * 0.35f, Highlighted ? 0.5f : 0f))
                : DisabledPlate;
        label.color = Interactable ? labelColor : DisabledLabel;

        // The bar grows from the left edge along the bottom of the button.
        float width = Mathf.Max(0.0001f, size.x * progress);
        bar.localScale = new Vector3(width, BarHeight, 1f);
        bar.localPosition = new Vector3(-size.x * 0.5f + width * 0.5f,
                                        -size.y * 0.5f + BarHeight * 0.5f, -0.002f);
    }

    void OnDestroy()
    {
        if (plateMaterial != null) Destroy(plateMaterial);
        if (barMaterial != null) Destroy(barMaterial);
    }
}
