using System;
using TMPro;
using UnityEngine;

// The menu the game opens with: a panel in front of the player with the
// game's name and a PLAY touch button. Nothing happens until it's pressed --
// GameFlow keeps the dragon away and spell casting off, and starts the fight
// on PlayPressed. With Show Tutorial on there is a TUTORIAL button beside it
// (TutorialPressed), and with Show Sandbox a SANDBOX button below, for the
// practice mode (SandboxPressed). Below them, WALLS BLOCK and THROUGH WALLS:
// whether the player's spells stop at the scanned room or fly through it
// (RoomWalls), the chosen one lit. Along the bottom, a flag for each language
// (Loc): touch one to switch every text in the game to it.
//
// Built from code like EndMenu. GameFlow adds this component itself, so it
// needs no scene setup.
public class StartMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    const float WideWidth = 0.56f;     // panel width: two buttons side by side
    const float OneRowHeight = 0.3f;
    const float RowSpacing = 0.1f;
    const float FlagRow = 0.09f;       // the panel grows by this for the flags
    const float FlagHeight = 0.06f;
    const float FlagSpacing = 0.04f;   // between two flags
    const float FrameWidth = 0.008f;   // the frame around the chosen flag
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f);
    static readonly Color PlayColor = new Color(0.25f, 0.65f, 0.4f, 0.95f);
    static readonly Color SandboxColor = new Color(0.2f, 0.45f, 0.9f, 0.95f);
    static readonly Color TutorialColor = new Color(0.55f, 0.35f, 0.85f, 0.95f);
    static readonly Color WallsColor = new Color(0.2f, 0.45f, 0.9f, 0.95f); // as the sandbox's mode buttons
    static readonly Color FrameColor = new Color(1f, 0.85f, 0.35f);

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
    Material frameMaterial;
    Transform frame; // behind the chosen flag
    TouchButton wallsBlock, throughWalls;
    readonly System.Collections.Generic.List<TouchButton> buttons = new System.Collections.Generic.List<TouchButton>();
    readonly System.Collections.Generic.Dictionary<TouchButton, string> textKeys = new System.Collections.Generic.Dictionary<TouchButton, string>();
    readonly System.Collections.Generic.Dictionary<TouchButton, string> flags = new System.Collections.Generic.Dictionary<TouchButton, string>(); // button -> language

    public void Show()
    {
        if (panel == null) Build();
        ShowLanguage();
        ShowWalls();

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
        // Each button: its text's key in Translations.csv, color, action.
        var rows = new System.Collections.Generic.List<System.Collections.Generic.List<(string, Color, Action)>>();
        var first = new System.Collections.Generic.List<(string, Color, Action)>();
        if (showTutorial) first.Add(("menu.tutorial", TutorialColor, () => TutorialPressed?.Invoke()));
        first.Add(("menu.play", PlayColor, () => PlayPressed?.Invoke()));
        rows.Add(first);
        if (showSandbox)
        {
            var sandbox = ("menu.sandbox", SandboxColor, (Action)(() => SandboxPressed?.Invoke()));
            if (first.Count < 2) first.Add(sandbox);
            else rows.Add(new System.Collections.Generic.List<(string, Color, Action)> { sandbox });
        }

        // The walls choice: one more row, of two buttons.
        var size = new Vector2(WideWidth, OneRowHeight + RowSpacing * rows.Count + FlagRow);
        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, size, Vector3.zero, Order);

        float titleY = size.y * 0.5f - 0.075f;
        MenuParts.NewText(panel, "SPELLSIGN", 0.7f, TitleColor, new Vector2(size.x, 0.1f),
                          new Vector3(0f, titleY, -0.004f), Order + 4);

        for (int r = 0; r < rows.Count; r++)
        {
            for (int b = 0; b < rows[r].Count; b++)
            {
                var (key, color, pressed) = rows[r][b];
                TouchButton button = TouchButton.Create(panel, Loc.T(key), ButtonSize, color, Order + 1);
                float x = rows[r].Count > 1 ? (b == 0 ? -0.13f : 0.13f) : 0f;
                button.transform.localPosition = new Vector3(x, titleY - 0.14f - RowSpacing * r, 0f);
                button.Pressed += pressed;
                buttons.Add(button);
                textKeys[button] = key;
            }
        }

        float wallsY = titleY - 0.14f - RowSpacing * rows.Count;
        wallsBlock = WallsButton("menu.walls_block", -0.13f, wallsY, false);
        throughWalls = WallsButton("menu.through_walls", 0.13f, wallsY, true);

        BuildFlags(wallsY - 0.095f);
        Loc.Changed += ShowLanguage;
        RoomWalls.Changed += ShowWalls;
    }

    TouchButton WallsButton(string key, float x, float y, bool passThrough)
    {
        TouchButton button = TouchButton.Create(panel, Loc.T(key), ButtonSize, WallsColor, Order + 1);
        button.transform.localPosition = new Vector3(x, y, 0f);
        button.Pressed += () => RoomWalls.SpellsPassThrough = passThrough;
        buttons.Add(button);
        textKeys[button] = key;
        return button;
    }

    // Lights the walls choice that is on.
    void ShowWalls()
    {
        if (wallsBlock == null) return;
        wallsBlock.Highlighted = !RoomWalls.SpellsPassThrough;
        throughWalls.Highlighted = RoomWalls.SpellsPassThrough;
    }

    // The flags side by side and centred, with a frame for the chosen one
    // drawn behind it.
    void BuildFlags(float y)
    {
        var languages = Loc.Languages;
        float total = -FlagSpacing;
        foreach (string language in languages) total += FlagHeight * Flags.Aspect(language) + FlagSpacing;

        frameMaterial = MenuParts.NewMaterial(FrameColor);
        frame = MenuParts.NewQuad(panel, "Chosen Language Frame", frameMaterial, Vector2.one, Vector3.zero, Order + 1);

        float x = -total * 0.5f;
        foreach (string language in languages)
        {
            var flagSize = new Vector2(FlagHeight * Flags.Aspect(language), FlagHeight);
            TouchButton flag = TouchButton.Create(panel, language, flagSize, Color.white, Order + 2);
            flag.SetPicture(Flags.Picture(language));
            flag.transform.localPosition = new Vector3(x + flagSize.x * 0.5f, y, 0f);
            string chosen = language;
            flag.Pressed += () => Loc.Set(chosen);
            buttons.Add(flag);
            flags[flag] = language;
            x += flagSize.x + FlagSpacing;
        }
    }

    // Button texts in the current language, and the frame on its flag.
    void ShowLanguage()
    {
        foreach (var pair in textKeys) pair.Key.Text = Loc.T(pair.Value);
        foreach (var pair in flags)
        {
            bool chosen = pair.Value == Loc.Current;
            pair.Key.Highlighted = chosen;
            if (!chosen) continue;
            Vector3 at = pair.Key.transform.localPosition;
            Vector3 flagSize = pair.Key.GetComponent<BoxCollider>().size;
            frame.localPosition = new Vector3(at.x, at.y, 0.001f);
            frame.localScale = new Vector3(flagSize.x + FrameWidth * 2f, flagSize.y + FrameWidth * 2f, 1f);
        }
    }

    void OnDestroy()
    {
        Loc.Changed -= ShowLanguage;
        RoomWalls.Changed -= ShowWalls;
        if (panel != null) Destroy(panel.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
        if (frameMaterial != null) Destroy(frameMaterial);
    }
}
