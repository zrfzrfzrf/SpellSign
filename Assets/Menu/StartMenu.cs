using System;
using TMPro;
using UnityEngine;

// The menu the game opens with: a panel in front of the player with the
// game's name and a PLAY touch button. Nothing happens until it's pressed --
// GameFlow keeps the dragon away and spell casting off, and starts the fight
// on PlayPressed. With Show Tutorial on there is a TUTORIAL button beside it
// (TutorialPressed), and with Show Sandbox a SANDBOX button below, for the
// practice mode (SandboxPressed).
//
// Built from code like EndMenu. GameFlow adds this component itself, so it
// needs no scene setup.
public class StartMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    const float NarrowWidth = 0.44f;   // panel width with one button per row
    const float WideWidth = 0.56f;     // ...with two side by side
    const float OneRowHeight = 0.3f;
    const float RowSpacing = 0.1f;
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f);
    static readonly Color PlayColor = new Color(0.25f, 0.65f, 0.4f, 0.95f);
    static readonly Color SandboxColor = new Color(0.2f, 0.45f, 0.9f, 0.95f);
    static readonly Color TutorialColor = new Color(0.55f, 0.35f, 0.85f, 0.95f);

    [Tooltip("Both hands. Their index fingertips are what presses the button.")]
    public HandGestureRecognizer[] hands;

    [Header("Placement")]
    [Tooltip("How far in front of the player's head the panel appears, in meters. Keep it within arm's reach.")]
    public float distance = 0.65f;
    [Tooltip("How far below eye level the panel's center sits, in meters.")]
    public float heightBelowEyes = 0.2f;

    [Tooltip("Adds the TUTORIAL button. Set before the menu is first shown.")]
    public bool showTutorial = false;
    [Tooltip("Adds the SANDBOX button. Set before the menu is first shown.")]
    public bool showSandbox = false;

    public event Action PlayPressed;
    public event Action TutorialPressed;
    public event Action SandboxPressed;

    Transform panel;
    MenuFollow follow; // moves the panel back in front of a player who walks or turns away
    Material panelMaterial;
    readonly System.Collections.Generic.List<TouchButton> buttons = new System.Collections.Generic.List<TouchButton>();

    public void Show()
    {
        if (panel == null) Build();

        Transform[] tips = MenuParts.IndexTips(hands);
        foreach (TouchButton button in buttons) button.Fingertips = tips;
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

        // TUTORIAL beside PLAY; SANDBOX on a row of its own below. Without
        // the tutorial, PLAY and SANDBOX share the row.
        var rows = new System.Collections.Generic.List<System.Collections.Generic.List<(string, Color, Action)>>();
        var first = new System.Collections.Generic.List<(string, Color, Action)>();
        if (showTutorial) first.Add(("TUTORIAL", TutorialColor, () => TutorialPressed?.Invoke()));
        first.Add(("PLAY", PlayColor, () => PlayPressed?.Invoke()));
        rows.Add(first);
        if (showSandbox)
        {
            var sandbox = ("SANDBOX", SandboxColor, (Action)(() => SandboxPressed?.Invoke()));
            if (first.Count < 2) first.Add(sandbox);
            else rows.Add(new System.Collections.Generic.List<(string, Color, Action)> { sandbox });
        }

        bool wide = false;
        foreach (var row in rows) if (row.Count > 1) wide = true;
        var size = new Vector2(wide ? WideWidth : NarrowWidth, OneRowHeight + RowSpacing * (rows.Count - 1));
        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, size, Vector3.zero, Order);

        float titleY = size.y * 0.5f - 0.075f;
        MenuParts.NewText(panel, "SPELLSIGN", 0.7f, TitleColor, new Vector2(size.x, 0.1f),
                          new Vector3(0f, titleY, -0.004f), Order + 4);

        for (int r = 0; r < rows.Count; r++)
        {
            for (int b = 0; b < rows[r].Count; b++)
            {
                var (label, color, pressed) = rows[r][b];
                TouchButton button = TouchButton.Create(panel, label, ButtonSize, color, Order + 1);
                float x = rows[r].Count > 1 ? (b == 0 ? -0.13f : 0.13f) : 0f;
                button.transform.localPosition = new Vector3(x, titleY - 0.14f - RowSpacing * r, 0f);
                button.Pressed += pressed;
                buttons.Add(button);
            }
        }
    }

    void OnDestroy()
    {
        if (panel != null) Destroy(panel.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
    }
}
