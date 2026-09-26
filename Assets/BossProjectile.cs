using UnityEngine;

// Bare-bones projectile for the dragon boss. Flies toward the point it was
// launched at and dies if it hits real-world furniture on the way (so cover
// works both directions: your spells get blocked by furniture, and the
// boss's shots do too).
//
// Also does distance-based player-hit detection: the VR player rig has no
// physical Collider (see PlayerController's "Real Hands" setup), so instead
// of OnTriggerEnter we just check, every frame, whether we've gotten within
// hitRadius of playerTarget (your CenterEyeAnchor). That's enough for a
// head-hit check without needing a Collider on the player at all.
//
// Attach this to whatever prefab you set as DragonBoss's "Projectile
// Prefab" -- a plain sphere primitive with a small Rigidbody (or none) is
// enough for testing.
public class BossProjectile : MonoBehaviour
{
    public float speed = 8f;
    public float lifetime = 5f;

    [Header("Player hit")]
    [Tooltip("How close to playerTarget counts as a hit, in meters.")]
    public float hitRadius = 0.3f;
    public int damageAmount = 10;

    private Vector3 direction;
    private LayerMask obstacleMask;
    private Transform playerTarget;
    private bool launched = false;

    // Called by DragonBoss right after Instantiate. playerTarget is
    // optional -- pass it to get player-hit detection; leave it null and
    // this behaves exactly like before (furniture-blocked, but harmless).
    public void Launch(Vector3 targetPosition, LayerMask obstacleMask, Transform playerTarget = null)
    {
        direction = (targetPosition - transform.position).normalized;
        this.obstacleMask = obstacleMask;
        this.playerTarget = playerTarget;
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

        if (playerTarget != null)
        {
            float distToPlayer = Vector3.Distance(transform.position, playerTarget.position);
            if (distToPlayer <= hitRadius)
            {
                PlayerHealth health = playerTarget.GetComponentInParent<PlayerHealth>();
                if (health != null)
                    health.TakeDamage(damageAmount);
                else
                    Debug.LogWarning("[BossProjectile] Hit the player but no PlayerHealth found on/above playerTarget.");

                Destroy(gameObject);
            }
        }
    }
}
