using UnityEngine;
using Meta.XR.MRUtilityKit;

// Bare-bones projectile for the dragon boss. Flies toward the point it was
// launched at and dies if it hits real-world furniture on the way (so cover
// works both directions: your spells get blocked by furniture, and the
// boss's shots do too).
//
// "Furniture" means the scanned room only -- colliders under an MRUK anchor
// (walls, floor, furniture, the room mesh). The dragon itself and the
// player's spells sit on the same layer but never stop a shot. Cover also
// protects from a near miss: a shot within hitRadius of the player's head
// does no damage while room geometry is between the two.
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
    
    //0 fire
    //1 lightning
    //2 water
    private Element element;

    static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    // Called by DragonBoss right after Instantiate. playerTarget is
    // optional -- pass it to get player-hit detection; leave it null and
    // this behaves exactly like before (furniture-blocked, but harmless).
    public void Launch(Vector3 targetPosition, LayerMask obstacleMask, Element element, Transform playerTarget = null)
    {
        direction = (targetPosition - transform.position).normalized;
        this.obstacleMask = obstacleMask;
        this.playerTarget = playerTarget;
        launched = true;
        this.element = element;
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        if (!launched) return;

        float step = speed * Time.deltaTime;

        // Check the path just ahead for real-world furniture before
        // actually moving -- prevents tunneling through thin colliders
        // at high speed and gives you a clean "blocked by cover" moment.
        if (RoomRaycast(transform.position, direction, step, out RaycastHit hit))
        {
            // Hit real furniture -- stop here instead of passing through.
            transform.position = hit.point;
            Destroy(gameObject);
            return;
        }

        transform.position += direction * step;

        if (playerTarget != null)
        {
            // The player's sigil shield stops the shot at its edge, no damage.
            if (SpellSign.Sigils.SigilShield.Blocks(transform.position, element))
            {
                Destroy(gameObject);
                return;
            }

            Vector3 toPlayer = playerTarget.position - transform.position;
            float distToPlayer = toPlayer.magnitude;
            // Behind cover the shot keeps flying instead: it stops at the
            // wall, or hits after all once it has a clear line past the edge.
            if (distToPlayer <= hitRadius && !RoomRaycast(transform.position, toPlayer.normalized, distToPlayer, out _))
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

    // Nearest scanned-room surface along the ray, skipping everything else on
    // obstacleMask.
    bool RoomRaycast(Vector3 origin, Vector3 dir, float distance, out RaycastHit nearest)
    {
        nearest = default;
        if (obstacleMask.value == 0 || distance <= 0f) return false;

        int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, distance, obstacleMask, QueryTriggerInteraction.Ignore);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (found && h.distance >= nearest.distance) continue;
            if (h.collider.GetComponentInParent<MRUKAnchor>() == null) continue;
            nearest = h;
            found = true;
        }
        return found;
    }
}
