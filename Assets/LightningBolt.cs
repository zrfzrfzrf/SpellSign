using UnityEngine;

// Attach to a "LightningBolt" prefab (an otherwise-empty GameObject with a
// Line Renderer on it -- see setup notes below). Unlike FireballProjectile,
// this doesn't fly through space: it's instant. The moment it spawns, it
// raycasts forward from wherever HandGestureRecognizer placed/aimed it,
// draws a glowing line to whatever it hits, and disappears a moment later.

[RequireComponent(typeof(LineRenderer))]
public class LightningBolt : MonoBehaviour
{
    [Tooltip("Max distance the bolt can reach if it doesn't hit anything.")]
    public float maxRange = 15f;

    [Tooltip("How long the bolt stays visible before disappearing, in seconds.")]
    public float flashDuration = 0.15f;

    [Tooltip("Width of the bolt line.")]
    public float lineWidth = 0.03f;

    [Tooltip("Which layers the bolt can hit. Leave as Everything until you've set up dedicated layers for your boss/environment.")]
    public LayerMask hitMask = ~0;

    private LineRenderer line;

    void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;

        Vector3 start = transform.position;
        Vector3 direction = transform.forward;
        Vector3 end;

        if (Physics.Raycast(start, direction, out RaycastHit hit, maxRange, hitMask))
        {
            end = hit.point;

            // TODO: this is where you'll apply the "Shocked" status to
            // whatever was hit, once the boss has a status-effect component
            // (same TODO spot as FireballProjectile's collision handler).
            Debug.Log("Lightning hit: " + hit.collider.gameObject.name);

            // GetComponentInParent, not GetComponent -- the hit collider may
            // be on a CHILD part of a multi-mesh model (e.g. the dragon's
            // Cube/Cube.002), while EnemyTarget sits on the root object.
            EnemyTarget target = hit.collider.GetComponentInParent<EnemyTarget>();
            if (target != null)
                target.TakeHit(Element.Lightning, hit.point);
        }
        else
        {
            end = start + direction * maxRange;
        }

        line.SetPosition(0, start);
        line.SetPosition(1, end);

        Destroy(gameObject, flashDuration);
    }
}