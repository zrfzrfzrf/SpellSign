using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The first-time tutorial, from the start menu's TUTORIAL button. Floating
// crystals stand two meters in front of the player; each breaks only to the
// element that beats it.
//
//   1-3. One crystal at a time, one sign each: Fire (on a Lightning crystal),
//        Lightning (on Water), Water (on Fire). A ghost hand shows the sign
//        and the push. Each break lights that arrow on the element wheel.
//   4.   All three crystals at once and no ghost hand: the player picks the
//        right spell for each. A wrong one bounces off and the wheel flashes
//        the arrow that would work.
//   Then the dragon arrives and the fight starts (Finished).
//
// Nothing is ever skipped for the player: if they're stuck the help gets
// stronger instead (the ghost hand grows and comes back). A small EXIT panel
// beside them returns to the start menu (ExitPressed), for staff.
//
// Built from code. GameFlow adds this component and runs it.
public class Tutorial : MonoBehaviour
{
    const float CrystalDistance = 2f;    // meters in front of the player
    const float CrystalBelowEyes = 0.15f;
    const float MixedSpacing = 0.7f;     // meters between the crystals of the last step
    const float BoardAbove = 0.45f;      // meters above the crystals, the line of instructions
    const float BoardTextSize = 0.85f;   // about 8 cm tall; smaller only to fit a long line
    const float WheelSide = 1.6f;        // meters to the left of the crystals, the element wheel, well clear of the last step's three
    const float WheelBelow = 0.15f;      // ...and how far below them, clear of the line of instructions
    const float WheelRadius = 0.22f;
    const float BoardWidth = 1.9f;
    const float HintAfter = 8f;          // seconds without a break before the help gets stronger
    const float HintPulseEvery = 2f;     // seconds between pulses of the crystal the hint is for
    const float LessonPause = 1.8f;      // seconds to take in each lesson before the next crystal
    const float FinishPause = 2f;        // seconds of "here comes the dragon" before the fight
    const int Order = 20;                // sorting order of the board's lowest layer
    static readonly Color BoardColor = new Color(0.04f, 0.05f, 0.09f, 0.75f);
    static readonly Color TextColor = new Color(0.95f, 0.95f, 1f);
    static readonly Color DoneColor = new Color(1f, 0.85f, 0.35f);
    static readonly Vector2 ExitPanelSize = new Vector2(0.28f, 0.14f);
    static readonly Vector2 ButtonSize = new Vector2(0.23f, 0.09f);

    // The signs taught, in order: easiest first, the hardest (Water) last.
    static readonly Element[] Lessons = { Element.Fire, Element.Lightning, Element.Water };
    // The last step's crystals, left to right.
    static readonly Element[] MixedRound = { Element.Water, Element.Lightning, Element.Fire };

    [Tooltip("Both hands. Their signs are what the ghost hand shows; their fingertips press EXIT.")]
    public HandGestureRecognizer[] hands;
    [Tooltip("Meta's OculusHand_R model, for the ghost hand.")]
    public GameObject ghostHandModel;
    [Tooltip("Material for the ghost hand (the Interaction SDK's OculusHand).")]
    public Material ghostHandMaterial;

    public event Action Finished;
    public event Action ExitPressed;

    readonly List<ElementCrystal> crystals = new List<ElementCrystal>();
    Transform root;           // everything the tutorial puts in the room
    Transform center;         // where the crystals stand
    Vector3 forward;          // level direction from the player to them
    TextMeshPro board;
    ElementWheel wheel;
    GhostHand ghost;
    Transform ghostTarget;
    Vector3 ghostVelocity;
    bool ghostJustShown;      // put it straight in place, without gliding there
    bool ghostPalmUp;         // the sign shown is made upside down
    Transform exitPanel;
    Material panelMaterial, boardMaterial;
    Coroutine running;

    public void Begin()
    {
        End();
        Build();
        running = StartCoroutine(Run());
    }

    // Removes everything the tutorial made. Safe to call when it isn't running.
    public void End()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        crystals.Clear();
        ghostTarget = null;
        if (exitPanel != null && HandGestureRecognizer.QuietPanel == exitPanel) HandGestureRecognizer.QuietPanel = null;
        if (root != null) Destroy(root.gameObject);
        if (panelMaterial != null) Destroy(panelMaterial);
        if (boardMaterial != null) Destroy(boardMaterial);
        root = null;
    }

    IEnumerator Run()
    {
        wheel.SetAllArrowsLit(false);

        foreach (Element sign in Lessons)
            yield return Lesson(sign);

        yield return MixedRoundStep();

        Say(Loc.T("tutorial.done"), DoneColor);
        yield return new WaitForSeconds(FinishPause);
        running = null;
        Finished?.Invoke();
    }

    // One sign: its crystal, the ghost hand showing it, and the arrow it lights.
    IEnumerator Lesson(Element sign)
    {
        Element beaten = Beaten(sign);
        string signName = ElementLook.DisplayName(sign).ToUpperInvariant();
        SpellPoseDefinition pose = PoseFor(sign);

        ElementCrystal crystal = Spawn(beaten, center.position);
        bool broken = false;
        crystal.Shattered += c => broken = true;
        crystal.Bounced += (c, spell) =>
            Say(Loc.T("tutorial.wrong_spell", ElementLook.DisplayName(spell), signName), ElementLook.Color(sign));

        Say(Loc.T("tutorial.make_sign", signName), ElementLook.Color(sign));
        ShowGhost(pose, sign, crystal.transform, 0f);

        float started = Time.time;
        while (!broken)
        {
            if (Time.time - started > HintAfter && ghost != null) ghost.Emphasis = 1f;
            yield return null;
        }

        HideGhost();
        wheel.SetArrowLit(sign, true);
        wheel.PulseArrow(sign);
        Say(Loc.T("tutorial.beats", ElementLook.DisplayName(sign), ElementLook.DisplayName(beaten)), ElementLook.Color(sign));
        yield return new WaitForSeconds(LessonPause);
    }

    // All three crystals; the player has to choose. Help comes only when they're stuck.
    IEnumerator MixedRoundStep()
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        int left = 0;
        float lastBreak = Time.time;
        ElementCrystal hinted = null;
        float nextPulse = 0f;

        for (int i = 0; i < MixedRound.Length; i++)
        {
            ElementCrystal crystal = Spawn(MixedRound[i], center.position + right * MixedSpacing * (i - 1));
            left++;
            crystal.Shattered += c =>
            {
                left--;
                lastBreak = Time.time;
                if (c == hinted)
                {
                    hinted = null;
                    HideGhost();
                }
                if (left > 0) Say(Loc.T("tutorial.mixed"), TextColor);
            };
            crystal.Bounced += (c, spell) =>
            {
                Element counter = ElementRules.Counter(c.Element);
                wheel.PulseArrow(counter);
                Say(Loc.T("tutorial.beats", ElementLook.DisplayName(counter), ElementLook.DisplayName(c.Element)), ElementLook.Color(counter));
            };
        }

        wheel.SetAllArrowsLit(true);
        Say(Loc.T("tutorial.mixed"), TextColor);

        while (left > 0)
        {
            // Stuck: point at one crystal and show the sign that breaks it.
            if (hinted == null && Time.time - lastBreak > HintAfter)
            {
                foreach (ElementCrystal c in crystals)
                    if (c != null && !c.Broken) { hinted = c; break; }
                if (hinted != null)
                {
                    Element counter = ElementRules.Counter(hinted.Element);
                    ShowGhost(PoseFor(counter), counter, hinted.transform, 1f);
                    nextPulse = 0f;
                }
            }
            if (hinted != null && Time.time >= nextPulse)
            {
                hinted.Pulse();
                wheel.PulseArrow(ElementRules.Counter(hinted.Element));
                nextPulse = Time.time + HintPulseEvery;
            }
            yield return null;
        }
        HideGhost();
    }

    void LateUpdate()
    {
        if (ghost == null || !ghost.gameObject.activeSelf || ghostTarget == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        // Where the player's own right hand would be, pointing at the crystal.
        Vector3 head = cam.transform.position;
        Vector3 toTarget = ghostTarget.position - head;
        toTarget.y = 0f;
        Vector3 ahead = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : forward;
        Vector3 right = Vector3.Cross(Vector3.up, ahead);
        Vector3 wrist = head + ahead * 0.4f + right * 0.12f + Vector3.down * 0.25f;

        // It follows the head a little behind, so it doesn't jitter with every small movement.
        Vector3 smoothed = wrist;
        if (ghostJustShown) ghostVelocity = Vector3.zero;
        else smoothed = Vector3.SmoothDamp(ghost.transform.position, wrist, ref ghostVelocity, 0.25f);
        ghostJustShown = false;
        ghost.Place(smoothed, ghostTarget.position - smoothed, ghostPalmUp ? Vector3.up : Vector3.down);
    }

    void ShowGhost(SpellPoseDefinition pose, Element sign, Transform target, float emphasis)
    {
        if (ghost == null || pose == null) return;
        ghostTarget = target;
        ghostJustShown = true;
        ghostPalmUp = pose.shownPalmUp;
        ghost.Emphasis = emphasis;
        ghost.Show(pose, ElementLook.Color(sign));
    }

    void HideGhost()
    {
        if (ghost != null) ghost.Hide();
        ghostTarget = null;
    }

    ElementCrystal Spawn(Element element, Vector3 position)
    {
        ElementCrystal crystal = ElementCrystal.Create(element, position);
        crystal.transform.SetParent(root, true);
        crystals.Add(crystal);
        return crystal;
    }

    void Say(string text, Color color)
    {
        if (board == null) return;
        board.text = text;
        board.color = color;
    }

    // The element `sign` beats.
    static Element Beaten(Element sign) => ElementRules.Counter(ElementRules.Counter(sign));

    // The hands' sign for an element: the pose whose spell is of that element.
    SpellPoseDefinition PoseFor(Element element)
    {
        if (hands == null) return null;
        foreach (HandGestureRecognizer hand in hands)
        {
            if (hand == null) continue;
            foreach (SpellPoseDefinition pose in hand.poses)
            {
                if (pose == null || pose.spellPrefab == null) continue;
                var projectile = pose.spellPrefab.GetComponent<FireballProjectile>();
                Element casts = projectile != null ? projectile.element : Element.Lightning;
                if (projectile == null && pose.spellPrefab.GetComponent<LightningStrike>() == null) continue;
                if (casts == element) return pose;
            }
        }
        return null;
    }

    void Build()
    {
        Camera cam = Camera.main;
        Vector3 head = cam != null ? cam.transform.position : Vector3.up * 1.6f;
        forward = cam != null ? cam.transform.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Quaternion facing = Quaternion.LookRotation(forward);

        root = new GameObject("Tutorial").transform;
        center = new GameObject("Crystals").transform;
        center.SetParent(root, false);
        center.position = head + forward * CrystalDistance + Vector3.down * CrystalBelowEyes;

        // The line of instructions above the crystals.
        var boardObject = new GameObject("Board").transform;
        boardObject.SetParent(root, false);
        boardObject.SetPositionAndRotation(center.position + Vector3.up * BoardAbove, facing);
        boardMaterial = MenuParts.NewMaterial(BoardColor);
        MenuParts.NewQuad(boardObject, "Background", boardMaterial, new Vector2(BoardWidth, 0.2f), Vector3.zero, Order);
        board = MenuParts.NewText(boardObject, "", BoardTextSize, TextColor, new Vector2(BoardWidth - 0.1f, 0.18f),
                                  new Vector3(0f, 0f, -0.01f), Order + 1);
        board.enableAutoSizing = true; // a long line shrinks to fit rather than running off the board
        board.fontSizeMin = 0.4f;
        board.fontSizeMax = BoardTextSize;

        // The element wheel, to the left of the crystals.
        wheel = ElementWheel.Create("Element Wheel", WheelRadius, true, Order);
        wheel.transform.SetParent(root, false);
        wheel.transform.SetPositionAndRotation(center.position - right * WheelSide + Vector3.down * WheelBelow, facing);

        ghost = GhostHand.Create(ghostHandModel, ghostHandMaterial);
        if (ghost != null)
        {
            ghost.transform.SetParent(root, false);
            ghost.Hide();
        }

        BuildExitPanel();
    }

    // EXIT, beside the player like the sandbox menu: out of the hands' way.
    void BuildExitPanel()
    {
        exitPanel = new GameObject("Tutorial Exit Panel").transform;
        exitPanel.SetParent(root, false);
        panelMaterial = MenuParts.NewMaterial(new Color(0.04f, 0.05f, 0.09f, 0.9f));
        MenuParts.NewQuad(exitPanel, "Background", panelMaterial, ExitPanelSize, Vector3.zero, Order);
        TouchButton exit = TouchButton.Create(exitPanel, Loc.T("tutorial.exit"), ButtonSize, new Color(0.75f, 0.25f, 0.22f, 0.95f), Order + 1);
        exit.Fingertips = MenuParts.IndexTips(hands);
        exit.Pressed += () => ExitPressed?.Invoke();

        var placement = exitPanel.gameObject.AddComponent<BesidePlayer>();
        placement.castTarget = center;
        placement.PlaceNow();

        HandGestureRecognizer.QuietPanel = exitPanel;
        HandGestureRecognizer.QuietHalfSize = new Vector3(ExitPanelSize.x * 0.5f + 0.04f, ExitPanelSize.y * 0.5f + 0.04f, 0.1f);
    }

    void OnDestroy() => End();
}
