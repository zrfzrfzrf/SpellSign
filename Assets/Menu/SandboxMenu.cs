using System;
using TMPro;
using UnityEngine;

// The panel that stays up in the sandbox: one touch button per way of casting
// (HandGestureRecognizer.CastMode), the chosen one lit, a line saying how that
// one works, and EXIT. Pressing a mode button switches both hands to it at once.
//
// Unlike the other menus it is up while casting is on, so it stays out of the
// hands' way: beside the player, to the left of the line they cast along (from
// them to the Cast Target), below eye level and turned to face them. It comes
// along when they walk off or turn to a target somewhere else. A fingertip
// right at it reads no signs (HandGestureRecognizer.QuietPanel), and its
// buttons are triggers, so spells fly through.
//
// Built from code like StartMenu. GameFlow adds this component itself, so it
// needs no scene setup.
public class SandboxMenu : MonoBehaviour
{
    const int Order = 10; // sorting order of the panel's lowest layer
    const float QuietMargin = 0.04f; // meters around the panel where a fingertip already reads no signs
    const float QuietDepth = 0.1f;   // ...and in front of and behind it
    static readonly Vector2 PanelSize = new Vector2(0.56f, 0.62f);
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);
    static readonly Vector2 WideButtonSize = new Vector2(0.36f, 0.09f);
    static readonly Color PanelColor = new Color(0.04f, 0.05f, 0.09f, 0.9f);
    static readonly Color TitleColor = new Color(1f, 0.85f, 0.35f);
    static readonly Color HintColor = new Color(0.8f, 0.82f, 0.88f);
    static readonly Color ModeColor = new Color(0.2f, 0.45f, 0.9f, 0.95f);
    static readonly Color ExitColor = new Color(0.75f, 0.25f, 0.22f, 0.95f);

    // In the order of HandGestureRecognizer.CastMode.
    static readonly string[] ModeLabels = { "CLASSIC", "PUSH", "CLASSIC + PUSH", "FLICK" };
    static readonly string[] ModeHints =
    {
        "Form the sign again for each cast",
        "Hold a sign, then push or swing down",
        "A new sign casts; so does a push or swing",
        "Hold a sign, point ahead, flick your hand",
    };

    [Tooltip("Both hands. Their index fingertips are what presses the buttons.")]
    public HandGestureRecognizer[] hands;

    [Header("Placement")]
    [Tooltip("What the player casts at (the dragon). The panel keeps to one side of the line from the player to it.")]
    public Transform castTarget;
    [Tooltip("How far along that line the panel's center sits, in meters from the player's head.")]
    public float distance = 0.3f;
    [Tooltip("How far to the right of that line it sits, in meters. Negative = to the left. Far enough that a hand held out to cast doesn't reach it.")]
    public float side = -0.62f;
    [Tooltip("How far below eye level the panel's center sits, in meters.")]
    public float heightBelowEyes = 0.45f;

    const float ReanchorDistance = 0.4f; // meters the player walks before the panel comes along
    const float ReanchorAngle = 45f;     // degrees the line to the cast target swings before it does
    const float GlideTime = 0.3f;        // roughly how long it takes to get there, in seconds

    public event Action ExitPressed;

    Transform panel;
    Material panelMaterial;
    Vector3 anchor;        // where the player's head was when the panel was last placed
    Vector3 castDirection; // level direction from there to the cast target
    Vector3 velocity;
    TextMeshPro hint;
    TouchButton[] modeButtons;
    TouchButton exitButton;

    public void Show()
    {
        if (panel == null) Build();

        Transform[] tips = MenuParts.IndexTips(hands);
        foreach (TouchButton button in modeButtons) button.Fingertips = tips;
        exitButton.Fingertips = tips;

        Place(true);
        panel.gameObject.SetActive(true);

        HandGestureRecognizer.QuietPanel = panel;
        HandGestureRecognizer.QuietHalfSize = new Vector3(PanelSize.x * 0.5f + QuietMargin,
                                                           PanelSize.y * 0.5f + QuietMargin, QuietDepth);
        ShowMode();
    }

    public void Hide()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        if (HandGestureRecognizer.QuietPanel == panel) HandGestureRecognizer.QuietPanel = null;
    }

    void LateUpdate()
    {
        if (panel != null && panel.gameObject.activeSelf) Place(false);
    }

    // Keeps the panel beside the player. Its spot is worked out from where the
    // player stood and which way the cast target was at that moment, and only
    // again once they have walked off or the target is somewhere else -- so
    // the buttons hold still to be pressed. at once = no glide.
    void Place(bool atOnce)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 head = cam.transform.position;

        // Toward the target; where the player is looking if there is none right now.
        Vector3 toTarget = castTarget != null && castTarget.gameObject.activeInHierarchy
            ? castTarget.position - head : cam.transform.forward;
        toTarget.y = 0f;
        bool haveDirection = toTarget.sqrMagnitude > 0.0001f;

        Vector3 walked = head - anchor;
        walked.y = 0f;
        bool targetMoved = haveDirection && castTarget != null && castTarget.gameObject.activeInHierarchy
            && Vector3.Angle(castDirection, toTarget) > ReanchorAngle;
        if (atOnce || walked.magnitude > ReanchorDistance || targetMoved)
        {
            anchor = head;
            if (haveDirection) castDirection = toTarget.normalized;
            else if (castDirection == Vector3.zero) castDirection = Vector3.forward;
        }

        Vector3 right = Vector3.Cross(Vector3.up, castDirection);
        Vector3 spot = anchor + castDirection * distance + right * side + Vector3.down * heightBelowEyes;
        // The panel's +Z points away from the viewer, which is how its text reads correctly.
        Quaternion facing = Quaternion.LookRotation((spot - anchor).normalized);

        if (atOnce)
        {
            panel.SetPositionAndRotation(spot, facing);
            velocity = Vector3.zero;
            return;
        }
        panel.position = Vector3.SmoothDamp(panel.position, spot, ref velocity, GlideTime);
        panel.rotation = Quaternion.Slerp(panel.rotation, facing, 1f - Mathf.Exp(-Time.deltaTime / (GlideTime * 0.5f)));
    }

    void Build()
    {
        panel = new GameObject("Sandbox Menu Panel").transform;

        panelMaterial = MenuParts.NewMaterial(PanelColor);
        MenuParts.NewQuad(panel, "Background", panelMaterial, PanelSize, Vector3.zero, Order);

        MenuParts.NewText(panel, "SANDBOX", 0.6f, TitleColor, new Vector2(PanelSize.x, 0.08f),
                          new Vector3(0f, 0.255f, -0.004f), Order + 4);
        hint = MenuParts.NewText(panel, "HINT", 0.22f, HintColor, new Vector2(PanelSize.x, 0.05f),
                                 new Vector3(0f, 0.185f, -0.004f), Order + 4);

        // The first two side by side, the others each on a row of their own.
        modeButtons = new TouchButton[ModeLabels.Length];
        for (int i = 0; i < ModeLabels.Length; i++)
        {
            var mode = (HandGestureRecognizer.CastMode)i;
            bool wide = i >= 2;
            modeButtons[i] = TouchButton.Create(panel, ModeLabels[i], wide ? WideButtonSize : ButtonSize, ModeColor, Order + 1);
            modeButtons[i].transform.localPosition = wide ? new Vector3(0f, 0.1f - 0.1f * (i - 1), 0f)
                                                          : new Vector3(i == 0 ? -0.13f : 0.13f, 0.1f, 0f);
            modeButtons[i].Pressed += () => Choose(mode);
        }

        exitButton = TouchButton.Create(panel, "EXIT", ButtonSize, ExitColor, Order + 1);
        exitButton.transform.localPosition = new Vector3(0f, -0.225f, 0f);
        exitButton.Pressed += () => ExitPressed?.Invoke();
    }

    void Choose(HandGestureRecognizer.CastMode mode)
    {
        HandGestureRecognizer.Mode = mode;
        Debug.Log($"[SandboxMenu] casting mode -> {mode}");
        ShowMode();
    }

    // Lights the chosen mode's button and says how that mode casts.
    void ShowMode()
    {
        int chosen = (int)HandGestureRecognizer.Mode;
        for (int i = 0; i < modeButtons.Length; i++)
            modeButtons[i].Highlighted = i == chosen;
        hint.text = ModeHints[chosen];
    }

    void OnDestroy()
    {
        if (panel != null)
        {
            if (HandGestureRecognizer.QuietPanel == panel) HandGestureRecognizer.QuietPanel = null;
            Destroy(panel.gameObject);
        }
        if (panelMaterial != null) Destroy(panelMaterial);
    }
}
