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

public class HandGestureRecognizer : MonoBehaviour
{
    public enum LocalAxis { Forward, Back, Up, Down, Right, Left }

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

    [Header("Palm Facing Check")]
    [Tooltip("Drag your Main Camera / CenterEyeAnchor here. Used only to check whether your palm is facing toward you (looking at your own palm) or away from you -- independent of Aim Source, which may be left empty if you're using hand-aiming. Only matters for poses that enable 'Require Palm Facing Away'.")]
    public Transform headReference;

    [Tooltip("Which local axis of the Palm transform points straight out of your palm (the direction you'd be looking at if you held your hand up to inspect your own palm). This is rig-dependent -- if the facing check seems inverted or unresponsive, try a different axis and rebuild. Debug Log State prints the live dot product so you can tell which axis is correct by watching it flip sign as you turn your hand.")]
    public LocalAxis palmNormalAxis = LocalAxis.Up;

    [Tooltip("Maximum allowed dot product between the palm normal and the direction toward your head for 'facing away' to count. 0 = palm must be at least perpendicular to you (not tilted toward your own face at all). More negative = stricter, requiring the palm to face more directly away.")]
    [Range(-1f, 1f)]
    public float palmAwayDotThreshold = 0f;

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
        public float lastCastTime = -999f;
    }
    private List<PoseRuntime> poseState = new List<PoseRuntime>();

    private float debugLogTimer = 0f;
    private float lastAnyCastTime = -999f;

    // For the movement-settling check: previous frame's fingertip positions.
    private Vector3[] prevTipPositions = new Vector3[5];
    private bool havePrevTips = false;
    private float currentSettleSpeed = 0f;

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

    void Update()
    {
        if (palm == null) return;

        UpdateFingerCurlStates();
        UpdateSettleSpeed();
        UpdateAimSmoothing();
        bool handSettled = currentSettleSpeed <= settleSpeedThreshold;

        float facingDot = 0f;
        bool palmFacingAway = true; // default true so poses without the requirement (or a missing Head Reference) are never blocked
        if (headReference != null)
        {
            Vector3 palmNormal = GetAxisDirection(palmNormalAxis);
            Vector3 toHead = (headReference.position - palm.position).normalized;
            facingDot = Vector3.Dot(palmNormal, toHead);
            palmFacingAway = facingDot <= palmAwayDotThreshold;
        }

        for (int i = 0; i < poses.Count; i++)
        {
            var pose = poses[i];
            if (pose == null) continue;
            var state = poseState[i];

            bool rawMatch = pose.Matches(fingerCurled)
                && (!pose.requirePalmFacingAway || palmFacingAway);

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
                OnPoseReleased(pose);

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
        }

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
                          $"facingDot={facingDot:F2} facingAway={palmFacingAway}");
            }
        }
    }

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
        }
        else
        {
            float totalDistance = 0f;
            int count = 0;
            for (int i = 0; i < 5; i++)
            {
                if (tips[i] == null) continue;
                totalDistance += Vector3.Distance(tips[i].position, prevTipPositions[i]);
                count++;
            }
            currentSettleSpeed = count > 0 ? (totalDistance / count) / Time.deltaTime : 0f;
        }

        for (int i = 0; i < 5; i++)
        {
            if (tips[i] != null)
                prevTipPositions[i] = tips[i].position;
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

        CastSpell(pose, state);
    }

    void OnPoseReleased(SpellPoseDefinition pose)
    {
        Debug.Log($"Pose released: {pose.spellName}");

        if (pose.effectObject != null)
            pose.effectObject.SetActive(false);
    }

    void CastSpell(SpellPoseDefinition pose, PoseRuntime state)
    {
        if (pose.spellPrefab == null || palm == null) return;

        // Global lockout first: don't let two different poses cast within
        // a hair of each other (fast transitions / tracking noise).
        if (Time.time - lastAnyCastTime < globalCastCooldown) return;

        // Then this spell's own individual cooldown.
        if (Time.time - state.lastCastTime < pose.castCooldown) return;
        state.lastCastTime = Time.time;
        lastAnyCastTime = Time.time;

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
    }

    Vector3 GetAimDirection(SpellPoseDefinition pose)
    {
        // Poses with extended fingers (Scissors/Lightning, later Ice, etc.)
        // can opt into aiming exactly along the index finger instead of the
        // generic hand-forward direction.
        if (pose.aimMode == SpellPoseDefinition.AimMode.PointWithFingers
            && indexTip != null && indexKnuckle != null)
        {
            return (smoothedIndexTip - smoothedIndexKnuckle).normalized;
        }

        if (aimSource != null)
            return aimSource.forward;
        if (indexKnuckle != null)
            return (smoothedIndexKnuckle - smoothedPalm).normalized;
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