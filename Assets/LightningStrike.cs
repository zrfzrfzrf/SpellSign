using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LightningStrike : MonoBehaviour
{
    [Tooltip("Max distance the bolt can reach if it doesn't hit anything.")]
    public float maxRange = 15f;

    [Tooltip("How long the bolt stays visible before disappearing, in seconds.")]
    public float flashDuration = 0.15f;

    [Tooltip("Width of the core (crisp, bright) line.")]
    public float lineWidth = 0.015f;

    [Tooltip("Width of the outer glow line -- should be noticeably thicker than lineWidth.")]
    public float glowWidth = 0.05f;

    [Tooltip("Which layers the bolt can hit.")]
    public LayerMask hitMask = ~0;

    [Tooltip("How far (in meters) the aim can be off an enemy and the bolt still strikes it. The bolt is a hair-thin line, so without this it slips through the gaps between an enemy's hit colliders. 0 = the aim has to be exact.")]
    public float aimAssistRadius = 0.15f;

    [Header("Jagged shape")]
    [Tooltip("How many straight segments make up the zigzag. More = more jagged detail.")]
    public int segments = 6;

    [Tooltip("How far each middle point can be pushed sideways off the straight line, in meters.")]
    public float jitterAmount = 0.05f;

    [Tooltip("Optional second LineRenderer (on a child object) for the thick blurred glow layer behind the core. Leave empty to just use the main LineRenderer alone.")]
    public LineRenderer glowLine;

    [Tooltip("Optional -- a Particle System set up with a single Burst, placed at the impact point for a spark flash. Leave empty to skip.")]
    public ParticleSystem impactSparks;

    private LineRenderer coreLine;

    static readonly RaycastHit[] assistHits = new RaycastHit[16];

    void Awake()
    {
        coreLine = GetComponent<LineRenderer>();
        coreLine.startWidth = lineWidth;
        coreLine.endWidth = lineWidth;

        if (glowLine != null)
        {
            glowLine.startWidth = glowWidth;
            glowLine.endWidth = glowWidth;
        }

        Vector3 start = transform.position;
        Vector3 direction = transform.forward;
        Vector3 end;

        bool struck = Physics.Raycast(start, direction, out RaycastHit hit, maxRange, hitMask);
        EnemyTarget target = struck ? hit.collider.GetComponentInParent<EnemyTarget>() : null;

        // A near miss still counts: look for an enemy close beside the line, no
        // further away than whatever the exact line hit (so walls still block).
        if (target == null && aimAssistRadius > 0f
            && NearestEnemyAlong(start, direction, struck ? hit.distance : maxRange, out RaycastHit near))
        {
            hit = near;
            struck = true;
            target = hit.collider.GetComponentInParent<EnemyTarget>();
        }

        if (struck)
        {
            end = hit.point;

            Debug.Log("Lightning hit: " + hit.collider.gameObject.name);

            if (target != null)
                target.TakeHit(Element.Lightning, hit.point, hit.collider);
        }
        else
        {
            end = start + direction * maxRange;
        }

        Vector3[] points = BuildJaggedPath(start, end, segments, jitterAmount);

        coreLine.positionCount = points.Length;
        coreLine.SetPositions(points);

        if (glowLine != null)
        {
            glowLine.positionCount = points.Length;
            glowLine.SetPositions(points);
        }

        if (impactSparks != null)
        {
            impactSparks.transform.position = end;
            impactSparks.Play();
        }

        Destroy(gameObject, flashDuration);
    }

    // The closest enemy collider that a ball of aimAssistRadius, swept along the
    // line, would touch.
    private bool NearestEnemyAlong(Vector3 start, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.SphereCastNonAlloc(start, aimAssistRadius, direction, assistHits, distance, hitMask);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = assistHits[i];
            if (h.distance <= 0f) continue; // already touching at the start: no hit point to strike
            if (found && h.distance >= nearest.distance) continue;
            if (h.collider.GetComponentInParent<EnemyTarget>() == null) continue;
            nearest = h;
            found = true;
        }
        return found;
    }

    // Splits the straight start->end line into `segments` pieces and nudges
    // each interior point sideways by a random amount, perpendicular to the
    // bolt's direction -- same idea as the jagged mockup, computed fresh
    // each cast since start/end change depending on where you aim.
    private Vector3[] BuildJaggedPath(Vector3 start, Vector3 end, int segments, float jitter)
    {
        segments = Mathf.Max(1, segments);
        Vector3[] points = new Vector3[segments + 1];

        Vector3 forward = (end - start).normalized;
        Vector3 perpendicular = Vector3.Cross(forward, Vector3.up);
        if (perpendicular.sqrMagnitude < 0.001f)
            perpendicular = Vector3.Cross(forward, Vector3.right);
        perpendicular.Normalize();
        Vector3 up = Vector3.Cross(forward, perpendicular);

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector3 point = Vector3.Lerp(start, end, t);

            if (i != 0 && i != segments)
            {
                point += perpendicular * Random.Range(-jitter, jitter);
                point += up * Random.Range(-jitter, jitter);
            }

            points[i] = point;
        }

        return points;
    }
}