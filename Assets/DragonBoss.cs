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
//   2. Add this script. The hit colliders go on the dragon's bones, so they
//      follow the animation (see Tools > Dragon in the editor).
//   3. Set Target to your CenterEyeAnchor.
//   4. Set Floor Mask and Obstacle Mask to whatever layer your
//      RealWorldMesh colliders are on (Default, most likely).
//   5. Assign Projectile Prefab to your existing boss fireball prefab
//      (anything with BossProjectile on it).
//   6. Optional: give Water / Fire / Lightning Attack their own projectile
//      prefab. The attack follows EnemyTarget's current element; one left
//      empty falls back to Projectile Prefab.
//
// Each attack is a random Attack Type (Normal / Charge / Super, picked by
// weight), fired with the current element's projectile. Before an attack,
// particles in the element's color are drawn into the muzzle, so the player
// can see the shot coming.
//
// The animation is the clock: every Attack Type names the moment in its
// animation when the mouth spits (Fire In State + Fire Time). The shot leaves
// on that moment and the particles are timed backward from it, so they finish
// gathering just as it leaves.
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

    // How an attack plays out, on top of the element's projectile and colors.
    [System.Serializable]
    public class AttackType
    {
        public string name = "Normal";
        [Tooltip("Relative chance of being picked. The weights don't have to add up to 1.")]
        public float weight = 1f;
        [Tooltip("Multiplies the element's Charge Time. 0 = no charge-up at all.")]
        public float chargeTimeScale = 1f;
        [Tooltip("Multiplies the charge-up particles' spread, size and amount.")]
        public float chargeEffectScale = 1f;
        [Tooltip("Shots per attack. 0 = whatever the element says.")]
        public int shots = 0;
        [Tooltip("Multiplies the projectile's size and the distance at which it hits the player.")]
        public float projectileScale = 1f;
        public float damageScale = 1f;
        [Tooltip("Rest after the attack ends, before the next one can start, in seconds.")]
        public float cooldownAfter = 2f;
        [Tooltip("Animator trigger fired when the attack starts.")]
        public string animatorTrigger = "Attack";
        [Tooltip("Hold the rest after the attack until the animation has played all the way back to Idle. For attacks that chain several animations, like the flying Super.")]
        public bool waitForAnimation = false;
        [Tooltip("Animator state the shot leaves in (its exact name in the Animator window). Empty = Fire Time counts from the trigger instead.")]
        public string fireInState = "";
        [Tooltip("Seconds into that state's animation when the mouth spits and the shot leaves. The charge-up particles finish gathering at this moment.")]
        public float fireTime = 0f;
        [Tooltip("For attacks that chain animations: the state that plays in full right before Fire In State. Lets a charge-up longer than Fire Time begin during it. Empty = the trigger leads straight into Fire In State.")]
        public string leadInState = "";
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
    [Tooltip("Seconds after landing (or being revived) before the first attack, so the player can get their bearings.")]
    public float openingDelay = 2f;
    [Tooltip("Where projectiles spawn from. If left empty, uses this transform.")]
    public Transform muzzle;
    [Tooltip("Layers that can block line of sight -- your RealWorldMesh colliders.")]
    public LayerMask obstacleMask;
    [Tooltip("If true, the dragon only attacks while nothing on Obstacle Mask is between its muzzle and the player. Off = it keeps shooting at a player behind cover, and the shots stop at the cover.")]
    public bool requireLineOfSight = false;

    [Header("Attack types")]
    public AttackType[] attackTypes =
    {
        // Fire Times are where the head's lunge peaks in each clip of "dragon boss.fbx".
        new AttackType { name = "Normal", weight = 0.6f, cooldownAfter = 2f,
                         fireInState = "attack", fireTime = 0.2f },
        new AttackType { name = "Charge", weight = 0.25f, chargeTimeScale = 3f, chargeEffectScale = 2f,
                         shots = 1, projectileScale = 2f, damageScale = 2f, cooldownAfter = 2f,
                         animatorTrigger = "ChargeAttack",
                         fireInState = "metarig|Charge Attack", fireTime = 1.2f },
        new AttackType { name = "Super", weight = 0.15f, chargeTimeScale = 7f, chargeEffectScale = 3f,
                         shots = 1, projectileScale = 3f, damageScale = 3f, cooldownAfter = 1f,
                         animatorTrigger = "SuperAttack", waitForAnimation = true,
                         fireInState = "metarig|Fly Superattack", fireTime = 3f,
                         leadInState = "metarig|Fly Idle" },
    };

    [Header("Attack per element")]
    public ElementAttack waterAttack = new ElementAttack { chargeColor = new Color(0.2f, 0.5f, 1f) };
    public ElementAttack fireAttack = new ElementAttack { chargeColor = new Color(1f, 0.5f, 0.1f) };
    public ElementAttack lightningAttack = new ElementAttack { shots = 3, chargeColor = new Color(0.55f, 0.95f, 1f) };

    [Header("Charge-up effect")]
    [Tooltip("How far from the muzzle the gathering particles start, in meters.")]
    public float chargeRadius = 0.25f;
    public float chargeParticlesPerSecond = 80f;

    const float ChargeParticleLife = 0.35f; // seconds a particle takes to reach the muzzle
    const string IdleState = "idle";        // the Animator's resting state
    const float AnimationTimeout = 30f;     // longest wait on an animation before giving up
    const float ParticleSizeMin = 0.012f;
    const float ParticleSizeMax = 0.028f;

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
        attackTimer = openingDelay;
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

    // Stops attacking right now: cancels an attack in progress (the charge-up and
    // any shots still to come) and keeps a new one from starting. GameFlow uses
    // it while the end menu is up.
    public void StopAttacking()
    {
        StopAllCoroutines();
        attacking = false;
        StopCharge();
        enabled = false;
    }

    // Back to a fresh fight: drops in front of the player again and attacks
    // after the usual first-shot delay. Run EnemyTarget.Revive() first, so the
    // dragon is active and alive.
    public void ResetForRestart()
    {
        enabled = true; // OnEnable resets the attack state and the attack timer
        PlaceInFrontOfPlayer();
        if (animator != null && animator.gameObject.activeInHierarchy)
            animator.Rebind(); // back to Idle, in case it was mid-attack
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

        // The rest after an attack counts down only once the attack is over.
        if (attacking) return;

        attackTimer -= Time.deltaTime;
        float distance = Vector3.Distance(transform.position, target.position);

        if (distance <= attackRange && attackTimer <= 0f && (!requireLineOfSight || HasLineOfSight()))
            StartCoroutine(AttackRoutine());
    }

    IEnumerator AttackRoutine()
    {
        attacking = true;

        // Picked once: a phase change mid-charge doesn't swap the shot.
        AttackType type = PickAttackType();
        ElementAttack attack = CurrentAttack();
        float chargeTime = attack.chargeTime * type.chargeTimeScale;
        Debug.Log($"[DragonBoss] {type.name} attack");

        // A charge-up longer than the animation's wind-up starts first, with the
        // dragon still in Idle, so both end on the spit. A chained attack fits it
        // into its lead-in state instead.
        bool chained = !string.IsNullOrEmpty(type.leadInState);
        float hold = chained ? 0f : Mathf.Max(0f, chargeTime - type.fireTime);

        bool triggered = false;
        bool charging = false;
        bool emitting = false;
        for (float t = 0f; t < AnimationTimeout && !IsDead(); t += Time.deltaTime)
        {
            if (!triggered && t >= hold)
            {
                AnimAttack(type);
                triggered = true;
            }

            float untilFire = triggered ? SecondsUntilFire(type, t - hold) : hold - t + type.fireTime;
            if (untilFire <= 0f) break;

            if (!charging && chargeTime > 0f && untilFire <= chargeTime)
            {
                StartCharge(attack, type, untilFire);
                charging = true;
                emitting = charge != null;
            }

            if (charge != null)
            {
                charge.transform.position = muzzle.position;
                // Stop early enough that the last particles arrive as it fires.
                if (emitting && untilFire <= ChargeParticleLife)
                {
                    charge.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    emitting = false;
                }
            }
            yield return null;
        }
        StopCharge();

        int shots = type.shots > 0 ? type.shots : Mathf.Max(1, attack.shots);
        for (int i = 0; i < shots && !IsDead(); i++)
        {
            Shoot(attack, type);
            if (i < shots - 1)
                yield return new WaitForSeconds(attack.shotInterval);
        }

        if (type.waitForAnimation)
            yield return StartCoroutine(WaitForAnimationToFinish());

        attackTimer = type.cooldownAfter;
        attacking = false;
    }

    // Seconds until the shot should leave, read from the animator so it stays
    // on the spit frame however long the blends and earlier states took.
    // Infinity while a chained attack hasn't reached its lead-in state yet.
    // Without an animator or a state name it's a plain timer from the trigger.
    float SecondsUntilFire(AttackType type, float sinceTrigger)
    {
        if (animator == null || string.IsNullOrEmpty(type.fireInState))
            return type.fireTime - sinceTrigger;

        // A state being blended into is the "next" one; its clip is already running.
        AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
        bool blending = animator.IsInTransition(0);

        if (blending && next.IsName(type.fireInState)) return type.fireTime - ClipTime(next);
        if (current.IsName(type.fireInState)) return type.fireTime - ClipTime(current);

        if (!string.IsNullOrEmpty(type.leadInState))
        {
            if (blending && next.IsName(type.leadInState)) return ClipTimeLeft(next) + type.fireTime;
            if (current.IsName(type.leadInState)) return ClipTimeLeft(current) + type.fireTime;
            return float.PositiveInfinity;
        }

        // The trigger takes a frame to start the state (and a misspelled state
        // name never does): count from the trigger until then.
        return type.fireTime - sinceTrigger;
    }

    static float ClipTime(AnimatorStateInfo state) => state.normalizedTime * state.length;

    static float ClipTimeLeft(AnimatorStateInfo state) => Mathf.Max(0f, 1f - state.normalizedTime) * state.length;

    // Waits for the animator to leave Idle (a trigger takes a frame or two to
    // start something), then to come all the way back to it.
    IEnumerator WaitForAnimationToFinish()
    {
        if (animator == null) yield break;

        for (float t = 0f; t < 0.5f && !AnimatorBusy(); t += Time.deltaTime)
            yield return null;
        for (float t = 0f; t < AnimationTimeout && AnimatorBusy() && !IsDead(); t += Time.deltaTime)
            yield return null;
    }

    bool AnimatorBusy() => animator.IsInTransition(0) || !animator.GetCurrentAnimatorStateInfo(0).IsName(IdleState);

    bool IsDead() => health != null && health.IsDead;

    // Weighted random: a type with twice the weight comes up twice as often.
    AttackType PickAttackType()
    {
        float total = 0f;
        foreach (AttackType type in attackTypes)
            total += Mathf.Max(0f, type.weight);

        float roll = Random.value * total;
        foreach (AttackType type in attackTypes)
        {
            roll -= Mathf.Max(0f, type.weight);
            if (roll < 0f) return type;
        }
        return attackTypes[0]; // only if the roll landed exactly on the total
    }

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

    void Shoot(ElementAttack attack, AttackType type)
    {
        GameObject prefab = attack.projectilePrefab != null ? attack.projectilePrefab : projectilePrefab;
        if (prefab == null || target == null) return;

        // Facing its flight direction, for projectiles that aren't round.
        Vector3 toTarget = target.position - muzzle.position;
        Quaternion facing = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : Quaternion.identity;

        GameObject proj = Instantiate(prefab, muzzle.position, facing);
        proj.transform.localScale *= type.projectileScale;

        BossProjectile bp = proj.GetComponent<BossProjectile>();
        if (bp != null)
        {
            bp.damageAmount = Mathf.RoundToInt(bp.damageAmount * type.damageScale);
            bp.hitRadius *= type.projectileScale; // a bigger shot connects from further away
            bp.Launch(target.position, obstacleMask, target);
        }
    }

    void AnimAttack(AttackType type)
    {
        if (animator != null)
            animator.SetTrigger(type.animatorTrigger);
    }

    void StartCharge(ElementAttack attack, AttackType type, float chargeTime)
    {
        if (chargeTime <= 0f) return;
        if (charge == null && !BuildCharge()) return;

        // Never longer than the charge itself, or nothing would reach the muzzle.
        float life = Mathf.Min(ChargeParticleLife, chargeTime);
        float scale = type.chargeEffectScale;
        float radius = chargeRadius * scale;

        charge.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        charge.transform.position = muzzle.position;

        ParticleSystem.MainModule main = charge.main;
        main.startColor = attack.chargeColor;
        main.startLifetime = life;
        main.startSize = new ParticleSystem.MinMaxCurve(ParticleSizeMin * scale, ParticleSizeMax * scale);
        // Negative speed: particles start on the sphere and fly inward.
        main.startSpeed = -radius / life;

        ParticleSystem.EmissionModule emission = charge.emission;
        emission.rateOverTime = chargeParticlesPerSecond * scale;

        ParticleSystem.ShapeModule shape = charge.shape;
        shape.radius = radius;

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
        main.startSize = new ParticleSystem.MinMaxCurve(ParticleSizeMin, ParticleSizeMax);
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