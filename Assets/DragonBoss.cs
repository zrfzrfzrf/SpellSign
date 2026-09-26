using UnityEngine;
using System.Collections;
using Meta.XR.MRUtilityKit;

// Stationary dragon boss: no NavMesh, no pathing -- it places itself once,
// then just stands there and shoots projectiles at the player. Solves the
// "where do I put it in Unity" problem by computing its own spot at runtime,
// relative to wherever the player actually is when the app starts (not a
// hardcoded world position, which lands differently every run -- see
// BossStandIn.cs for why that matters).
//
// Setup:
//   1. Drag your dragon model into the scene.
//   2. Add a Collider on it (roughly matching its size) and this script.
//   3. Set Target to your CenterEyeAnchor.
//   4. Set Floor Mask and Obstacle Mask to whatever layer your
//      RealWorldMesh colliders are on (Default, most likely).
//   5. Assign Projectile Prefab to your existing boss fireball prefab
//      (anything with BossProjectile on it).
[RequireComponent(typeof(Collider))]
public class DragonBoss : MonoBehaviour
{
    [Header("Who to shoot at")]
    public Transform target; // your CenterEyeAnchor

    [Header("Placement")]
    [Tooltip("How far in front of the player to place the dragon, in meters. Lower this if your room is small.")]
    public float spawnDistance = 2.5f;
    [Tooltip("How high above the player's head to start the floor-detection raycast from. Keep this small -- if it puts the start point above your room's actual ceiling, the ray hits the ceiling first and mistakes it for the floor.")]
    public float raycastStartHeight = 0.5f;
    [Tooltip("Layer(s) your real-world mesh colliders are on -- usually 'Default'. Used to find the actual floor height under the spawn point.")]
    public LayerMask floorMask;
    [Tooltip("If true, the dragon visibly drops from spawn height down to the floor instead of appearing already grounded.")]
    public bool animateDrop = true;
    public float dropSpeed = 4f;

    [Header("Attack")]
    public GameObject projectilePrefab;
    public float attackRange = 8f;
    public float attackCooldown = 2f;
    [Tooltip("Where projectiles spawn from. If left empty, uses this transform.")]
    public Transform muzzle;
    [Tooltip("Layers that can block line of sight -- your RealWorldMesh colliders.")]
    public LayerMask obstacleMask;

    private float attackTimer;
    private float targetFloorY;
    private bool grounded;
    private bool placed;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;
    }

    void OnEnable()
    {
        StartCoroutine(PlaceWhenReady());
    }

    IEnumerator PlaceWhenReady()
    {
        while (MRUK.Instance == null)
            yield return null;

        // Same pattern as RuntimeNavMeshBaker: this fires immediately if the
        // room already finished loading by the time we subscribe.
        MRUK.Instance.SceneLoadedEvent.AddListener(PlaceInFrontOfPlayer);
    }

    void OnDisable()
    {
        if (MRUK.Instance != null)
            MRUK.Instance.SceneLoadedEvent.RemoveListener(PlaceInFrontOfPlayer);
    }

    void PlaceInFrontOfPlayer()
    {
        if (target == null)
        {
            Debug.LogWarning("[DragonBoss] No target assigned -- can't place relative to the player.");
            return;
        }

        Vector3 flatForward = target.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 desiredXZ = target.position + flatForward * spawnDistance;
        Vector3 rayStart = new Vector3(desiredXZ.x, target.position.y + raycastStartHeight, desiredXZ.z);

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastStartHeight + 5f, floorMask))
        {
            targetFloorY = hit.point.y;
            Debug.Log($"[DragonBoss] Found real floor at y={targetFloorY:F2}.");
        }
        else
        {
            // No real floor detected under the spawn point (room scan not
            // covering that spot, or MRUK not in use at all). Fall back to
            // an estimated floor height under the PLAYER instead of a
            // hardcoded world Y -- avoids the "different spot every launch"
            // bug from the NavMesh boss work.
            targetFloorY = target.position.y - 1.6f;
            Debug.LogWarning("[DragonBoss] No floor found under spawn point -- using an estimated floor height instead.");
        }

        transform.position = new Vector3(desiredXZ.x, animateDrop ? rayStart.y : targetFloorY, desiredXZ.z);
        transform.LookAt(new Vector3(target.position.x, transform.position.y, target.position.z));

        grounded = !animateDrop;
        placed = true;
    }

    void Update()
    {
        if (!placed || target == null) return;

        if (!grounded)
        {
            Vector3 pos = transform.position;
            pos.y = Mathf.MoveTowards(pos.y, targetFloorY, dropSpeed * Time.deltaTime);
            transform.position = pos;
            if (Mathf.Approximately(pos.y, targetFloorY)) grounded = true;
            return; // don't attack mid-drop
        }

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
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, toTarget.magnitude, obstacleMask))
            return false;
        return true;
    }

    void Shoot()
    {
        if (projectilePrefab == null) return;
        GameObject proj = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
        BossProjectile bp = proj.GetComponent<BossProjectile>();
        if (bp != null) bp.Launch(target.position, obstacleMask, target);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}