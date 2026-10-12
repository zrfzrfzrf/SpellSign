using System;
using TMPro;
using UnityEngine;

// The menu the game opens with: a panel in front of the player with the
// game's name and a PLAY touch button. Nothing happens until it's pressed --
// GameFlow keeps the dragon away and spell casting off, and starts the fight
// on PlayPressed. With Show Sandbox on there is a SANDBOX button beside it,
// for the practice mode (SandboxPressed).
//
// Built from code like EndMenu. GameFlow adds this component itself, so it
// needs no scene setup.
public class StartMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    static readonly Vector2 PanelSize = new Vector2(0.44f, 0.3f);
    static readonly Vector2 WidePanelSize = new Vector2(0.56f, 0.3f); // with two buttons side by side
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f);
    static readonly Color PlayColor = new Color(0.25f, 0.65f, 0.4f, 0.95f);
    static readonly Color SandboxColor = new Color(0.2f, 0.45f, 0.9f, 0.95f);

    [Tooltip("Both hands. Their index fingertips are what presses the button.")]
    public HandGestureRecognizer[] hands;

    [Header("Placement")]
    [Tooltip("How far in front of the player's head the panel appears, in meters. Keep it within arm's reach.")]
    public float distance = 0.65f;
    [Tooltip("How far below eye level the panel's center sits, in meters.")]
    public float heightBelowEyes = 0.2f;

    [Tooltip("Adds the SANDBOX button. Set before the menu is first shown.")]
    public bool showSandbox = false;

    public event Action PlayPressed;
    public event Action SandboxPressed;

    Transform panel;
    MenuFollow follow; // moves the panel back in front of a player who walks or turns away
    Material panelMaterial;
    TouchButton playButton;
    TouchButton sandboxButton;

    public void Show()
    {
        if (panel == null) Build();

        playButton.Fingertips = MenuParts.IndexTips(hands);
        if (sandboxButton != null) sandboxButton.Fingertips = playButton.Fingertips;
        follow.distance = distance;
        follow.heightBelowEyes = heightBelowEyes;
        MenuParts.PlaceInFrontOfPlayer(panel, distance, heightBelowEyes);
        panel.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void Build()
    {
        panel = new GameObject("Start Menu Panel").transform;
        follow = panel.gameObject.AddComponent<MenuFollow>();

        Vector2 size = showSandbox ? WidePanelSize : PanelSize;
        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, size, Vector3.zero, Order);

        MenuParts.NewText(panel, "SPELLSIGN", 0.7f, TitleColor, new Vector2(size.x, 0.1f),
                          new Vector3(0f, 0.075f, -0.004f), Order + 4);

        playButton = TouchButton.Create(panel, "PLAY", ButtonSize, PlayColor, Order + 1);
        playButton.transform.localPosition = new Vector3(showSandbox ? -0.13f : 0f, -0.065f, 0f);
        playButton.Pressed += () => PlayPressed?.Invoke();

        if (!showSandbox) return;
        sandboxButton = TouchButton.Create(panel, "SANDBOX", ButtonSize, SandboxColor, Order + 1);
        sandboxButton.transform.localPosition = new Vector3(0.13f, -0.065f, 0f);
        sandboxButton.Pressed += () => SandboxPressed?.Invoke();
    }

    void OnDestroy()
    {
        if (panel != null) Destroy(panel.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
    }
}
