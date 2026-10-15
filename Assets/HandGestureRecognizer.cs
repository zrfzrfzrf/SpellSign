using System.Collections.Generic;
using UnityEngine;

// Generalized version of FistGestureDetector -- instead of hardcoding one
// gesture (fist), it tracks each finger's curl state independently and
// checks every SpellPoseDefinition you assign against the current hand
// shape. The aiming/spawning logic (aimSource -> indexKnuckle -> local
// axis fallback) is unchanged from the original script.
//
// Setup is the same as before: drag in the joint Transforms from your
// OVRHandVisualRight/Left's Hand Visual component. Then, instead of one
// Fireball Prefab field, drag in a list of SpellPoseDefinition assets
// (one per spell) under "Poses".
//
// How a held sign turns into a cast is set by Mode, for both hands at once:
//   Classic        - one cast each time the sign is formed (the original way).
//   Push           - the sign picks the spell; a thrust of the hand casts it.
//                    A thrust is a push away from the body, or a swing of the
//                    hand downward from the elbow (how people throw lightning).
//   ClassicAndPush - both: forming the sign casts once, and every thrust
//                    while it is held casts again.
//   Flick          - the easy-going one. Any quick movement of the hand casts
//                    (a jab, a chop, a throw, a flick of the wrist), and so
//                    does bringing the sign onto an enemy -- but only while
//                    the hand points into the player's view, which is also
//                    when the glow is at its brightest.
// GameFlow picks the mode (the sandbox menu switches between them).
//
// While a sign is being read the hand glows in the spell's color, and the
// glow flashes on every cast. Cooldowns are shared by both hands.

public class HandGestureRecognizer : MonoBehaviour
{
    public enum LocalAxis { Forward, Back, Up, Down, Right, Left }
    public enum CastMode { Classic, Push, ClassicAndPush, Flick }

    // How a held sign becomes a cast. One setting for both hands.
    public static CastMode Mode = CastMode.Classic;

    // A menu panel that is up while casting is on (the sandbox menu). A hand
    // reaching for it reads no signs, so pressing a button can't cast a spell.
    // QuietHalfSize is the panel's half width, half height and how far in
    // front of or behind it still counts, in meters.
    public static Transform QuietPanel;
    public static Vector3 QuietHalfSize;

    // Shared by both hands, so two hands can't cast twice as fast as one.
    static float lastAnyCastTime = -999f;
    static readonly Dictionary<SpellPoseDefinition, float> lastCastTimes = new Dictionary<SpellPoseDefinition, float>();

    // Statics outlive a play session when the editor skips its domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Mode = CastMode.Classic;
        QuietPanel = null;
        lastAnyCastTime = -999f;
        lastCastTimes.Clear();
    }

    [Header("Drag these from OVRHandVisualRight/Left's Hand Visual component")]
    public Transform palm;       // drag HandWristRoot here
    public Transform thumbTip;   // drag HandThumbTip here
    public Transform indexTip;   // drag HandIndexTip here
    public Transform middleTip;  // drag HandMiddleTip here
    public Transform ringTip;    // drag HandRingTip here
    public Transform pinkyTip;   // drag HandPinkyTip here

    [Tooltip("How close (in meters) a fingertip must get to the palm to count as CURLING. Smaller = stricter.")]
    public float closeThreshold = 0.10f;

    [Tooltip("How far (in meters) a fingertip must move from the palm to count as OPENING. Should be a bit larger than closeThreshold so detection doesn't flicker at the boundary.")]
    public float openThreshold = 0.13f;

    [Header("Spells (one SpellPoseDefinition asset per gesture)")]
    public List<SpellPoseDefinition> poses = new List<SpellPoseDefinition>();

    [Header("Aiming (same priority order as before)")]
    [Tooltip("Drag your Main Camera / CenterEyeAnchor here to throw where you're LOOKING. Leave empty for hand-aiming.")]
    public Transform aimSource;

    [Tooltip("RECOMMENDED for hand-aiming: drag your index finger's KNUCKLE joint here (e.g. XRHand_IndexProximal), NOT the tip. Spells fly from wrist through this knuckle.")]
    public Transform indexKnuckle;

    [Tooltip("Only used when Aim Source and Index Knuckle are both empty.")]
    public LocalAxis handForwardAxis = LocalAxis.Forward;

    [Tooltip("How far in front of your palm the spell spawns, so it doesn't spawn inside your own hand model.")]
    public float spawnOffset = 0.25f;

    [Tooltip("Fine-tune correction for up/down aim, in degrees.")]
    public float aimPitchCorrection = 0f;

    [Header("Anti-collision")]
    [Tooltip("Minimum time (seconds) between ANY two casts, regardless of which spell. A small safety net against a fast hand transition (e.g. a fist opening into a point) confirming two different poses almost simultaneously.")]
    public float globalCastCooldown = 0.15f;

    [Tooltip("Average fingertip movement speed (meters/second) below which the hand counts as 'settled'. Pose hold-timers only accumulate while the hand is settled -- so a fast pass-through shape (e.g. briefly forming a full fist on your way to pointing) never accrues enough time to confirm, even with a short Pose Confirm Delay, because your fingers are still moving the whole time they're shaped like that pose. A genuinely held pose confirms fast since you're not moving. Raise this if deliberate casts feel like they're not registering; lower it if pass-through poses still sneak through.")]
    public float settleSpeedThreshold = 0.6f;

    [Header("Thrust to cast (Push and ClassicAndPush modes)")]
    [Tooltip("How fast the hand must move away from the chest to count as a push, in meters per second. Lower = gentler pushes cast.")]
    public float pushSpeed = 0.7f;
    [Tooltip("The hand must slow to below this, or come back, before its next push counts, in meters per second.")]
    public float pushRearmSpeed = 0.2f;
    [Tooltip("How far the hand must have travelled outward in the push before it casts, in meters. Filters out tracking jitter.")]
    public float pushMinTravel = 0.06f;
    [Tooltip("How fast the hand must tip downward to count as a swing, in degrees per second. A quarter turn from the elbow in a third of a second is about 270.")]
    public float swingSpeed = 200f;
    [Tooltip("How far the hand must have tipped downward in the swing before it casts, in degrees.")]
    public float swingMinAngle = 35f;

    [Header("Flick to cast (Flick mode)")]
    [Tooltip("The hand only casts while it points within this many degrees of where the head faces.")]
    public float aimGateAngle = 40f;
    [Tooltip("How fast the hand must move, in any direction, to count as a flick, in meters per second...")]
    public float flickSpeed = 0.45f;
    [Tooltip("...or how fast it must turn, in degrees per second.")]
    public float flickTurnRate = 110f;
    [Tooltip("How far the hand must have moved in the flick before it casts, in meters...")]
    public float flickMinTravel = 0.04f;
    [Tooltip("...or how far it must have turned, in degrees.")]
    public float flickMinAngle = 15f;
    [Tooltip("Bringing a held sign onto an enemy casts once, if the hand points within this many meters of it. Keep it below what the spells themselves need to hit, so a spell cast this way lands. 0 = pointing alone never casts.")]
    public float pointRadius = 0.1f;

    [Header("Hand glow")]
    [Tooltip("How wide the glow on the hand is while a sign is held, in meters.")]
    public float glowSize = 0.16f;

    [Header("Palm Facing Check")]
    [Tooltip("Drag your Main Camera / CenterEyeAnchor here. Used only to check whether your palm is facing toward you (looking at your own palm) or away from you -- independent of Aim Source, which may be left empty if you're using hand-aiming. Only matters for poses that enable 'Require Palm Facing Away'.")]
    public Transform headReference;

    [Tooltip("Which local axis of the Palm transform points straight out of your palm (the direction you'd be looking at if you held your hand up to inspect your own palm). This is rig-dependent -- if the facing check seems inverted or unresponsive, try a different axis and rebuild. Debug Log State prints the live dot product so you can tell which axis is correct by watching it flip sign as you turn your hand.")]
    public LocalAxis palmNormalAxis = LocalAxis.Up;

    [Tooltip("Maximum allowed dot product between the palm normal and the direction toward your head for 'facing away' to count. 0 = palm must be at least perpendicular to you (not tilted toward your own face at all). More negative = stricter, requiring the palm to face more directly away.")]
    [Range(-1f, 1f)]
    public float palmAwayDotThreshold = 0f;

    [Tooltip("Minimum dot product between the palm normal and straight down for 'facing down' to count. 1 = palm must be perfectly level; 0.3 allows about 70 degrees of tilt, so you can still aim well up or down and roll your hand, but a hand hanging at your side (palm sideways) doesn't count. Only matters for poses that enable 'Require Palm Facing Down'.")]
    [Range(-1f, 1f)]
    public float palmDownDotThreshold = 0.3f;

    [Header("Debugging")]
    [Tooltip("Logs live per-finger curl state and which pose (if any) matches, twice a second.")]
    public bool debugLogState = false;

    // Per-finger curl state, persisted across frames (hysteresis).
    private bool[] fingerCurled = new bool[5];

    // Per-pose runtime state, aligned by index with the `poses` list.
    private class PoseRuntime
    {
        public float holdTimer;
        public bool confirmed;
        public bool castPending; // confirmed during a cooldown: cast as soon as it ends
    }
    private List<PoseRuntime> poseState = new List<PoseRuntime>();

    private float debugLogTimer = 0f;

    // For the movement-settling check: previous frame's fingertip positions.
    private Vector3[] prevTipPositions = new Vector3[5];
    private bool havePrevTips = false;
    private float currentSettleSpeed = 0f;
    // The same, measured within the hand: how fast the fingers are changing
    // shape, whatever the arm is doing. The modes that cast on a moving arm
    // use this one.
    private Vector3[] prevTipInHand = new Vector3[5];
    private float fingerSettleSpeed = 0f;

    const float SignLatchTime = 0.25f;  // seconds a sign stays held after tracking loses its shape (fast pushes blur the hand)
    const float ThrustBufferTime = 0.2f; // seconds a thrust waits for a cooldown to end before it is forgotten
    const float PushSmoothing = 15f;    // how fast the measured push speed follows the raw one, per second
    const float TrackingJump = 0.25f;   // meters in one frame that can only be tracking snapping, not a hand moving
    const float ChestBelowHead = 0.25f; // meters from the eyes down to where a push starts from
    const float SwingSmoothing = 25f;   // how fast the measured swing speed follows the raw one, per second
    const float SwingTurning = 90f;     // degrees per second of tipping down above which the hand is still mid-swing
    const float SwingEndShare = 0.35f;  // a swing is over once it has slowed to this share of its top speed
    const float SwingJump = 45f;        // degrees in one frame that can only be tracking snapping
    const float FlickSmoothing = 20f;   // how fast the measured flick speeds follow the raw ones, per second
    const float FlickIdle = 0.5f;       // share of a flick's speed below which no movement is under way
    const float FlickRest = 0.2f;       // ...and below which the hand is lying still
    const float FlickRestRefresh = 0.4f; // seconds of drifting about, between those two, before the hand counts as still anyway
    const float FlickAimMargin = 5f;    // degrees nearer the view the start of a flick must have pointed for its aim to be used
    const float FlickRecoverTime = 0.6f; // seconds after a flick in which coming back to where it began doesn't cast
    const float PointRange = 15f;       // meters ahead that pointing at an enemy is looked for
    const float PointRearmTime = 0.25f; // seconds the hand must be off an enemy before pointing at it casts again
    const float GlowOffAimStrength = 0.3f; // how bright the glow is while the hand points out of the view (Flick mode)
    const float GlowFadeTime = 0.1f;    // seconds the glow takes to appear or go
    const float GlowFlashTime = 0.25f;  // seconds the flash on a cast lasts
    const float GlowPulseRate = 3f;     // pulses per second while a sign is held
    const float GlowHeldStrength = 0.8f; // how bright the glow is while a sign is held; a cast flashes it to full

    private CastMode activeMode;        // the mode this hand's state belongs to
    private int heldPose = -1;          // index of the sign being read, in the non-Classic modes
    private float heldUntil;
    private float prevReach = -1f;      // distance from chest to hand last frame
    private float reachSpeed;
    private float pushTravel;
    private bool pushArmed = true;
    private bool pushWaiting;           // a push made mid-swing, waiting for the swing to end
    private bool havePitch;
    private float prevPitch;            // how far the hand pointed above level last frame, in degrees
    private float swingRate;            // degrees per second the hand is tipping downward
    private float swingTravel;          // degrees tipped downward in the swing under way
    private float swingPeak;            // its top speed so far
    private float lastSwingPeak, lastSwingTravel; // the swing that just ended, for the debug log
    private float thrustBufferedUntil = -1f;

    // Flick mode.
    private bool haveFlick;
    private Vector3 prevOffset;         // hand relative to head last frame
    private Vector3 prevAlong;          // which way the hand pointed last frame
    private float moveSpeed, turnRate;  // how fast the hand is moving (m/s) and turning (deg/s), any direction
    private Vector3 flickFromOffset, flickFromAlong; // where the movement under way started
    private Vector3 flickFromFinger;    // ...and which way the index finger pointed
    private float flickFromReach;       // ...and how far from the chest the hand was then
    private float flickFromTime;        // ...and when
    private float flickMinReach;        // the closest to the chest it has come since
    private float flickPeak;            // its top speed so far, as a share of a flick's
    private bool flickFar;              // ...it has gone far enough
    private bool flickTurned;           // ...and the hand has turned on the way, not just moved
    private bool flickBig;              // ...and so it is a flick
    private bool flickFired;            // ...and has cast already
    private bool flickEnded;            // a flick that hasn't cast yet ended this frame
    private Vector3 recoverOffset, recoverAlong; // where the last flick began: coming back there doesn't cast
    private float recoverUntil = -1f;
    private Vector3 heldHandAim, heldFingerAim;  // the aim from the start of a flick, used for its cast
    private float aimHeldUntil = -1f;
    private bool pointArmed = true;     // the hand has been off any enemy, so pointing at one casts
    private bool onEnemy;               // the hand points at an enemy right now
    private float lastOnEnemy = -999f;
    static readonly RaycastHit[] pointHits = new RaycastHit[16];

    private Transform glow;
    private Material glowMaterial;
    private Color glowColor = Color.white;
    private float glowShown;            // 0..1, fades in and out
    private float glowFlash;            // 1 right after a cast, fades to 0
    private float glowAimed = 1f;       // 1 = full strength, 0 = dimmed for pointing out of the view
    private float glowPulse;
    static readonly int GlowColorId = Shader.PropertyToID("_Color");

    [Header("Aim Smoothing (all spells)")]
    [Tooltip("How fast the smoothed joint positions catch up to the raw tracked positions, per second. Higher = less lag but less smoothing of hand-tracking jitter; lower = smoother but more delay between actually aiming and the aim direction catching up.")]
    public float aimSmoothSpeed = 20f;

    // Exponentially-smoothed copies of palm/indexTip/indexKnuckle. Every aim
    // mode (PointWithFingers, and the indexKnuckle->palm fallback that Fire
    // and Water currently use since aimSource is unassigned) ultimately
    // subtracts two live tracked joint positions -- raw per-frame data is
    // noisy enough that a poorly-timed cast (e.g. right as poseConfirmDelay
    // elapses) can catch a tracking glitch and fire off-axis.
    private Vector3 smoothedPalm;
    private Vector3 smoothedIndexTip;
    private Vector3 smoothedIndexKnuckle;
    private bool haveSmoothedAim = false;

    void Start()
    {
        poseState.Clear();
        foreach (var pose in poses)
        {
            poseState.Add(new PoseRuntime());
            if (pose != null && pose.effectObject != null)
                pose.effectObject.SetActive(false);
        }
    }

    // Pausing the recognizer (e.g. while a menu is up) in the middle of a hold
    // would leave that pose "confirmed" with its glow on; clear it so resuming
    // starts clean.
    void OnDisable()
    {
        ResetSigns();
        glowShown = 0f;
        glowFlash = 0f;
        if (glow != null) glow.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (glow != null) Destroy(glow.gameObject);
        if (glowMaterial != null) Destroy(glowMaterial);
    }

    // Forgets every sign being held: nothing confirmed, nothing charging.
    void ResetSigns()
    {
        foreach (var state in poseState)
        {
            state.holdTimer = 0f;
            state.confirmed = false;
            state.castPending = false;
        }
        foreach (var pose in poses)
            if (pose != null && pose.effectObject != null)
                pose.effectObject.SetActive(false);

        heldPose = -1;
        pushWaiting = false;
        thrustBufferedUntil = -1f;
        aimHeldUntil = -1f;
        pointArmed = true;
    }

    void Update()
    {
        if (palm == null) return;

        UpdateFingerCurlStates();
        UpdateSettleSpeed();
        UpdateAimSmoothing();
        // Each detector runs only in the modes that use it, and starts from
        // scratch when its mode comes on.
        bool thrust = (Mode == CastMode.Push || Mode == CastMode.ClassicAndPush) && UpdateThrust();
        if (Mode == CastMode.Flick) UpdateFlick();
        else haveFlick = false;
        bool handSettled = currentSettleSpeed <= settleSpeedThreshold;

        // Switching modes starts every sign from scratch.
        if (activeMode != Mode)
        {
            ResetSigns();
            activeMode = Mode;
            prevReach = -1f;
            havePitch = false;
        }

        Vector3 palmNormal = GetAxisDirection(palmNormalAxis);

        float facingDot = 0f;
        bool palmFacingAway = true; // default true so poses without the requirement (or a missing Head Reference) are never blocked
        if (headReference != null)
        {
            Vector3 toHead = (headReference.position - palm.position).normalized;
            facingDot = Vector3.Dot(palmNormal, toHead);
            palmFacingAway = facingDot <= palmAwayDotThreshold;
        }

        float downDot = Vector3.Dot(palmNormal, Vector3.down);
        bool palmFacingDown = downDot >= palmDownDotThreshold;

        if (ReachingForMenu())
        {
            ResetSigns();
            UpdateGlow(null);
            return;
        }

        if (Mode != CastMode.Classic)
        {
            UpdateHeldSign(thrust, palmFacingAway, palmFacingDown);
            return;
        }

        SpellPoseDefinition glowing = null;
        for (int i = 0; i < poses.Count; i++)
        {
            var pose = poses[i];
            if (pose == null) continue;
            var state = poseState[i];

            bool rawMatch = Matches(pose, palmFacingAway, palmFacingDown);

            // Only accumulate hold time while the shape matches AND the
            // hand has settled (fingers aren't actively moving). A fast
            // pass-through pose (mid-transition to a different gesture)
            // matches briefly but never settles, so it never accrues time.
            // A deliberately held pose is already still, so it confirms
            // almost immediately even with a short Pose Confirm Delay.
            if (rawMatch && handSettled)
                state.holdTimer += Time.deltaTime;
            else
                state.holdTimer = 0f;

            bool isConfirmed = state.confirmed
                ? rawMatch
                : (state.holdTimer >= pose.poseConfirmDelay);

            if (isConfirmed && !state.confirmed)
                OnPoseDetected(pose, state);
            else if (!isConfirmed && state.confirmed)
            {
                state.castPending = false;
                OnPoseReleased(pose);
            }
            else if (isConfirmed && state.castPending)
            {
                // Still one cast per hold -- this is the one a cooldown held back.
                state.castPending = !CastSpell(pose);
            }

            if (isConfirmed && pose.effectObject != null)
            {
                // Position AND rotation now track the palm every frame.
                // Position-only was fine for Fire's glow (no "facing" to
                // speak of), but a hand-attached shield needs to turn with
                // your palm too, or it'll just hover there facing whichever
                // way it happened to spawn.
                pose.effectObject.transform.position = palm.position;
                pose.effectObject.transform.rotation = palm.rotation;
            }

            state.confirmed = isConfirmed;
            if (isConfirmed && glowing == null) glowing = pose;
        }
        UpdateGlow(glowing);

        if (debugLogState)
        {
            debugLogTimer += Time.deltaTime;
            if (debugLogTimer > 0.5f)
            {
                debugLogTimer = 0f;
                string match = "none";
                foreach (var pose in poses)
                    if (pose != null && pose.Matches(fingerCurled)) { match = pose.spellName; break; }

                Debug.Log($"thumb={fingerCurled[0]} index={fingerCurled[1]} middle={fingerCurled[2]} " +
                          $"ring={fingerCurled[3]} pinky={fingerCurled[4]} match={match} " +
                          $"settleSpeed={currentSettleSpeed:F2} settled={handSettled} " +
                          $"facingDot={facingDot:F2} facingAway={palmFacingAway} " +
                          $"downDot={downDot:F2} facingDown={palmFacingDown}");
            }
        }
    }

    bool Matches(SpellPoseDefinition pose, bool palmFacingAway, bool palmFacingDown)
    {
        return pose.Matches(fingerCurled)
            && (!pose.requirePalmFacingAway || palmFacingAway)
            && (!pose.requirePalmFacingDown || palmFacingDown);
    }

    // The modes other than Classic: a sign that is held stays "read" for as
    // long as its shape is kept, whatever the arm does, and every thrust of
    // the hand casts it. In ClassicAndPush, forming the sign casts once too.
    void UpdateHeldSign(bool thrust, bool palmFacingAway, bool palmFacingDown)
    {
        bool fingersSettled = fingerSettleSpeed <= settleSpeedThreshold;

        int matched = -1;
        for (int i = 0; i < poses.Count; i++)
        {
            var pose = poses[i];
            if (pose == null) continue;
            var state = poseState[i];

            bool rawMatch = Matches(pose, palmFacingAway, palmFacingDown);

            // Only the fingers have to hold still here: the arm is meant to move.
            if (!rawMatch) state.holdTimer = 0f;
            else if (fingersSettled) state.holdTimer += Time.deltaTime;

            bool isConfirmed = state.confirmed ? rawMatch : state.holdTimer >= pose.poseConfirmDelay;
            if (isConfirmed && !state.confirmed)
            {
                Debug.Log($"Pose detected: {pose.spellName}");
                state.castPending = Mode == CastMode.ClassicAndPush; // the cast for forming the sign
            }
            else if (!isConfirmed && state.confirmed)
            {
                Debug.Log($"Pose released: {pose.spellName}");
                state.castPending = false;
            }
            state.confirmed = isConfirmed;

            if (isConfirmed && matched < 0) matched = i;
        }

        // A fast thrust can blur the hand for a few frames: the sign stays
        // held through that.
        if (matched >= 0)
        {
            heldPose = matched;
            heldUntil = Time.time + SignLatchTime;
        }
        else if (Time.time > heldUntil)
            heldPose = -1;

        if (heldPose < 0)
        {
            thrustBufferedUntil = -1f;
            pointArmed = true; // a sign formed while already pointing at an enemy casts
            UpdateGlow(null);
            return;
        }

        SpellPoseDefinition held = poses[heldPose];
        PoseRuntime heldState = poseState[heldPose];

        if (heldState.castPending)
            heldState.castPending = !CastSpell(held);

        bool aimed = true;
        if (Mode == CastMode.Flick)
        {
            aimed = AimedAhead(held);
            bool pointed = PointedAtEnemy(held, aimed); // every frame: it keeps track of being on an enemy
            thrust = pointed || (aimed && TakeFlick());
        }

        // A thrust made during a cooldown casts as soon as the cooldown ends.
        if (thrust) thrustBufferedUntil = Time.time + ThrustBufferTime;
        if (Time.time <= thrustBufferedUntil && CastSpell(held))
        {
            thrustBufferedUntil = -1f;
            aimHeldUntil = -1f;
            if (Mode == CastMode.Flick && onEnemy) pointArmed = false; // this cast was the one for pointing here
        }

        UpdateGlow(held, aimed);
    }

    Transform Head() => headReference != null ? headReference : (Camera.main != null ? Camera.main.transform : null);

    // True while the hand points into the player's view. Flick mode casts
    // nothing while it doesn't: a hand aimed at the floor or off to the side
    // is hardly ever meant to cast.
    bool AimedAhead(SpellPoseDefinition pose)
    {
        Transform head = Head();
        return head == null || Vector3.Angle(GetAimDirection(pose), head.forward) <= aimGateAngle;
    }

    // Measures the hand's movement for Flick mode: how fast it moves (relative
    // to the head, so walking doesn't count) and turns, in any direction. A
    // flick is a burst of that -- fast enough at its peak, and far enough from
    // where it started. Distances are taken from where the burst started, not
    // added up frame by frame, so tracking jitter can't build up into one.
    //
    // When a flick ends, the spell goes along whichever is nearer to where the
    // player is looking: the way the hand pointed when the flick began (a
    // flick of the wrist dips away from the aim and comes back) or the way it
    // points now (a chop or a throw ends on the aim). The movement that brings
    // the hand back to where a flick began is the same gesture finishing, and
    // doesn't cast again.
    void UpdateFlick()
    {
        flickEnded = false;
        Transform head = Head();
        if (head == null || Time.deltaTime <= 0f) return;

        // Speeds from the smoothed joints, which are steadier; distances from
        // the raw ones, which don't lag behind a quick flick.
        Vector3 offset = smoothedPalm - head.position;
        Vector3 along = indexKnuckle != null ? (smoothedIndexKnuckle - smoothedPalm).normalized : GetAxisDirection(handForwardAxis);
        Vector3 rawOffset = palm.position - head.position;
        Vector3 rawAlong = indexKnuckle != null ? (indexKnuckle.position - palm.position).normalized : along;
        float reach = (rawOffset + Vector3.up * ChestBelowHead).magnitude; // from the chest, as for a push
        if (!haveFlick)
        {
            prevOffset = offset;
            prevAlong = along;
            moveSpeed = turnRate = flickPeak = 0f;
            flickFar = flickTurned = flickBig = flickFired = false;
            StartFlickFrom(rawOffset, rawAlong, reach);
            haveFlick = true;
            return;
        }

        float moved = (offset - prevOffset).magnitude;
        float turned = Vector3.Angle(prevAlong, along);
        prevOffset = offset;
        prevAlong = along;

        // Tracking snapping to a new place is not a flick.
        if (moved > TrackingJump || turned > SwingJump)
        {
            haveFlick = false;
            return;
        }

        float blend = 1f - Mathf.Exp(-FlickSmoothing * Time.deltaTime);
        moveSpeed = Mathf.Lerp(moveSpeed, moved / Time.deltaTime, blend);
        turnRate = Mathf.Lerp(turnRate, turned / Time.deltaTime, blend);
        float activity = Mathf.Max(moveSpeed / flickSpeed, turnRate / flickTurnRate);

        // No movement under way. The place a movement starts from is where
        // the hand last lay still -- not where it is while only just getting
        // going, or the first part of every flick would go unmeasured.
        if (flickPeak <= 0f && activity <= FlickIdle)
        {
            if (activity <= FlickRest || Time.time - flickFromTime > FlickRestRefresh)
                StartFlickFrom(rawOffset, rawAlong, reach);
            return;
        }

        // Far enough: moved, or turned, at any point of the movement. Drawing
        // the hand back toward the body is getting ready, not casting, so that
        // alone doesn't count; coming back out from there does.
        float travel = (rawOffset - flickFromOffset).magnitude;
        flickMinReach = Mathf.Min(flickMinReach, reach);
        if ((travel >= flickMinTravel && reach - flickFromReach > -travel * 0.5f)
            || reach - flickMinReach >= flickMinTravel
            || Vector3.Angle(flickFromAlong, rawAlong) >= flickMinAngle)
            flickFar = true;
        if (Vector3.Angle(flickFromAlong, rawAlong) >= flickMinAngle * 0.5f) flickTurned = true;
        flickPeak = Mathf.Max(flickPeak, activity);
        flickBig = flickFar && flickPeak >= 1f;

        // Over once it has stopped, or slowed to a fraction of its top speed.
        if (activity > FlickIdle && activity >= flickPeak * SwingEndShare) return;

        if (flickBig)
        {
            bool comingBack = Time.time <= recoverUntil
                && (rawOffset - recoverOffset).magnitude < flickMinTravel * 1.5f
                && Vector3.Angle(rawAlong, recoverAlong) < flickMinAngle;
            if (comingBack)
                recoverUntil = -1f;
            else
            {
                flickEnded = !flickFired;
                if (flickEnded)
                {
                    // Aim the way the hand pointed at the start, if that was
                    // more into the view than where it has ended up.
                    if (Vector3.Angle(flickFromAlong, head.forward) + FlickAimMargin < Vector3.Angle(rawAlong, head.forward))
                    {
                        heldHandAim = flickFromAlong;
                        heldFingerAim = flickFromFinger;
                        aimHeldUntil = Time.time + ThrustBufferTime;
                    }
                    else
                        SnapAim(); // the smoothed aim is still catching up with the hand
                }
                recoverOffset = flickFromOffset;
                recoverAlong = flickFromAlong;
                recoverUntil = Time.time + FlickRecoverTime;
            }
            if (debugLogState)
                Debug.Log($"Flick{(comingBack ? " (coming back, no cast)" : "")}: peak {flickPeak:F1}x, moved {travel:F2} m, turned {Vector3.Angle(flickFromAlong, rawAlong):F0} deg");
        }

        flickPeak = 0f;
        flickFar = flickTurned = flickBig = flickFired = false;
        StartFlickFrom(rawOffset, rawAlong, reach);
    }

    // Remembers where the hand is as the place the next movement starts from.
    void StartFlickFrom(Vector3 offset, Vector3 along, float reach)
    {
        flickFromOffset = offset;
        flickFromAlong = along;
        flickFromFinger = indexTip != null && indexKnuckle != null ? (indexTip.position - indexKnuckle.position).normalized : along;
        flickFromReach = flickMinReach = reach;
        flickFromTime = Time.time;
    }

    // True when a flick should cast now. One that turns the hand casts when it
    // ends, when it is clear which way the spell should go; one that keeps the
    // hand pointing the same way throughout (a jab) casts as soon as it is big
    // enough.
    bool TakeFlick()
    {
        if (flickEnded) return true;
        if (!flickBig || flickFired || flickTurned) return false;

        flickFired = true;
        return true;
    }

    // True on the frame a held sign comes to point at an enemy: within Point
    // Radius of one, with nothing of the room in between. Once it has cast,
    // the hand has to leave the enemy for a moment before pointing casts again.
    // A hand turning as fast as a flick doesn't cast by pointing: that is a
    // flick, which casts when it ends, where the hand finishes.
    bool PointedAtEnemy(SpellPoseDefinition pose, bool aimed)
    {
        onEnemy = false;
        if (pointRadius <= 0f) return false;

        Vector3 direction = GetAimDirection(pose);
        Vector3 from = palm.position + direction * spawnOffset; // where the spell would start
        int count = Physics.SphereCastNonAlloc(from, pointRadius, direction, pointHits, PointRange, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        RaycastHit nearest = default;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = pointHits[i];
            if (h.distance <= 0f) continue; // already touching at the start
            if (found && h.distance >= nearest.distance) continue;
            nearest = h;
            found = true;
        }

        onEnemy = found && nearest.collider.GetComponentInParent<ISpellTarget>() != null;
        if (!onEnemy)
        {
            if (Time.time - lastOnEnemy > PointRearmTime) pointArmed = true;
            return false;
        }

        lastOnEnemy = Time.time;
        if (!aimed || !pointArmed || turnRate >= flickTurnRate) return false;
        pointArmed = false;
        if (debugLogState) Debug.Log("Point: sign brought onto " + nearest.collider.name);
        return true;
    }

    // True on the frame the hand completes a thrust: a push away from the
    // body, or a downward swing from the elbow. A push made while the hand is
    // still tipping down waits for the swing to end, so the spell leaves when
    // the hand points where it is going rather than partway there.
    bool UpdateThrust()
    {
        if (UpdatePush()) pushWaiting = true;
        bool swung = UpdateSwing();

        bool midSwing = swingRate > SwingTurning;
        if (!swung && (!pushWaiting || midSwing)) return false;

        pushWaiting = false;
        if (swung) SnapAim(); // the smoothed aim is still catching up with a swing
        if (debugLogState)
            Debug.Log(swung ? $"Swing: {lastSwingPeak:F0} deg/s over {lastSwingTravel:F0} deg" : $"Push: {reachSpeed:F2} m/s after {pushTravel:F2} m");
        return true;
    }

    // True on the frame the hand completes a push away from the body: moving
    // away from the chest fast enough, after travelling far enough. The hand
    // has to slow down or come back before it can push again. Measured from
    // the chest rather than the head because a push starts below the eyes:
    // from the head, half of a forward push would look like sideways movement.
    bool UpdatePush()
    {
        Transform head = Head();
        if (head == null || Time.deltaTime <= 0f) return false;

        float reach = Vector3.Distance(palm.position, head.position + Vector3.down * ChestBelowHead);
        float moved = prevReach < 0f ? 0f : reach - prevReach;
        prevReach = reach;

        // Tracking snapping to a new place is not a push.
        if (Mathf.Abs(moved) > TrackingJump)
        {
            reachSpeed = 0f;
            pushTravel = 0f;
            return false;
        }

        reachSpeed = Mathf.Lerp(reachSpeed, moved / Time.deltaTime, 1f - Mathf.Exp(-PushSmoothing * Time.deltaTime));

        if (reachSpeed < pushRearmSpeed)
        {
            pushArmed = true;
            pushTravel = 0f;
            return false;
        }

        pushTravel += Mathf.Max(0f, moved);
        if (!pushArmed || reachSpeed < pushSpeed || pushTravel < pushMinTravel) return false;

        pushArmed = false;
        return true;
    }

    // True on the frame a downward swing of the hand ends: it tipped down fast
    // enough and far enough, and has now slowed to a fraction of its top
    // speed. Measured as the turning of the hand (wrist to knuckles) rather
    // than its travel, so a child's short arm swings the same as an adult's.
    bool UpdateSwing()
    {
        if (indexKnuckle == null || Time.deltaTime <= 0f) return false;

        Vector3 along = indexKnuckle.position - palm.position;
        if (along.sqrMagnitude < 0.000001f) return false;
        float pitch = Mathf.Asin(Mathf.Clamp(along.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        float tipped = havePitch ? prevPitch - pitch : 0f; // positive = downward
        prevPitch = pitch;
        havePitch = true;

        // Tracking snapping to a new pose is not a swing.
        if (Mathf.Abs(tipped) > SwingJump)
        {
            swingRate = 0f;
            swingTravel = 0f;
            swingPeak = 0f;
            return false;
        }

        swingRate = Mathf.Lerp(swingRate, tipped / Time.deltaTime, 1f - Mathf.Exp(-SwingSmoothing * Time.deltaTime));

        if (swingRate > SwingTurning)
        {
            swingTravel += Mathf.Max(0f, tipped);
            swingPeak = Mathf.Max(swingPeak, swingRate);
        }

        // Over once it has stopped, or slowed to a fraction of its top speed.
        bool over = swingPeak > 0f && (swingRate <= SwingTurning || swingRate < swingPeak * SwingEndShare);
        if (!over) return false;

        bool swung = swingPeak >= swingSpeed && swingTravel >= swingMinAngle;
        lastSwingPeak = swingPeak;
        lastSwingTravel = swingTravel;
        swingPeak = 0f;
        swingTravel = 0f;
        return swung;
    }

    // Brings the smoothed aim joints up to where the hand is right now.
    void SnapAim()
    {
        smoothedPalm = palm.position;
        if (indexTip != null) smoothedIndexTip = indexTip.position;
        if (indexKnuckle != null) smoothedIndexKnuckle = indexKnuckle.position;

        // The jump this makes in the smoothed joints is not movement of the hand.
        Transform head = Head();
        if (head != null) prevOffset = smoothedPalm - head.position;
        if (indexKnuckle != null) prevAlong = (smoothedIndexKnuckle - smoothedPalm).normalized;
    }

    // True while this hand is at the menu panel that stays up during casting.
    bool ReachingForMenu()
    {
        if (QuietPanel == null || !QuietPanel.gameObject.activeInHierarchy) return false;

        Vector3 point = QuietPanel.InverseTransformPoint(indexTip != null ? indexTip.position : palm.position);
        return Mathf.Abs(point.x) <= QuietHalfSize.x && Mathf.Abs(point.y) <= QuietHalfSize.y
            && Mathf.Abs(point.z) <= QuietHalfSize.z;
    }

    // The glow on the hand: on while `pose`'s sign is being read (null = no
    // sign), in that spell's color. Dim while the sign can't cast because the
    // hand points out of the view (aimed = false). Flashes for a moment after
    // a cast.
    void UpdateGlow(SpellPoseDefinition pose, bool aimed = true)
    {
        if (pose != null) glowColor = pose.glowColor;
        glowAimed = Mathf.MoveTowards(glowAimed, aimed ? 1f : 0f, Time.deltaTime / GlowFadeTime);
        glowShown = Mathf.MoveTowards(glowShown, pose != null ? 1f : 0f, Time.deltaTime / GlowFadeTime);
        glowFlash = Mathf.MoveTowards(glowFlash, 0f, Time.deltaTime / GlowFlashTime);
        glowPulse += Time.deltaTime * GlowPulseRate * 2f * Mathf.PI;

        float strength = Mathf.Max(glowShown * Mathf.Lerp(GlowOffAimStrength, GlowHeldStrength, glowAimed), glowFlash);
        if (strength <= 0f)
        {
            if (glow != null && glow.gameObject.activeSelf) glow.gameObject.SetActive(false);
            return;
        }
        if (glow == null && !BuildGlow()) return;
        glow.gameObject.SetActive(true);

        // The middle of the palm, between the wrist and the knuckles.
        glow.position = indexKnuckle != null ? (palm.position + indexKnuckle.position) * 0.5f : palm.position;
        Camera cam = Camera.main;
        if (cam != null)
            glow.rotation = Quaternion.LookRotation(glow.position - cam.transform.position);

        float size = glowSize * (1f + 0.06f * Mathf.Sin(glowPulse)) * (1f + 0.9f * glowFlash);
        glow.localScale = Vector3.one * size;

        Color color = Color.Lerp(glowColor, Color.white, glowFlash * 0.6f);
        color.a = strength;
        glowMaterial.SetColor(GlowColorId, color);
    }

    // A camera-facing quad with a soft round falloff (Resources/SummonHalo, the
    // same shader as the summon totem's halo). The hands are the player's real
    // ones seen through passthrough, so there is no hand model to tint.
    bool BuildGlow()
    {
        Shader shader = Resources.Load<Shader>("SummonHalo");
        if (shader == null)
        {
            Debug.LogWarning("[HandGestureRecognizer] Resources/SummonHalo shader missing -- no hand glow.");
            return false;
        }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name + " Glow";
        Destroy(quad.GetComponent<Collider>()); // spells must not hit the glow
        glowMaterial = new Material(shader);
        var quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.sharedMaterial = glowMaterial;
        quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;
        glow = quad.transform;
        return true;
    }

    // True while this hand's finger shape matches the pose. For non-spell
    // gestures (e.g. SummonTotem's two-finger touch) -- nothing is cast.
    public bool IsHolding(SpellPoseDefinition pose) => pose != null && pose.Matches(fingerCurled);

    void UpdateFingerCurlStates()
    {
        Transform[] tips = { thumbTip, indexTip, middleTip, ringTip, pinkyTip };
        for (int i = 0; i < 5; i++)
        {
            if (tips[i] == null) continue;
            float distance = Vector3.Distance(tips[i].position, palm.position);
            // Hysteresis per finger: use the tighter threshold to start
            // curling, the looser one to release, same trick your fist
            // detector already used, just applied per finger now.
            float threshold = fingerCurled[i] ? openThreshold : closeThreshold;
            fingerCurled[i] = distance < threshold;
        }
    }

    void UpdateSettleSpeed()
    {
        Transform[] tips = { thumbTip, indexTip, middleTip, ringTip, pinkyTip };

        if (!havePrevTips || Time.deltaTime <= 0f)
        {
            currentSettleSpeed = 0f;
            fingerSettleSpeed = 0f;
        }
        else
        {
            float totalDistance = 0f;
            float totalInHand = 0f;
            int count = 0;
            for (int i = 0; i < 5; i++)
            {
                if (tips[i] == null) continue;
                totalDistance += Vector3.Distance(tips[i].position, prevTipPositions[i]);
                totalInHand += Vector3.Distance(palm.InverseTransformPoint(tips[i].position), prevTipInHand[i]);
                count++;
            }
            currentSettleSpeed = count > 0 ? (totalDistance / count) / Time.deltaTime : 0f;
            fingerSettleSpeed = count > 0 ? (totalInHand / count) / Time.deltaTime : 0f;
        }

        for (int i = 0; i < 5; i++)
        {
            if (tips[i] == null) continue;
            prevTipPositions[i] = tips[i].position;
            prevTipInHand[i] = palm.InverseTransformPoint(tips[i].position);
        }
        havePrevTips = true;
    }

    void UpdateAimSmoothing()
    {
        // palm is guaranteed non-null here -- Update() already returned
        // early above if it wasn't. indexTip/indexKnuckle are still
        // optional (not every rig setup drags them in), so each is only
        // smoothed if assigned.
        if (!haveSmoothedAim)
        {
            // First frame: snap straight to the real positions instead of
            // lerping from zero, so the very first cast isn't skewed
            // toward the world origin.
            smoothedPalm = palm.position;
            if (indexTip != null) smoothedIndexTip = indexTip.position;
            if (indexKnuckle != null) smoothedIndexKnuckle = indexKnuckle.position;
            haveSmoothedAim = true;
            return;
        }

        smoothedPalm = Vector3.Lerp(smoothedPalm, palm.position, aimSmoothSpeed * Time.deltaTime);
        if (indexTip != null)
            smoothedIndexTip = Vector3.Lerp(smoothedIndexTip, indexTip.position, aimSmoothSpeed * Time.deltaTime);
        if (indexKnuckle != null)
            smoothedIndexKnuckle = Vector3.Lerp(smoothedIndexKnuckle, indexKnuckle.position, aimSmoothSpeed * Time.deltaTime);
    }

    void OnPoseDetected(SpellPoseDefinition pose, PoseRuntime state)
    {
        Debug.Log($"Pose detected: {pose.spellName}");

        if (pose.effectObject != null)
            pose.effectObject.SetActive(true);

        state.castPending = !CastSpell(pose);
    }

    void OnPoseReleased(SpellPoseDefinition pose)
    {
        Debug.Log($"Pose released: {pose.spellName}");

        if (pose.effectObject != null)
            pose.effectObject.SetActive(false);
    }

    // False only when a cooldown blocked the cast, so the caller can try again
    // while the pose is still held instead of silently dropping it.
    bool CastSpell(SpellPoseDefinition pose)
    {
        if (pose.spellPrefab == null || palm == null) return true; // nothing to cast

        // Global lockout first: don't let two different poses cast within
        // a hair of each other (fast transitions / tracking noise).
        if (Time.time - lastAnyCastTime < globalCastCooldown) return false;

        // Then this spell's own individual cooldown. Both are counted across
        // the two hands.
        if (lastCastTimes.TryGetValue(pose, out float lastCast) && Time.time - lastCast < pose.castCooldown) return false;
        lastCastTimes[pose] = Time.time;
        lastAnyCastTime = Time.time;
        glowColor = pose.glowColor;
        glowFlash = 1f;

        Vector3 direction = GetAimDirection(pose);

        // Aim Pitch Correction was tuned for the generic hand-forward aim
        // (Fire). Point With Fingers is already the real finger direction --
        // stacking the same correction on top of it is exactly what would
        // knock Lightning's angle off, so skip it for that mode.
        if (aimPitchCorrection != 0f && pose.aimMode != SpellPoseDefinition.AimMode.PointWithFingers)
            direction = Quaternion.AngleAxis(aimPitchCorrection, palm.right) * direction;

        Vector3 spawnPosition = palm.position + direction * spawnOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(direction);

        Instantiate(pose.spellPrefab, spawnPosition, spawnRotation);
        return true;
    }

    Vector3 GetAimDirection(SpellPoseDefinition pose)
    {
        // Poses with extended fingers (Scissors/Lightning, later Ice, etc.)
        // can opt into aiming exactly along the index finger instead of the
        // generic hand-forward direction.
        // A flick that dipped away from the aim casts the way the hand pointed
        // when it began (see UpdateFlick).
        bool fromFlickStart = Time.time <= aimHeldUntil;

        if (pose.aimMode == SpellPoseDefinition.AimMode.PointWithFingers
            && indexTip != null && indexKnuckle != null)
        {
            return fromFlickStart ? heldFingerAim : (smoothedIndexTip - smoothedIndexKnuckle).normalized;
        }

        if (aimSource != null)
            return aimSource.forward;
        if (indexKnuckle != null)
            return fromFlickStart ? heldHandAim : (smoothedIndexKnuckle - smoothedPalm).normalized;
        return GetAxisDirection(handForwardAxis);
    }

    // Shared by aiming (handForwardAxis) and the palm-facing check (palmNormalAxis) --
    // both just need "some local axis of the palm transform, as a world direction."
    Vector3 GetAxisDirection(LocalAxis axis)
    {
        switch (axis)
        {
            case LocalAxis.Forward: return palm.forward;
            case LocalAxis.Back:    return -palm.forward;
            case LocalAxis.Up:      return palm.up;
            case LocalAxis.Down:    return -palm.up;
            case LocalAxis.Right:   return palm.right;
            case LocalAxis.Left:    return -palm.right;
            default:                return palm.forward;
        }
    }
}