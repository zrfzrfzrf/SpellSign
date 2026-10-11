using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The end-of-level menu: a panel that appears in front of the player with the
// result and two touch buttons. It stays where it appeared (not stuck to the
// head), within arm's reach, upright and facing the player.
//
// The panel is built from code the first time it's shown; this component only
// needs the hands, whose index fingertips press the buttons.
public class EndMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    static readonly Vector2 PanelSize = new Vector2(0.56f, 0.32f);
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color WonColor = new Color(0.45f, 0.95f, 0.55f);
    static readonly Color LostColor = new Color(1f, 0.35f, 0.3f);

    [Tooltip("Both hands. Their index fingertips are what presses the buttons.")]
    public HandGestureRecognizer[] hands;

    [Header("Placement")]
    [Tooltip("How far in front of the player's head the panel appears, in meters. Keep it within arm's reach.")]
    public float distance = 0.65f;
    [Tooltip("How far below eye level the panel's center sits, in meters.")]
    public float heightBelowEyes = 0.2f;

    [Header("Levels")]
    [Tooltip("Turn on once there is a next level. Even then, the button only works after a win.")]
    public bool nextLevelAvailable = false;

    public event Action RestartPressed;
    public event Action NextLevelPressed;

    Transform panel;
    Material panelMaterial;
    TextMeshPro title;
    TouchButton restartButton;
    TouchButton nextLevelButton;

    public void Show(bool won)
    {
        if (panel == null) Build();

        title.text = won ? "VICTORY" : "DEFEAT";
        title.color = won ? WonColor : LostColor;
        nextLevelButton.Interactable = won && nextLevelAvailable;

        Transform[] tips = IndexTips();
        restartButton.Fingertips = tips;
        nextLevelButton.Fingertips = tips;

        PlaceInFrontOfPlayer();
        panel.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
    }

    void Build()
    {
        panel = new GameObject("End Menu Panel").transform;

        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, PanelSize, Vector3.zero, Order);

        title = MenuParts.NewText(panel, "TITLE", 0.8f, Color.white, new Vector2(PanelSize.x, 0.1f),
                                  new Vector3(0f, 0.09f, -0.004f), Order + 4);

        restartButton = TouchButton.Create(panel, "RESTART", ButtonSize,
                                           new Color(0.2f, 0.45f, 0.9f, 0.95f), Order + 1);
        restartButton.transform.localPosition = new Vector3(-0.13f, -0.065f, 0f);
        restartButton.Pressed += () => RestartPressed?.Invoke();

        nextLevelButton = TouchButton.Create(panel, "NEXT LEVEL", ButtonSize,
                                             new Color(0.25f, 0.65f, 0.4f, 0.95f), Order + 1);
        nextLevelButton.transform.localPosition = new Vector3(0.13f, -0.065f, 0f);
        nextLevelButton.Pressed += () => NextLevelPressed?.Invoke();
    }

    // Straight ahead of where the player is looking, ignoring head tilt, so
    // the panel stands upright instead of tilting with a bowed head.
    void PlaceInFrontOfPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        panel.position = cam.transform.position + forward * distance + Vector3.down * heightBelowEyes;
        // The panel's +Z points away from the viewer, which is how its text reads correctly.
        panel.rotation = Quaternion.LookRotation(forward);
    }

    Transform[] IndexTips()
    {
        var tips = new List<Transform>();
        foreach (HandGestureRecognizer hand in hands)
            if (hand != null && hand.indexTip != null)
                tips.Add(hand.indexTip);
        return tips.ToArray();
    }

    void OnDestroy()
    {
        if (panel != null) Destroy(panel.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
    }
}
