using System;
using TMPro;
using UnityEngine;

// The end-of-level menu: a panel that appears in front of the player with the
// result (won, lost, or the fight's time ran out) and two touch buttons: RESTART, and MENU back to the start menu (with
// its tutorial and sandbox). A NEXT LEVEL button joins them, on a row below,
// once there is a next level. It stays where it appeared (not stuck to the
// head), within arm's reach, upright and facing the player -- until the player
// walks or turns away from it, when it glides back in front of them.
//
// The panel is built from code the first time it's shown; this component only
// needs the hands, whose index fingertips press the buttons.
public class EndMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    static readonly Vector2 PanelSize = new Vector2(0.56f, 0.32f);
    const float NextLevelRow = 0.1f; // the panel grows by this for the NEXT LEVEL row
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color WonColor = new Color(0.45f, 0.95f, 0.55f);
    static readonly Color LostColor = new Color(1f, 0.35f, 0.3f);
    static readonly Color TimeUpColor = new Color(1f, 0.8f, 0.3f);

    public enum Result { Won, Lost, TimeUp }

    [Tooltip("Both hands. Their index fingertips are what presses the buttons.")]
    public HandGestureRecognizer[] hands;

    [Header("Placement")]
    [Tooltip("How far in front of the player's head the panel appears, in meters. Keep it within arm's reach.")]
    public float distance = 0.65f;
    [Tooltip("How far below eye level the panel's center sits, in meters.")]
    public float heightBelowEyes = 0.2f;

    [Header("Levels")]
    [Tooltip("Turn on once there is a next level: adds the NEXT LEVEL button, which works after a win. Set before the menu is first shown.")]
    public bool nextLevelAvailable = false;

    public event Action RestartPressed;
    public event Action MenuPressed;
    public event Action NextLevelPressed;

    Transform panel;
    MenuFollow follow; // moves the panel back in front of a player who walks or turns away
    Material panelMaterial;
    TextMeshPro title;
    TouchButton restartButton;
    TouchButton menuButton;
    TouchButton nextLevelButton; // only once there is a next level

    public void Show(Result result)
    {
        if (panel == null) Build();

        bool won = result == Result.Won;
        title.text = Loc.T(won ? "end.victory" : result == Result.TimeUp ? "end.time_up" : "end.defeat");
        restartButton.Text = Loc.T("end.restart");
        menuButton.Text = Loc.T("end.menu");
        if (nextLevelButton != null) nextLevelButton.Text = Loc.T("end.next_level");
        title.color = won ? WonColor : result == Result.TimeUp ? TimeUpColor : LostColor;
        Transform[] tips = MenuParts.IndexTips(hands);
        restartButton.Fingertips = tips;
        menuButton.Fingertips = tips;
        if (nextLevelButton != null)
        {
            nextLevelButton.Interactable = won;
            nextLevelButton.Fingertips = tips;
        }

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
        panel = new GameObject("End Menu Panel").transform;
        follow = panel.gameObject.AddComponent<MenuFollow>();

        Vector2 size = PanelSize + (nextLevelAvailable ? Vector2.up * NextLevelRow : Vector2.zero);
        float titleY = size.y * 0.5f - 0.07f;
        float buttonsY = titleY - 0.155f;
        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, size, Vector3.zero, Order);

        title = MenuParts.NewText(panel, "TITLE", 0.8f, Color.white, new Vector2(size.x, 0.1f),
                                  new Vector3(0f, titleY, -0.004f), Order + 4);

        restartButton = TouchButton.Create(panel, "RESTART", ButtonSize,
                                           new Color(0.2f, 0.45f, 0.9f, 0.95f), Order + 1);
        restartButton.transform.localPosition = new Vector3(-0.13f, buttonsY, 0f);
        restartButton.Pressed += () => RestartPressed?.Invoke();

        menuButton = TouchButton.Create(panel, "MENU", ButtonSize,
                                        new Color(0.4f, 0.42f, 0.5f, 0.95f), Order + 1);
        menuButton.transform.localPosition = new Vector3(0.13f, buttonsY, 0f);
        menuButton.Pressed += () => MenuPressed?.Invoke();

        if (!nextLevelAvailable) return;
        nextLevelButton = TouchButton.Create(panel, "NEXT LEVEL", ButtonSize,
                                             new Color(0.25f, 0.65f, 0.4f, 0.95f), Order + 1);
        nextLevelButton.transform.localPosition = new Vector3(0f, buttonsY - NextLevelRow, 0f);
        nextLevelButton.Pressed += () => NextLevelPressed?.Invoke();
    }

    void OnDestroy()
    {
        if (panel != null) Destroy(panel.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
    }
}
