using UnityEngine;

// Bare-bones projectile for the boss stand-in. Flies toward the point it
// was launched at and dies if it hits real-world furniture on the way
// (so cover works both directions: your spells get blocked by furniture,
// and now the boss's shots do too).
//
// Attach this to whatever prefab you set as BossStandIn's
// "Projectile Prefab" -- a plain sphere primitive with a small Rigidbody
// (or none) is enough for testing.
public class BossProjectile : MonoBehaviour
{
    public float speed = 8f;
    public float lifetime = 5f;

    private Vector3 direction;
    private LayerMask obstacleMask;
    private bool launched = false;

    // Called by BossStandIn right after Instantiate.
    public void Launch(Vector3 targetPosition, LayerMask obstacleMask)
    {
        direction = (targetPosition - transform.position).normalized;
        this.obstacleMask = obstacleMask;
        launched = true;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (!launched) return;

        float step = speed * Time.deltaTime;

        // Check the path just ahead for real-world furniture before
        // actually moving -- prevents tunneling through thin colliders
        // at high speed and gives you a clean "blocked by cover" moment.
        if (Physics.Raycast(transform.position, direction, out RaycastHit hit, step, obstacleMask))
        {
            // Hit real furniture -- stop here instead of passing through.
            transform.position = hit.point;
            Destroy(gameObject);
            return;
        }

        transform.position += direction * step;
    }

    // TODO once you have a real player-hit trigger/collider to check against:
    // void OnTriggerEnter(Collider other) { if (other.CompareTag("Player")) { ... } }
}
