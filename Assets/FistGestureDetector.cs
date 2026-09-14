using UnityEngine;

public class FistGestureDetector : MonoBehaviour
{
    public enum LocalAxis { Forward, Back, Up, Down, Right, Left }
    [Header("Drag these from OVRHandVisualRight/Left's Hand Visual component")]
    public Transform palm;       // drag HandWristRoot here
    public Transform thumbTip;   // drag HandThumbTip here
    public Transform indexTip;   // drag HandIndexTip here
    public Transform middleTip;  // drag HandMiddleTip here
    public Transform ringTip;    // drag HandRingTip here
    public Transform pinkyTip;   // drag HandPinkyTip here

    [Tooltip("How close (in meters) a fingertip must get to the palm to COUNT as curled (fist forming). Smaller = stricter.")]
    public float closeThreshold = 0.10f;

    [Tooltip("How far (in meters) a fingertip must move from the palm to count as UN-curled (fist releasing). Should be a bit larger than closeThreshold, so detection doesn't flicker right at the edge.")]
    public float openThreshold = 0.13f;

    [Tooltip("How many fingers must be curled to count as a fist (out of 5)")]
    public int fingersRequiredForFist = 3;

    [Header("Visual effect (optional)")]
    [Tooltip("Any GameObject to show while the fist is held -- e.g. a glowing sphere or particle system. It will follow your hand and turn on/off automatically.")]
    public GameObject effectObject;

    [Header("Fireball")]
    [Tooltip("Drag your Fireball prefab here (a sphere with a Rigidbody and the FireballProjectile script).")]
    public GameObject fireballPrefab;

    [Tooltip("Which direction the fireball flies. Drag your Main Camera / CenterEyeAnchor here so it throws in the direction you're LOOKING. Leave EMPTY to throw in the direction your HAND is pointing instead -- if you want hand-aiming, this is the one you want empty.")]
    public Transform aimSource;

    [Tooltip("Only used when Aim Source is empty AND Index Knuckle is empty. Different hand rigs orient their local axes differently, so you may need to test which of these actually points 'out through your fingers' for YOUR rig.")]
    public LocalAxis handForwardAxis = LocalAxis.Forward;

    [Tooltip("RECOMMENDED for hand-aiming: drag your index finger's KNUCKLE joint here (e.g. XRHand_IndexProximal or XRHand_IndexMetacarpal -- NOT the tip). The fireball will fly in the direction from your wrist through this knuckle, which is the natural 'pointing straight out from your fist' direction and stays accurate even while your fingers are curled.")]
    public Transform indexKnuckle;

    [Tooltip("How far in front of your palm the fireball spawns, so it doesn't spawn inside your own hand model.")]
    public float spawnOffset = 0.25f;

    [Tooltip("Minimum time (seconds) between fireball throws, even if the fist re-triggers rapidly. Prevents accidental double/triple-fires from a single fist motion.")]
    public float throwCooldown = 0.4f;

    [Tooltip("How long (seconds) your hand must CONTINUOUSLY read as a fist before it actually counts. Filters out brief hand-tracking glitches that happen when your hands get close to your body/face and briefly leave the headset's camera view. Higher = more reliable but slightly less instant-feeling; 0.1-0.15 is a good starting point.")]
    public float fistConfirmDelay = 0.12f;

    [Tooltip("Fine-tune correction for up/down aim, in degrees. If the fireball flies too high, use a NEGATIVE number (e.g. -15) to angle it back down. If it flies too low, use a positive number. Adjust in small steps and rebuild to test.")]
    public float aimPitchCorrection = 0f;

    [Header("Debugging")]
    [Tooltip("If checked, logs live finger distances every half second so you can tune the thresholds above based on real numbers instead of guessing.")]
    public bool debugLogDistances = false;

    private bool wasFist = false;
    private float debugLogTimer = 0f;
    private float lastThrowTime = -999f;
    private float fistHoldTimer = 0f;

    void Start()
    {
        // Start hidden -- it only appears while you're actually holding the fist
        if (effectObject != null)
            effectObject.SetActive(false);
    }

    void Update()
    {
        if (palm == null)
            return;

        bool rawIsFist = CheckIsFist();

        // Debounce: only count time toward "confirmed" while the raw
        // reading is continuously true. Any single bad frame from a
        // tracking glitch resets the timer back to zero, so a glitch
        // can't sneak through as a real detected/released event.
        if (rawIsFist)
        {
            fistHoldTimer += Time.deltaTime;
        }
        else
        {
            fistHoldTimer = 0f;
        }

        bool isFist = wasFist
            ? rawIsFist                              // once confirmed closed, releasing is immediate (feels responsive)
            : (fistHoldTimer >= fistConfirmDelay);    // but CLOSING requires holding steady first

        // Only fire once when the pose changes, not every single frame
        if (isFist && !wasFist)
        {
            OnFistDetected();
        }
        else if (!isFist && wasFist)
        {
            OnFistReleased();
        }

        // Keep the effect glued to your palm every frame while it's active
        if (isFist && effectObject != null)
        {
            effectObject.transform.position = palm.position;
        }

        wasFist = isFist;

        // Optional: print live distances every half second so you can
        // see real numbers and tune closeThreshold/openThreshold/
        // fingersRequiredForFist based on what your actual hand does,
        // instead of guessing.
        if (debugLogDistances)
        {
            debugLogTimer += Time.deltaTime;
            if (debugLogTimer > 0.5f)
            {
                debugLogTimer = 0f;
                Debug.Log(
                    $"thumb={Vector3.Distance(thumbTip.position, palm.position):F3} " +
                    $"index={Vector3.Distance(indexTip.position, palm.position):F3} " +
                    $"middle={Vector3.Distance(middleTip.position, palm.position):F3} " +
                    $"ring={Vector3.Distance(ringTip.position, palm.position):F3} " +
                    $"pinky={Vector3.Distance(pinkyTip.position, palm.position):F3} " +
                    $"isFist={isFist}"
                );
            }
        }
    }

    bool CheckIsFist()
    {
        // Hysteresis: use the tighter threshold to trigger a fist,
        // but the looser threshold to release it. This stops the
        // detection from flickering rapidly when your hand sits
        // right at the boundary (which happens a lot with real
        // hand-tracking data -- it's never perfectly still).
        float threshold = wasFist ? openThreshold : closeThreshold;

        Transform[] fingertips = { thumbTip, indexTip, middleTip, ringTip, pinkyTip };

        int curledCount = 0;
        foreach (var tip in fingertips)
        {
            if (tip == null) continue;

            float distance = Vector3.Distance(tip.position, palm.position);
            if (distance < threshold)
                curledCount++;
        }

        return curledCount >= fingersRequiredForFist;
    }

    void OnFistDetected()
    {
        Debug.Log("Fist detected! Casting fireball.");

        if (effectObject != null)
            effectObject.SetActive(true);

        ThrowFireball();
    }

    void ThrowFireball()
    {
        if (fireballPrefab == null || palm == null)
            return;

        // Cooldown guard: even if OnFistDetected somehow fires again very
        // quickly (rapid open/close, tracking jitter, etc.), don't let
        // fireballs spam out faster than this interval.
        if (Time.time - lastThrowTime < throwCooldown)
            return;
        lastThrowTime = Time.time;

        // Priority order for aiming direction:
        // 1. aimSource (e.g. camera) if assigned -- throw where you're LOOKING
        // 2. indexKnuckle if assigned -- throw where your fist is POINTING
        //    (wrist -> knuckle direction, accurate even mid-fist)
        // 3. palm's local axis as a last-resort fallback
        Vector3 direction;
        if (aimSource != null)
        {
            direction = aimSource.forward;
        }
        else if (indexKnuckle != null)
        {
            direction = (indexKnuckle.position - palm.position).normalized;
        }
        else
        {
            direction = GetPalmDirection();
        }

        // Apply the manual up/down fine-tune correction, rotating around
        // the palm's own right axis so it tilts relative to your hand,
        // not the world.
        if (aimPitchCorrection != 0f)
        {
            direction = Quaternion.AngleAxis(aimPitchCorrection, palm.right) * direction;
        }

        Vector3 spawnPosition = palm.position + direction * spawnOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(direction);

        Instantiate(fireballPrefab, spawnPosition, spawnRotation);
    }

    Vector3 GetPalmDirection()
    {
        switch (handForwardAxis)
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

    void OnFistReleased()
    {
        Debug.Log("Fist released.");

        if (effectObject != null)
            effectObject.SetActive(false);
    }
}