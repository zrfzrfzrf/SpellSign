using UnityEngine;
using UnityEngine.AI;

// Temporary stand-in "boss" for testing navigation + attacks against real
// furniture BEFORE the real boss model/animations exist.
//
// Setup:
//   1. Put this on your Capsule (or the Breeze character from the NavMesh
//      sample). Make sure it also has a NavMeshAgent component.
//   2. Bake a NavMesh over your RealWorldMesh (Window -> AI -> Navigation,
//      or MRUK's runtime nav-mesh baking if you're generating it at
//      runtime after the room loads).
//   3. Set "target" to whatever represents the player -- usually your
//      OVRCameraRig's Main Camera / CenterEyeAnchor transform.
//   4. Assign a projectilePrefab (any simple object with the
//      BossProjectile script on it -- a sphere primitive works fine
//      for now, you don't need real VFX yet).
//   5. Set obstacleMask to whatever layer your RealWorldMesh colliders
//      are on (Default, if you didn't change it).
[RequireComponent(typeof(NavMeshAgent))]
public class BossStandIn : MonoBehaviour
{
    [Header("Who to chase / shoot at")]
    public Transform target;

    [Header("Movement")]
    [Tooltip("How often (seconds) to re-path toward the target. Doesn't need to be every frame.")]
    public float repathInterval = 0.5f;

    [Header("Attack")]
    public GameObject projectilePrefab;
    [Tooltip("Max distance the boss will try to shoot from.")]
    public float attackRange = 6f;
    [Tooltip("Seconds between shots.")]
    public float attackCooldown = 2f;
    [Tooltip("Where projectiles spawn from -- assign an empty child positioned at the boss's 'mouth'/muzzle. If left empty, uses this transform.")]
    public Transform muzzle;

    [Header("Line of sight")]
    [Tooltip("Layers that can block line of sight -- put your RealWorldMesh colliders' layer here.")]
    public LayerMask obstacleMask;

    private NavMeshAgent agent;
    private float repathTimer;
    private float attackTimer;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (muzzle == null) muzzle = transform;
    }

    void OnEnable()
    {
        // A NavMeshAgent only tries to place itself onto a NavMesh ONCE,
        // right when it's created. If RuntimeNavMeshBaker hasn't baked
        // yet at that point (normal on first launch -- the real room
        // hasn't loaded), the agent gives up and stays "not on a
        // NavMesh" forever, even after a NavMesh appears later. So we
        // re-place it ourselves every time a bake finishes.
        RuntimeNavMeshBaker.OnBaked += HandleNavMeshBaked;
    }

    void OnDisable()
    {
        RuntimeNavMeshBaker.OnBaked -= HandleNavMeshBaked;
    }

    void HandleNavMeshBaked()
    {
        // Snap onto the nearest point on the freshly baked NavMesh so the
        // agent becomes valid again. Wide radius on purpose -- we'd
        // rather the boss snap to SOME valid floor point than fail
        // outright, especially once this runs in a big museum hall where
        // you can't hand-place it precisely ahead of time.
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 50f, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            Debug.Log("[BossStandIn] Re-placed agent onto freshly baked NavMesh.");
        }
        else
        {
            Debug.LogWarning("[BossStandIn] No NavMesh found within 50m of the boss's position -- move it closer to the scanned room.");
        }
    }

    private float debugLogTimer;
    private bool lastSetDestinationSucceeded;

    void Update()
    {
        if (target == null) return;

        // --- Movement: keep chasing the player, respecting the NavMesh
        // baked over the real room (so it walks around furniture). ---
        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            // SetDestination returns false if it couldn't find a valid
            // path -- we were never checking this, so failures were
            // invisible. Log it now instead of guessing.
            lastSetDestinationSucceeded = agent.SetDestination(target.position);
            repathTimer = repathInterval;
        }

        // --- Diagnostics: print exactly what the agent thinks is
        // happening every 2 seconds, so we can see WHY it's not moving
        // instead of guessing. Remove this once movement is confirmed
        // working.
        debugLogTimer -= Time.deltaTime;
        if (debugLogTimer <= 0f)
        {
            debugLogTimer = 2f;
            Debug.Log($"[BossStandIn DEBUG] onNavMesh={agent.isOnNavMesh} " +
                      $"setDestOK={lastSetDestinationSucceeded} " +
                      $"hasPath={agent.hasPath} pathStatus={agent.pathStatus} " +
                      $"remainingDist={agent.remainingDistance:F2} " +
                      $"velocity={agent.velocity.magnitude:F2} " +
                      $"isStopped={agent.isStopped} " +
                      $"pos={transform.position} target={target.position}");
        }

        // --- Attack: only fire if close enough AND nothing (furniture)
        // is blocking the shot. This is your "cover" mechanic -- if the
        // player is hiding behind a real object, HasLineOfSight returns
        // false and the boss holds fire. ---
        attackTimer -= Time.deltaTime;
        float distance = Vector3.Distance(transform.position, target.position);

        if (distance <= attackRange && attackTimer <= 0f && HasLineOfSight())
        {
            Shoot();
            attackTimer = attackCooldown;
        }
    }

    bool HasLineOfSight()
    {
        Vector3 origin = muzzle.position;
        Vector3 toTarget = target.position - origin;

        // If a raycast toward the player hits something on the obstacle
        // layer BEFORE reaching the player, the player is covered.
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, obstacleMask))
        {
            // Hit something before reaching the player -> blocked.
            return false;
        }
        return true;
    }

    void Shoot()
    {
        if (projectilePrefab == null) return;

        GameObject proj = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
        BossProjectile bp = proj.GetComponent<BossProjectile>();
        if (bp != null)
        {
            bp.Launch(target.position, obstacleMask, Element.Fire);
        }
    }

    // Visualize attack range + a line-of-sight check in the Scene view.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}