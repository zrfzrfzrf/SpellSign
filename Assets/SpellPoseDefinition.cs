using UnityEngine;

// A data asset describing ONE spell's hand pose + what it casts.
//
// Create one via: right-click in Project window -> Create -> SpellSign -> Spell Pose Definition
// Make one per spell (Fire, Water, Ice, Lightning...). Set each finger's
// required state, drag in the projectile prefab, tune cooldown -- no code
// needed to add a new spell.
//
// Finger states:
//   Any          - don't care what this finger is doing
//   Must Be Open - this finger must be extended (NOT curled) for this pose to match
//   Must Be Curled - this finger must be curled in for this pose to match
//
// Examples:
//   Fire (flat hand)   -> index/middle/ring/pinky "Must Be Open", thumb Any, Require Palm Facing Down
//   Water (horns)      -> index + pinky "Must Be Open", middle + ring "Must Be Curled"
//   Lightning (point)  -> index "Must Be Open", middle/ring/pinky "Must Be Curled"
[CreateAssetMenu(fileName = "NewSpellPose", menuName = "SpellSign/Spell Pose Definition")]
public class SpellPoseDefinition : ScriptableObject
{
    public enum FingerState { Any, MustBeOpen, MustBeCurled }

    [Header("Identity")]
    public string spellName = "Fireball";

    [Header("Required finger states")]
    public FingerState thumb = FingerState.Any;
    public FingerState index = FingerState.Any;
    public FingerState middle = FingerState.Any;
    public FingerState ring = FingerState.Any;
    public FingerState pinky = FingerState.Any;

    [Header("What it casts")]
    [Tooltip("Prefab to spawn when this pose is confirmed (e.g. a fireball, a water-blob, an ice-shard projectile).")]
    public GameObject spellPrefab;

    [Tooltip("Optional: a GameObject in the scene (e.g. a glow effect) to show while this exact pose is held. Leave empty if not needed.")]
    public GameObject effectObject;

    [Tooltip("Color of the glow on the hand while this sign is being read.")]
    public Color glowColor = Color.white;

    [Header("Timing")]
    [Tooltip("How long (seconds) this exact pose must be held continuously before it counts as confirmed. Filters out brief hand-tracking glitches. 0.1-0.15 is a good starting point.")]
    public float poseConfirmDelay = 0.12f;

    [Tooltip("Minimum time (seconds) between casts of THIS spell specifically.")]
    public float castCooldown = 0.4f;

    public enum AimMode { UseHandDefault, PointWithFingers, BetweenIndexAndPinky }

    [Header("Aiming")]
    [Tooltip("Use Hand Default = the same aimSource -> indexKnuckle -> local axis fallback every pose used before -- good for poses like a fist where nothing is extended to point with. " +
             "Point With Fingers = aim exactly along your extended index finger (index knuckle -> index tip), which reads far more naturally for poses like Scissors/Lightning where you're actually pointing at something. " +
             "Between Index And Pinky = from the wrist to halfway between the index and pinky tips, down the middle of the horns (Water).")]
    public AimMode aimMode = AimMode.UseHandDefault;

    [Header("Tutorial")]
    [Tooltip("The tutorial's ghost hand shows this sign upside down, palm up, the way it is meant to be made. It doesn't change how the sign is recognized.")]
    public bool shownPalmUp = false;

    [Header("Extra requirement")]
    [Tooltip("If enabled, this pose can only be confirmed while your palm is facing AWAY from your own head (not looking at your own palm). Uses the Head Reference / Palm Normal Axis settings on HandGestureRecognizer -- leave this off for poses where palm orientation shouldn't matter.")]
    public bool requirePalmFacingAway = false;

    [Tooltip("If enabled, this pose can only be confirmed while your palm faces the floor -- a hand held out flat, not one hanging at your side. How far it may tilt is set by Palm Down Dot Threshold on HandGestureRecognizer.")]
    public bool requirePalmFacingDown = false;

    // curled[0]=thumb, [1]=index, [2]=middle, [3]=ring, [4]=pinky
    public bool Matches(bool[] curled)
    {
        FingerState[] required = { thumb, index, middle, ring, pinky };
        for (int i = 0; i < 5; i++)
        {
            if (required[i] == FingerState.Any) continue;

            bool needsCurled = required[i] == FingerState.MustBeCurled;
            if (curled[i] != needsCurled)
                return false;
        }
        return true;
    }
}