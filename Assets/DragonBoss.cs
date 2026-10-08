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
//   6. Optional: give Water / Fire / Lightning Attack their own projectile
//      prefab. The attack follows EnemyTarget's current element; one left
//      empty falls back to Projectile Prefab.
//
// Before each attack, particles in the attack's color are drawn into the
// muzzle for Charge Time seconds, so the player can see the shot coming.
[RequireComponent(typeof(Collider))]
public class DragonBoss : MonoBehaviour
{
    [System.Serializable]
    public class ElementAttack
    {
        [Tooltip("What this attack fires -- anything with BossProjectile on it. Empty = Projectile Prefab.")]
        public GameObject projectilePrefab;
        [Tooltip("Shots per attack. Each one is aimed at where the player is when it leaves the mouth.")]
        public int shots = 1;
        [Tooltip("Seconds between shots when Shots is more than 1.")]
        public float shotInterval = 0.2f;
        [Tooltip("How long particles gather at the mouth before the first shot, in seconds.")]
        public float chargeTime = 0.6f;
        public Color chargeColor = Color.white;
    }

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

    [Header("Attack per element")]
    public ElementAttack waterAttack = new ElementAttack { chargeColor = new Color(0.2f, 0.5f, 1f) };
    public ElementAttack fireAttack = new ElementAttack { chargeColor = new Color(1f, 0.5f, 0.1f) };
    public ElementAttack lightningAttack = new ElementAttack { shots = 3, chargeColor = new Color(0.55f, 0.95f, 1f) };

    [Header("Charge-up effect")]
    [Tooltip("How far from the muzzle the gathering particles start, in meters.")]
    public float chargeRadius = 0.25f;
    public float chargeParticlesPerSecond = 80f;

    const float ChargeParticleLife = 0.35f; // seconds a particle takes to reach the muzzle

    private float attackTimer;
    private bool attacking;
    private ParticleSystem charge;
    private Material chargeMaterial;
    private float targetFloorY;
    private bool grounded;
    private bool placed;

    [SerializeField]
    private Animator animator;

    private EnemyTarget health;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;
        health = GetComponent<EnemyTarget>();
    }

    void OnEnable()
    {
        // Also runs on revive: give the player a moment before the first shot.
        attackTimer = attackCooldown;
        attacking = false; // an attack cut short by deactivation never cleared it
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
        StopCharge();
    }

    void OnDestroy()
    {
        if (charge != null) Destroy(charge.gameObject);
        if (chargeMaterial != null) Destroy(chargeMaterial);
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
        if (health != null && health.IsDead) return;

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

        if (!attacking && distance <= attackRange && attackTimer <= 0f && HasLineOfSight())
        {
            // Cooldown runs from the start of the charge, so attacks keep the
            // same rhythm no matter how long each one takes.
            attackTimer = attackCooldown;
            StartCoroutine(AttackRoutine());
        }
    }

    IEnumerator AttackRoutine()
    {
        attacking = true;

        // Picked once: a phase change mid-charge doesn't swap the shot.
        ElementAttack attack = CurrentAttack();
        AnimAttack();
        StartCharge(attack);

        bool emitting = true;
        for (float t = 0f; t < attack.chargeTime; t += Time.deltaTime)
        {
            if (IsDead()) break;

            if (charge != null)
            {
                charge.transform.position = muzzle.position;
                // Stop early enough that the last particles arrive as it fires.
                if (emitting && t >= attack.chargeTime - ChargeParticleLife)
                {
                    charge.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    emitting = false;
                }
            }
            yield return null;
        }
        StopCharge();

        int shots = Mathf.Max(1, attack.shots);
        for (int i = 0; i < shots && !IsDead(); i++)
        {
            Shoot(attack);
            if (i < shots - 1)
                yield return new WaitForSeconds(attack.shotInterval);
        }

        attacking = false;
    }

    bool IsDead() => health != null && health.IsDead;

    ElementAttack CurrentAttack()
    {
        // No EnemyTarget = no element; Fire is the plain single shot.
        if (health == null) return fireAttack;
        switch (health.CurrentElement)
        {
            case Element.Water:     return waterAttack;
            case Element.Lightning: return lightningAttack;
            default:                return fireAttack;
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

    void Shoot(ElementAttack attack)
    {
        GameObject prefab = attack.projectilePrefab != null ? attack.projectilePrefab : projectilePrefab;
        if (prefab == null || target == null) return;

        // Facing its flight direction, for projectiles that aren't round.
        Vector3 toTarget = target.position - muzzle.position;
        Quaternion facing = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : Quaternion.identity;

        GameObject proj = Instantiate(prefab, muzzle.position, facing);
        BossProjectile bp = proj.GetComponent<BossProjectile>();
        if (bp != null) bp.Launch(target.position, obstacleMask, target);
    }

    void AnimAttack()
    {
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }
    }

    void StartCharge(ElementAttack attack)
    {
        if (attack.chargeTime <= 0f) return;
        if (charge == null && !BuildCharge()) return;

        // Never longer than the charge itself, or nothing would reach the muzzle.
        float life = Mathf.Min(ChargeParticleLife, attack.chargeTime);

        charge.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        charge.transform.position = muzzle.position;

        ParticleSystem.MainModule main = charge.main;
        main.startColor = attack.chargeColor;
        main.startLifetime = life;
        // Negative speed: particles start on the sphere and fly inward.
        main.startSpeed = -chargeRadius / life;

        ParticleSystem.EmissionModule emission = charge.emission;
        emission.rateOverTime = chargeParticlesPerSecond;

        ParticleSystem.ShapeModule shape = charge.shape;
        shape.radius = chargeRadius;

        charge.Play();
    }

    void StopCharge()
    {
        if (charge != null)
            charge.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    // Built from code (Sprites/Default, like DeflectRipple) and kept out of the
    // dragon's hierarchy: EnemyTarget flashes, hides and measures every child
    // renderer, and this one shouldn't be part of that.
    bool BuildCharge()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning("[DragonBoss] Sprites/Default shader not found -- no charge-up effect.");
            return false;
        }

        var go = new GameObject(name + " Charge");
        charge = go.AddComponent<ParticleSystem>();
        charge.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = charge.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
        // Local, so the particles stay around the muzzle if the dragon is moved.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystem.ShapeModule shape = charge.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radiusThickness = 0f; // surface only

        ParticleSystem.ColorOverLifetimeModule fade = charge.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 1f) });
        fade.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        chargeMaterial = new Material(shader);
        renderer.sharedMaterial = chargeMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 1f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}