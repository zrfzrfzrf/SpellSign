using UnityEngine;

// Crackling arcs around a projectile: a handful of jagged lines that shoot out
// from its center in random directions and re-randomize several times a
// second. A boss shot flies straight at the player, so a bolt drawn along its
// flight path would be seen end-on, as a dot -- arcs in every direction read
// as lightning from any angle.
//
// Add it to the projectile's root and assign Arc Material (e.g.
// LightningMaterial). The lines are created at runtime.
public class LightningArcs : MonoBehaviour
{
    public Material arcMaterial;

    public int arcCount = 7;
    [Tooltip("How far each arc reaches from the center, in meters.")]
    public float arcLength = 0.25f;
    [Tooltip("Straight pieces per arc. More = more jagged detail.")]
    public int segments = 5;
    [Tooltip("How far each point can be pushed off the straight line, in meters.")]
    public float jitter = 0.045f;
    [Tooltip("Width at the center; arcs taper to a point.")]
    public float width = 0.02f;
    [Tooltip("Seconds between reshuffles. Shorter = more frantic crackle.")]
    public float refreshInterval = 0.06f;

    LineRenderer[] arcs;
    Vector3[] points;
    float timer;

    void Awake()
    {
        segments = Mathf.Max(1, segments);
        points = new Vector3[segments + 1];
        arcs = new LineRenderer[Mathf.Max(0, arcCount)];

        for (int i = 0; i < arcs.Length; i++)
        {
            var arcGo = new GameObject("Arc" + i);
            arcGo.transform.SetParent(transform, false);

            LineRenderer line = arcGo.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.sharedMaterial = arcMaterial;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, width), new Keyframe(1f, 0f));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            arcs[i] = line;
        }

        Reshuffle();
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < refreshInterval) return;
        timer = 0f;
        Reshuffle();
    }

    void Reshuffle()
    {
        foreach (LineRenderer line in arcs)
        {
            Vector3 tip = Random.onUnitSphere * arcLength * Random.Range(0.6f, 1f);
            points[0] = Vector3.zero;
            for (int p = 1; p <= segments; p++)
                points[p] = tip * ((float)p / segments) + Random.insideUnitSphere * jitter;
            line.SetPositions(points);
        }
    }
}
