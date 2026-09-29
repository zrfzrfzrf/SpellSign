using UnityEngine;

// Revives the dead boss when a hand holds a two-finger "V" (index + middle
// extended, other fingers curled -- see SummonPose) against this object for
// chargeTime seconds. While charging, the object shifts from its own color to
// white and a soft halo grows and pulses faster; letting go early drains the
// charge instead of resetting it. Does nothing while the boss is alive.
//
// The object stays grabbable: a grab isn't the V pose, so it never charges.
[RequireComponent(typeof(Collider))]
public class SummonTotem : MonoBehaviour
{
    [Header("References")]
    public EnemyTarget boss;
    [Tooltip("Both hands. Their finger states and index/middle fingertips are reused.")]
    public HandGestureRecognizer[] hands;
    public SpellPoseDefinition summonPose;

    [Header("Charge")]
    public float chargeTime = 2f;
    [Tooltip("Seconds for a full charge to drain away after letting go.")]
    public float drainTime = 1f;
    [Tooltip("How close (m) both fingertips must be to the surface. Inside counts too.")]
    public float touchMargin = 0.03f;

    [Header("Glow")]
    public Color glowColor = Color.white;
    [Tooltip("Halo size relative to the object, when charging starts and at full charge.")]
    public float haloMinSize = 1f;
    public float haloMaxSize = 2.5f;
    [Tooltip("Pulse rate (per second) at the start and end of charging.")]
    public float pulseSlowRate = 1.5f;
    public float pulseFastRate = 8f;
    [Tooltip("Length of the flash when the boss is summoned.")]
    public float flashTime = 0.5f;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    Collider body;
    Material bodyMaterial;
    Color bodyColor;
    Transform halo;
    Material haloMaterial;

    float charge;   // 0..1 while touching
    float flash;    // 1 right after a summon, fades to 0
    float pulsePhase;

    void Awake()
    {
        body = GetComponent<Collider>();
        bodyMaterial = GetComponent<Renderer>().material;
        bodyColor = bodyMaterial.GetColor(BaseColorId);
        CreateHalo();
    }

    void OnDestroy()
    {
        if (halo != null) Destroy(halo.gameObject);
    }

    void Update()
    {
        bool charging = boss != null && boss.IsDead && AnyHandTouching();

        if (charging)
            charge += Time.deltaTime / chargeTime;
        else
            charge -= Time.deltaTime / drainTime;
        charge = Mathf.Clamp01(charge);

        if (charging && charge >= 1f)
        {
            boss.Revive();
            charge = 0f;
            flash = 1f;
        }
        flash = Mathf.Max(0f, flash - Time.deltaTime / flashTime);

        UpdateGlow();
    }

    bool AnyHandTouching()
    {
        foreach (var hand in hands)
        {
            if (hand != null && hand.IsHolding(summonPose)
                && IsTouching(hand.indexTip) && IsTouching(hand.middleTip))
                return true;
        }
        return false;
    }

    bool IsTouching(Transform fingertip)
    {
        if (fingertip == null) return false;
        Vector3 p = fingertip.position;
        return Vector3.Distance(body.ClosestPoint(p), p) <= touchMargin;
    }

    void UpdateGlow()
    {
        pulsePhase += Time.deltaTime * Mathf.Lerp(pulseSlowRate, pulseFastRate, charge) * 2f * Mathf.PI;
        float pulse = 0.5f + 0.5f * Mathf.Sin(pulsePhase); // 0..1
        float strength = Mathf.Max(charge, flash);

        Color c = Color.Lerp(bodyColor, glowColor, strength);
        bodyMaterial.SetColor(BaseColorId, c);
        bodyMaterial.SetColor(ColorId, c);

        if (halo == null) return;
        halo.gameObject.SetActive(strength > 0f);
        if (strength <= 0f) return;

        float size = flash > 0f
            ? haloMaxSize * (1f + 0.6f * (1f - flash))                       // summon flash: expands as it fades
            : Mathf.Lerp(haloMinSize, haloMaxSize, charge) * (1f + 0.1f * charge * pulse);

        Bounds b = body.bounds;
        halo.position = b.center;
        halo.localScale = Vector3.one * size * b.size.magnitude;

        Camera cam = Camera.main;
        if (cam != null)
            halo.rotation = Quaternion.LookRotation(halo.position - cam.transform.position);

        Color hc = glowColor;
        hc.a = flash > 0f ? flash : charge * Mathf.Lerp(0.6f, 1f, pulse);
        haloMaterial.SetColor(ColorId, hc);
    }

    // A camera-facing quad with a soft radial falloff (Resources/SummonHalo).
    // Quest's mobile pipeline has no bloom, so brightening the object alone
    // wouldn't glow past its edges.
    void CreateHalo()
    {
        Shader shader = Resources.Load<Shader>("SummonHalo");
        if (shader == null)
        {
            Debug.LogWarning("[SummonTotem] Resources/SummonHalo shader missing -- no halo.");
            return;
        }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name + " Halo";
        Destroy(quad.GetComponent<Collider>()); // spells must not hit the halo
        haloMaterial = new Material(shader);
        quad.GetComponent<Renderer>().sharedMaterial = haloMaterial;
        halo = quad.transform;
        quad.SetActive(false);
    }
}
