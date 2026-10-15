using UnityEngine;

// A see-through hand that shows the player a spell's sign, over and over: it
// relaxes, forms the sign, pushes forward with a flash of the spell's color,
// and comes back. Tinted in the spell's color.
//
// It is Meta's own right-hand model (OculusHand_R, the same mesh the headset
// draws for tracked hands) with its finger bones bent from code. Which way
// each bone bends is worked out once, when it is built: for a finger, the
// turn that moves its tip furthest to the palm side of the hand; for the
// thumb, the one that brings its tip toward the base of the little finger.
public class GhostHand : MonoBehaviour
{
    // Per finger, thumb first: its three bones from the knuckle out, and its tip.
    static readonly string[,] Bones =
    {
        { "b_r_thumb1", "b_r_thumb2", "b_r_thumb3" },
        { "b_r_index1", "b_r_index2", "b_r_index3" },
        { "b_r_middle1", "b_r_middle2", "b_r_middle3" },
        { "b_r_ring1", "b_r_ring2", "b_r_ring3" },
        { "b_r_pinky1", "b_r_pinky2", "b_r_pinky3" },
    };
    static readonly string[] Tips =
    {
        "r_thumb_finger_tip_marker", "r_index_finger_tip_marker", "r_middle_finger_tip_marker",
        "r_ring_finger_tip_marker", "r_pinky_finger_tip_marker",
    };
    const string Wrist = "b_r_wrist";
    const string PinkyBase = "r_pinky_palm_knuckle_marker"; // where a tucked thumb reaches toward

    // Degrees each bone bends in a curled finger, knuckle first; the thumb tucks in less.
    static readonly float[] FingerCurl = { 70f, 95f, 65f };
    static readonly float[] ThumbCurl = { 25f, 40f, 45f };
    const float Relaxed = 0.15f;      // how curled the fingers are between signs (0 = straight, 1 = curled)
    const float Loop = 2.6f;          // seconds for one showing of the sign
    const float PushDistance = 0.18f; // meters it pushes forward
    const float FlashSize = 0.22f;    // meters across, the flash at the end of the push
    const float Opacity = 0.65f;
    const float EmphasisGrow = 0.35f; // how much bigger it gets when the player needs help

    static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
    static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineOpacityId = Shader.PropertyToID("_OutlineOpacity");
    static readonly int HaloColorId = Shader.PropertyToID("_Color");

    // 0..1: how much it insists. The tutorial raises it when the player is stuck.
    public float Emphasis { get; set; }

    Transform model;
    Transform[,] bones = new Transform[5, 3];
    Quaternion[,] rest = new Quaternion[5, 3];
    Vector3[,] bendAxis = new Vector3[5, 3];  // local axis each bone bends about, toward the palm
    float[] target = new float[5];            // 0..1 curl of each finger in the sign being shown
    Material material;
    Transform flash;
    Material flashMaterial;
    Color color = Color.white;
    float clock;
    float shownEmphasis;

    // handModel: the OculusHand_R model. handMaterial: a material for it (the
    // Interaction SDK's OculusHand); without one it is drawn plain.
    public static GhostHand Create(GameObject handModel, Material handMaterial)
    {
        var go = new GameObject("Ghost Hand");
        var ghost = go.AddComponent<GhostHand>();
        if (!ghost.Build(handModel, handMaterial))
        {
            Destroy(go);
            return null;
        }
        return ghost;
    }

    // Shows this sign from now on, in this color, starting from a relaxed hand.
    public void Show(SpellPoseDefinition pose, Color tint)
    {
        SpellPoseDefinition.FingerState[] states = { pose.thumb, pose.index, pose.middle, pose.ring, pose.pinky };

        // A finger the sign doesn't care about does what looks natural: the
        // thumb tucks in when most fingers are curled and stays out otherwise.
        int curled = 0;
        for (int f = 1; f < 5; f++) if (states[f] == SpellPoseDefinition.FingerState.MustBeCurled) curled++;
        for (int f = 0; f < 5; f++)
        {
            target[f] = states[f] == SpellPoseDefinition.FingerState.MustBeCurled ? 1f
                      : states[f] == SpellPoseDefinition.FingerState.MustBeOpen ? 0f
                      : curled >= 2 ? 1f : 0f;
        }

        color = tint;
        clock = 0f;
        Tint(color);
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    // Where the wrist is and which way the hand points; the palm faces `palmFacing`.
    public void Place(Vector3 wrist, Vector3 pointing, Vector3 palmFacing)
    {
        // The model's wrist: +X runs along the fingers, -Y out of the palm.
        Vector3 along = pointing.normalized;
        Vector3 back = -Vector3.ProjectOnPlane(palmFacing, along).normalized;
        if (back.sqrMagnitude < 0.0001f) back = Vector3.up;
        Vector3 thumbSide = Vector3.Cross(along, back);
        transform.SetPositionAndRotation(wrist, Quaternion.LookRotation(thumbSide, back));
    }

    void Update()
    {
        if (model == null) return;
        clock = (clock + Time.deltaTime) % Loop;
        shownEmphasis = Mathf.MoveTowards(shownEmphasis, Emphasis, Time.deltaTime * 2f);

        // relax | form the sign | hold | push | hold out | come back | rest
        float form = Smooth(clock, 0.35f, 0.75f) * (1f - Smooth(clock, 1.8f, 2.4f));
        float push = Smooth(clock, 1.15f, 1.4f) * (1f - Smooth(clock, 1.8f, 2.4f));
        float burst = clock >= 1.4f ? Mathf.Clamp01(1f - (clock - 1.4f) / 0.35f) : 0f;

        for (int f = 0; f < 5; f++)
        {
            float curl = Mathf.Lerp(Relaxed, target[f], form);
            float[] angles = f == 0 ? ThumbCurl : FingerCurl;
            for (int j = 0; j < 3; j++)
                if (bones[f, j] != null)
                    bones[f, j].localRotation = rest[f, j] * Quaternion.AngleAxis(angles[j] * curl, bendAxis[f, j]);
        }

        float grow = 1f + EmphasisGrow * shownEmphasis;
        model.localScale = Vector3.one * grow;
        model.localPosition = Vector3.right * PushDistance * push; // +X: the way the fingers point

        material.SetFloat(OpacityId, Mathf.Lerp(Opacity, 0.9f, shownEmphasis));

        if (flash != null)
        {
            Camera cam = Camera.main;
            flash.position = model.TransformPoint(Vector3.right * 0.12f);
            if (cam != null) flash.rotation = Quaternion.LookRotation(flash.position - cam.transform.position);
            flash.localScale = Vector3.one * FlashSize * grow * (0.6f + burst);
            Color c = color;
            c.a = burst;
            flashMaterial.SetColor(HaloColorId, c);
            flash.gameObject.SetActive(burst > 0f);
        }
    }

    static float Smooth(float t, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, t));

    void Tint(Color tint)
    {
        if (material == null) return;
        Color top = Color.Lerp(tint, Color.white, 0.35f), bottom = tint;
        if (material.HasProperty(ColorTopId))
        {
            material.SetColor(ColorTopId, top);
            material.SetColor(ColorBottomId, bottom);
            material.SetColor(OutlineColorId, tint);
            material.SetFloat(OutlineOpacityId, 1f);
        }
        else
            material.color = new Color(tint.r, tint.g, tint.b, Opacity);
    }

    bool Build(GameObject handModel, Material handMaterial)
    {
        if (handModel == null)
        {
            Debug.LogWarning("[GhostHand] No hand model assigned -- no ghost hand.");
            return false;
        }

        model = Instantiate(handModel, transform, false).transform;
        model.name = "Hand";
        Transform wrist = Find(Wrist), pinkyBase = Find(PinkyBase);

        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skin == null || wrist == null || pinkyBase == null)
        {
            Debug.LogWarning("[GhostHand] The hand model isn't the OculusHand_R rig this expects -- no ghost hand.");
            return false;
        }
        skin.updateWhenOffscreen = true; // the bones move well outside the bind-pose bounds
        skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        material = new Material(handMaterial != null ? handMaterial : skin.sharedMaterial);
        skin.sharedMaterial = material;

        for (int f = 0; f < 5; f++)
        {
            Transform tip = Find(Tips[f]);
            for (int j = 0; j < 3; j++)
            {
                bones[f, j] = Find(Bones[f, j]);
                if (bones[f, j] == null || tip == null) continue;
                rest[f, j] = bones[f, j].localRotation;
                bendAxis[f, j] = f == 0 ? BendAxis(bones[f, j], () => -Vector3.Distance(tip.position, pinkyBase.position))
                                        : BendAxis(bones[f, j], () => -wrist.InverseTransformPoint(tip.position).y); // the palm is the wrist's -Y side
            }
        }

        Shader haloShader = Resources.Load<Shader>("SummonHalo");
        if (haloShader != null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Flash";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            flashMaterial = new Material(haloShader);
            quad.GetComponent<Renderer>().sharedMaterial = flashMaterial;
            flash = quad.transform;
            quad.SetActive(false);
        }
        return true;
    }

    // Of the bone's three local axes, both ways round, the turn that scores
    // highest: the way the finger curls.
    static Vector3 BendAxis(Transform bone, System.Func<float> score)
    {
        Quaternion start = bone.localRotation;
        Vector3 best = Vector3.forward;
        float bestScore = float.MinValue;
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.left, Vector3.down, Vector3.back })
        {
            bone.localRotation = start * Quaternion.AngleAxis(45f, axis);
            float value = score();
            if (value > bestScore)
            {
                bestScore = value;
                best = axis;
            }
        }
        bone.localRotation = start;
        return best;
    }

    Transform Find(string boneName)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == boneName) return t;
        return null;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (flashMaterial != null) Destroy(flashMaterial);
    }
}
