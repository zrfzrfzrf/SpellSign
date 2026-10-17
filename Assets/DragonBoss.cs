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
// An Attack Type can play out in one of two ways:
//
//   Two-phase (Charge State + Attack State both filled in): the dragon plays
//   a charge animation, and the charge-up particles run for exactly as long as
//   it plays. Then it plays a separate attack animation. A projectile attack
//   fires when that animation starts (or Attack Fire Time seconds into it);
//   a breath attack lasts for as long as that animation does.
//
//   Single animation (the old way, Normal uses it): one trigger starts one
//   animation, and every Attack Type names the moment in it when the mouth
//   spits (Fire In State + Fire Time). The shot leaves on that moment and the
//   particles are timed backward from it, so they finish gathering just as it
//   leaves.
//
// The Breath attack is the one that can't be dodged: the dragon flies up and
// breathes its element down over the player and the floor around them. Cover
// doesn't help and neither does moving; only the sigil shield (SigilShield)
// stops it. It does its damage in ticks for as long as the breath lasts.
//
// A hit that takes the dragon into its next phase (see EnemyTarget) cuts short
// whatever it was doing. It staggers back, still open to the spells that were
// working; then it transforms: particles of the new element swirl into it, it
// whitens, and on the flash the new colors take over and a ring bursts across
// the floor. Spells bounce off for the whole transformation, and it attacks
// again a moment after.
public class DragonBoss : MonoBehaviour
{
	[System.Serializable]
	public class ElementAttack
	{
		[Tooltip("What this attack fires -- anything with BossProjectile on it. Empty = Projectile Prefab.")]
		public GameObject projectilePrefab;
		[Tooltip("Shots per attack. All of them are aimed at where the player is when the first one leaves the mouth, so stepping aside dodges the whole burst.")]
		public int shots = 1;
		[Tooltip("Seconds between shots when Shots is more than 1.")]
		public float shotInterval = 0.2f;
		[Tooltip("How long particles gather at the mouth before the first shot, in seconds. (Not used by two-phase attacks: there the charge animation's length decides.)")]
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
		[Tooltip("Multiplies the element's Charge Time. 0 = no charge-up at all. Ignored by two-phase attacks.")]
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
		[Tooltip("Animator trigger fired when the attack starts. Ignored by two-phase attacks.")]
		public string animatorTrigger = "Attack";
		[Tooltip("Hold the rest after the attack until the animation has played all the way back to Idle. For attacks that chain several animations, like the flying Super. Ignored by two-phase attacks, which always wait.")]
		public bool waitForAnimation = false;
		[Tooltip("Animator state the shot leaves in (its exact name in the Animator window). Empty = Fire Time counts from the trigger instead. Ignored by two-phase attacks.")]
		public string fireInState = "";
		[Tooltip("Seconds into that state's animation when the mouth spits and the shot leaves. The charge-up particles finish gathering at this moment. Ignored by two-phase attacks.")]
		public float fireTime = 0f;
		[Tooltip("For attacks that chain animations: the state that plays in full right before Fire In State. Lets a charge-up longer than Fire Time begin during it. Empty = the trigger leads straight into Fire In State. Ignored by two-phase attacks.")]
		public string leadInState = "";

		[Header("Separate charge and attack animations")]
		[Tooltip("Animator state for the charge animation (exact name in the Animator window). The charge-up particles run for as long as it plays. It should not loop. Leave empty (or leave Attack State empty) for the old single-animation behaviour.")]
		public string chargeState = "";
		[Tooltip("Animator state for the attack itself. A breath lasts as long as it plays; projectile attacks fire when it starts. It needs a transition back to Idle with Has Exit Time ticked.")]
		public string attackState = "";
		[Tooltip("Projectile attacks only: seconds into the attack animation before the shot leaves. 0 = the moment it starts. Set it to when the mouth opens if the animation has a wind-up.")]
		public float attackFireTime = 0f;

		[Header("Breath (instead of projectiles)")]
		[Tooltip("Above 0 = this attack is a breath instead of projectiles. With an Attack State, the animation's length decides how long it lasts and this number only marks it as a breath. Without one, it is the seconds the breath lasts; keep it a little shorter than the sigil shield lasts, so a shield raised just before the breath covers all of it.")]
		public float breathDuration = 0f;
		[Tooltip("Total damage over the whole breath to a player with no sigil shield up, dealt in ticks.")]
		public int breathDamage = 35;
		[Tooltip("Single-animation breath only: animator speed while breathing. Below 1 stretches the pose the dragon is in at Fire Time over the breath; 1 = the animation carries on as normal. Ignored by two-phase attacks.")]
		public float breathAnimatorSpeed = 1f;
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
        // Normal still uses the single-animation path. Fire Time is where the head's
        // lunge peaks in the clip of "dragon boss.fbx".
        new AttackType { name = "Normal", weight = 0.6f, cooldownAfter = 2f,
						 fireInState = "attack", fireTime = 0.2f },
        // Charge and Breath are two-phase. The state names are placeholders: use the
        // exact names of your own states in the Animator window.
        new AttackType { name = "Charge", weight = 0.25f, chargeEffectScale = 2f,
						 shots = 1, projectileScale = 2f, damageScale = 2f, cooldownAfter = 2f,
						 chargeState = "metarig|Prep Charge", attackState = "metarig|Fire Charge",
						 attackFireTime = 0f },
		new AttackType { name = "Breath", weight = 1f, chargeEffectScale = 3f, cooldownAfter = 1f,
						 chargeState = "metarig|Prep Superattack", attackState = "metarig|Fire Superattack",
						 breathDuration = 1f, breathDamage = 35 },
	};

	[Header("Attack per element")]
	public ElementAttack waterAttack = new ElementAttack { chargeColor = new Color(0.2f, 0.5f, 1f) };
	public ElementAttack fireAttack = new ElementAttack { chargeColor = new Color(1f, 0.5f, 0.1f) };
	public ElementAttack lightningAttack = new ElementAttack { shots = 3, chargeColor = new Color(1f, 0.9f, 0.2f) };

	[Header("Charge-up effect")]
	[Tooltip("How far from the muzzle the gathering particles start, in meters.")]
	public float chargeRadius = 0.25f;
	public float chargeParticlesPerSecond = 80f;

	[Header("Breath effect")]
	[Tooltip("How often the breath hurts a player with no shield up.")]
	public float breathTicksPerSecond = 2f;
	[Tooltip("How wide the cone of breath opens from the mouth, in degrees from its center line.")]
	public float breathConeAngle = 14f;
	public float breathParticlesPerSecond = 200f;
	[Tooltip("How far around the player the breath washes over the floor, in meters.")]
	public float floorFireRadius = 2.5f;
	public float floorFireParticlesPerSecond = 120f;

	[Header("Phase change")]
	[Tooltip("Seconds the dragon reels after the hit that takes it into its next phase. It can still be hurt, by the same spells as before.")]
	public float staggerTime = 1f;
	[Tooltip("How far it is knocked back at the start of the stagger, in meters.")]
	public float staggerRecoil = 0.35f;
	[Tooltip("Seconds the transformation into the new element takes. Spells bounce off for all of it.")]
	public float transformTime = 4f;
	[Tooltip("Seconds after the transformation before the next attack can start.")]
	public float restAfterPhaseChange = 2f;

	const float StaggerKickShare = 0.15f;   // part of the stagger spent being knocked back; the rest recovers
	const float StaggerRearAngle = 12f;     // degrees the head is thrown up and back
	const float StaggerShakeAngle = 6f;     // degrees of side-to-side shake at its strongest
	const float StaggerShakeRate = 7f;      // shakes per second
	const float GatherShare = 0.5f;         // part of the transformation spent gathering particles and whitening
	const float SweepShare = 0.3f;          // part spent turning from white to the new colors; the rest it just stands
	const float GatherParticleLife = 0.45f; // seconds a particle takes to swirl into the body
	const float GatherRadiusScale = 0.7f;   // how far out the particles start, relative to the dragon's size
	const float GatherSwirl = 5f;           // radians per second the particles circle while closing in
	const float GatherParticlesPerSecond = 400f;
	const int RingParticles = 160;
	const float RingSpeed = 5f;             // meters per second the ring spreads over the floor
	const float RingParticleLife = 0.6f;

	const float ChargeParticleLife = 0.35f; // seconds a particle takes to reach the muzzle
	const float BreathParticleLife = 0.6f;  // seconds a particle takes to get from the mouth to the player
	const float BreathOvershoot = 1f;       // meters the breath carries on past the player's head
	const float ShieldClearance = 0.2f;     // meters short of the shield's edge the breath stops, so it doesn't poke inside
	const string IdleState = "idle";        // the Animator's resting state
	const float AnimationTimeout = 30f;     // longest wait on an animation before giving up
	const float StateBlend = 0.1f;          // seconds the animator blends between charge and attack states
	const float ParticleSizeMin = 0.012f;
	const float ParticleSizeMax = 0.028f;

	// While true the dragon never starts an attack: it lands, stands there and
	// can be hit (phase changes included). For the sandbox.
	public bool Passive { get; set; }

	private float attackTimer;
	private bool attacking;
	private Coroutine attackRoutine;
	private Coroutine phaseRoutine;
	private bool staggering;
	private Vector3 staggerOffset;
	private Quaternion staggerBaseRotation;
	private ParticleSystem gather;
	private ParticleSystem ring;
	private BoxCollider[] hitboxes;
	private ParticleSystem charge;
	private Material chargeMaterial;
	private ParticleSystem breath;
	private ParticleSystem floorFire;
	private Material breathMaterial;
	private Texture2D breathNoise;
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
		hitboxes = GetComponentsInChildren<BoxCollider>();
	}

	void OnEnable()
	{
		// Also runs on revive: give the player a moment before the first shot.
		attackTimer = openingDelay;
		attacking = false; // an attack cut short by deactivation never cleared it
		if (health != null) health.PhaseChangeStarted += OnPhaseChangeStarted;
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
		if (health != null) health.PhaseChangeStarted -= OnPhaseChangeStarted;
		StopCharge();
		StopBreath();
		AbortPhaseChange();
	}

	// Stops attacking right now: cancels an attack in progress (the charge-up,
	// the breath and any shots still to come) and keeps a new one from starting. GameFlow uses
	// it while the end menu is up.
	public void StopAttacking()
	{
		StopAllCoroutines();
		attackRoutine = null;
		attacking = false;
		StopCharge();
		StopBreath();
		AbortPhaseChange();
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
		if (gather != null) Destroy(gather.gameObject);
		if (ring != null) Destroy(ring.gameObject);
		if (chargeMaterial != null) Destroy(chargeMaterial);
		if (breath != null) Destroy(breath.gameObject);
		if (floorFire != null) Destroy(floorFire.gameObject);
		if (breathMaterial != null) Destroy(breathMaterial);
		if (breathNoise != null) Destroy(breathNoise);
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
		if (attacking || Passive) return;

		attackTimer -= Time.deltaTime;
		float distance = Vector3.Distance(transform.position, target.position);

		if (distance <= attackRange && attackTimer <= 0f && (!requireLineOfSight || HasLineOfSight()))
			attackRoutine = StartCoroutine(AttackRoutine());
	}

	IEnumerator AttackRoutine()
	{
		attacking = true;

		// The type and its timing are picked once. The element isn't: an attack
		// can outlast a phase, and its colors and shot then follow the dragon's.
		AttackType type = PickAttackType();
		ElementAttack attack = CurrentAttack();
		Debug.Log($"[DragonBoss] {type.name} attack");

		// Separate charge and attack animations: handled on its own.
		if (IsTwoPhase(type))
		{
			// Run inside this coroutine, so cancelling the attack ends it too.
			yield return TwoPhaseAttack(type);
			attackTimer = type.cooldownAfter;
			attacking = false;
			attackRoutine = null;
			yield break;
		}

		float chargeTime = attack.chargeTime * type.chargeTimeScale;

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

			if (CurrentAttack() != attack)
			{
				attack = CurrentAttack();
				if (charging && charge != null)
				{
					ParticleSystem.MainModule chargeMain = charge.main;
					chargeMain.startColor = attack.chargeColor;
				}
			}

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

		if (type.breathDuration > 0f)
		{
			yield return BreathRoutine(attack, type); // run inside this coroutine, so cancelling the attack ends it too
		}
		else
		{
			int shots = type.shots > 0 ? type.shots : Mathf.Max(1, attack.shots);
			// Aimed once for the whole burst: later shots don't follow a player who has moved.
			Vector3 aimPoint = target.position;
			for (int i = 0; i < shots && !IsDead(); i++)
			{
				Shoot(CurrentAttack(), type, aimPoint);
				if (i < shots - 1)
					yield return new WaitForSeconds(attack.shotInterval);
			}
		}

		if (type.waitForAnimation)
			yield return WaitForAnimationToFinish();

		attackTimer = type.cooldownAfter;
		attacking = false;
		attackRoutine = null;
	}

	// An attack with both a charge state and an attack state plays them as two
	// separate animations. Without an Animator it can't, so it falls back.
	bool IsTwoPhase(AttackType type) =>
		animator != null && !string.IsNullOrEmpty(type.chargeState) && !string.IsNullOrEmpty(type.attackState);

	// Charge animation (particles for as long as it plays), then the attack
	// animation (projectiles when it starts, or breath for as long as it lasts).
	IEnumerator TwoPhaseAttack(AttackType type)
	{
		ElementAttack attack = CurrentAttack();

		// ---- Charge: particles run exactly as long as the charge state ----
		PlayState(type.chargeState);

		if (type.name == "Breath")
		{
			AttackWarningUI.ShowWarning();
		}

		bool entered = false;
		bool emitting = false;
		for (float t = 0f; t < AnimationTimeout && !IsDead(); t += Time.deltaTime)
		{
			if (InState(type.chargeState, out AnimatorStateInfo state))
			{
				if (!entered)
				{
					entered = true;
					StartCharge(attack, type, ClipTimeLeft(state));
					emitting = charge != null;
				}

				if (CurrentAttack() != attack)
				{
					attack = CurrentAttack();
					if (charge != null)
					{
						ParticleSystem.MainModule chargeMain = charge.main;
						chargeMain.startColor = attack.chargeColor;
					}
				}

				if (charge != null)
				{
					charge.transform.position = muzzle.position;
					// Stop early enough that the last particles arrive as the animation ends.
					if (emitting && ClipTimeLeft(state) <= ChargeParticleLife)
					{
						charge.Stop(true, ParticleSystemStopBehavior.StopEmitting);
						emitting = false;
					}
				}
			}
			else if (entered || t > 0.5f)
			{
				break; // it has finished, or never started
			}
			yield return null;
		}
		StopCharge();
		Debug.LogWarning("TEST: " + type.name);
		if (!entered) Debug.LogWarning($"[DragonBoss] Never entered charge state '{type.chargeState}' -- check the name.");
		if (IsDead()) yield break;

		// ---- Attack ----
		if (type.breathDuration > 0f)
			yield return BreathDuringAnimation(CurrentAttack(), type);
		else
			yield return FireOnAttackAnimation(type);

		yield return WaitForAnimationToFinish();
	}

	// Plays the attack animation and fires its projectiles when it starts, or
	// Attack Fire Time seconds into it.
	IEnumerator FireOnAttackAnimation(AttackType type)
	{
		PlayState(type.attackState);

		// Wait for the animation to start, then for the shot's moment in it.
		bool entered = false;
		for (float t = 0f; t < AnimationTimeout && !IsDead(); t += Time.deltaTime)
		{
			if (InState(type.attackState, out AnimatorStateInfo state))
			{
				entered = true;
				if (ClipTime(state) >= type.attackFireTime) break;
			}
			else if (entered || t > 0.5f)
			{
				break;
			}
			yield return null;
		}
		if (!entered) Debug.LogWarning($"[DragonBoss] Never entered attack state '{type.attackState}' -- check the name.");

		int shots = type.shots > 0 ? type.shots : Mathf.Max(1, CurrentAttack().shots);
		Vector3 aimPoint = target.position; // aimed once for the whole burst
		for (int i = 0; i < shots && !IsDead(); i++)
		{
			Shoot(CurrentAttack(), type, aimPoint);
			if (i < shots - 1)
				yield return new WaitForSeconds(CurrentAttack().shotInterval);
		}
	}

	// The breath lasts exactly as long as the attack state plays. Breath Damage is
	// still the total, split into ticks over the animation's length.
	IEnumerator BreathDuringAnimation(ElementAttack attack, AttackType type)
	{
		PlayerHealth player = target.GetComponentInParent<PlayerHealth>();
		if (player == null)
			Debug.LogWarning("[DragonBoss] No PlayerHealth found on/above the target -- the breath does no damage.");

		PlayState(type.attackState);

		bool breathing = false;
		int ticks = 1, ticksDone = 0;
		float tickInterval = 1f;
		for (float t = 0f; t < AnimationTimeout && !IsDead(); t += Time.deltaTime)
		{
			if (InState(type.attackState, out AnimatorStateInfo state))
			{
				if (!breathing)
				{
					breathing = true;
					float duration = Mathf.Max(0.1f, state.length);
					ticks = Mathf.Max(1, Mathf.RoundToInt(duration * breathTicksPerSecond));
					tickInterval = duration / ticks;
					StartBreath(attack);
				}

				if (CurrentAttack() != attack)
				{
					attack = CurrentAttack();
					SetBreathColor(attack.chargeColor);
				}
				AimBreath();

				float played = ClipTime(state);
				while (ticksDone < ticks && played >= (ticksDone + 0.5f) * tickInterval)
				{
					ticksDone++;
					// Split so the ticks add up to exactly Breath Damage.
					int damage = type.breathDamage * ticksDone / ticks - type.breathDamage * (ticksDone - 1) / ticks;
					if (player != null && !SpellSign.Sigils.SigilShield.IsUp)
						player.TakeDamage(damage);
				}
			}
			else if (breathing || t > 0.5f)
			{
				break;
			}
			yield return null;
		}
		StopBreath();
	}

	void PlayState(string stateName)
	{
		if (animator != null && !string.IsNullOrEmpty(stateName))
			animator.CrossFadeInFixedTime(stateName, StateBlend);
	}

	// True while the animator is playing (or blending into) the named state and
	// hasn't started leaving it or reached its end.
	bool InState(string stateName, out AnimatorStateInfo info)
	{
		info = animator.GetCurrentAnimatorStateInfo(0);
		if (animator.IsInTransition(0))
		{
			AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
			if (next.IsName(stateName)) { info = next; return true; }
			return false; // blending into something else
		}
		return info.IsName(stateName) && info.normalizedTime < 1f;
	}

	void OnPhaseChangeStarted(Element next)
	{
		if (phaseRoutine != null) StopCoroutine(phaseRoutine);
		phaseRoutine = StartCoroutine(PhaseChangeRoutine());
	}

	// The stagger, then the transformation. No attack starts until it is over.
	IEnumerator PhaseChangeRoutine()
	{
		// Whatever it was doing ends here: no shot, no breath, back on its feet.
		if (attackRoutine != null) StopCoroutine(attackRoutine);
		attackRoutine = null;
		attacking = true;
		StopCharge();
		StopBreath();
		if (animator != null)
		{
			foreach (AttackType type in attackTypes)
				animator.ResetTrigger(type.animatorTrigger);
			animator.CrossFadeInFixedTime(IdleState, 0.25f);
		}

		// Stagger: knocked back and shaking. Hits still count, against the old element.
		staggering = true;
		staggerBaseRotation = transform.rotation;
		for (float t = 0f; t < staggerTime && !IsDead(); t += Time.deltaTime)
		{
			PoseStagger(t);
			yield return null;
		}
		EndStagger();
		if (IsDead())
		{
			phaseRoutine = null;
			yield break;
		}

		// Transformation: spells bounce off from here on.
		health.Invulnerable = true;
		Color color = AttackFor(health.PhaseElement).chargeColor;
		float gatherTime = transformTime * GatherShare;
		float sweepTime = transformTime * SweepShare;

		bool emitting = StartGather(color);
		for (float t = 0f; t < gatherTime; t += Time.deltaTime)
		{
			float u = t / gatherTime;
			health.ChangeElementVisual(health.CurrentElement, u * u); // whitens slowly, then fast
			if (gather != null)
			{
				gather.transform.position = BodyCenter(out _);
				// Stop early enough that the last particles arrive on the flash.
				if (emitting && gatherTime - t <= GatherParticleLife)
				{
					gather.Stop(true, ParticleSystemStopBehavior.StopEmitting);
					emitting = false;
				}
			}
			yield return null;
		}

		// The flash: the element changes while the dragon is all white.
		health.FinishPhaseChange();
		health.ChangeElementVisual(health.CurrentElement, 1f);
		BurstRing(color);

		for (float t = 0f; t < sweepTime; t += Time.deltaTime)
		{
			health.ChangeElementVisual(health.CurrentElement, 1f - t / sweepTime);
			yield return null;
		}
		health.ChangeElementVisual(health.CurrentElement);

		float settleTime = transformTime - gatherTime - sweepTime;
		if (settleTime > 0f) yield return new WaitForSeconds(settleTime);

		health.Invulnerable = false;
		attackTimer = restAfterPhaseChange;
		attacking = false;
		phaseRoutine = null;
	}

	// One frame of the stagger, t seconds in: thrown back with its head up,
	// shaking less and less as it finds its feet again.
	void PoseStagger(float t)
	{
		float u = Mathf.Clamp01(t / staggerTime);
		float kick = u < StaggerKickShare
			? Mathf.SmoothStep(0f, 1f, u / StaggerKickShare)
			: 1f - Mathf.SmoothStep(0f, 1f, (u - StaggerKickShare) / (1f - StaggerKickShare));
		float fade = (1f - u) * (1f - u);
		float phase = t * StaggerShakeRate * 2f * Mathf.PI;

		// As a change from last frame, so the drop to the floor can go on underneath.
		Vector3 offset = staggerBaseRotation * Vector3.back * (staggerRecoil * kick);
		transform.position += offset - staggerOffset;
		staggerOffset = offset;

		transform.rotation = staggerBaseRotation * Quaternion.Euler(
			-StaggerRearAngle * kick,
			StaggerShakeAngle * fade * Mathf.Sin(phase),
			StaggerShakeAngle * fade * Mathf.Sin(phase + 1.3f));
	}

	// Back to where and how it stood before the stagger.
	void EndStagger()
	{
		if (!staggering) return;
		transform.position -= staggerOffset;
		transform.rotation = staggerBaseRotation;
		staggerOffset = Vector3.zero;
		staggering = false;
	}

	// Ends a phase change that can't play out (the fight is over, or the dragon
	// was switched off). The new element still takes over, without the show.
	void AbortPhaseChange()
	{
		if (phaseRoutine != null) StopCoroutine(phaseRoutine);
		phaseRoutine = null;
		EndStagger();
		if (gather != null) gather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

		if (health == null) return;
		health.Invulnerable = false;
		if (health.ChangingPhase && !health.IsDead) health.FinishPhaseChange();
	}

	// The middle of the dragon's hitboxes, and how far out its gathering
	// particles should start.
	Vector3 BodyCenter(out float radius)
	{
		bool any = false;
		Bounds bounds = default;
		foreach (BoxCollider box in hitboxes)
		{
			if (box == null || !box.enabled) continue;
			if (!any) { bounds = box.bounds; any = true; }
			else bounds.Encapsulate(box.bounds);
		}

		if (!any)
		{
			radius = 1.5f;
			return transform.position + Vector3.up;
		}
		radius = bounds.extents.magnitude * GatherRadiusScale;
		return bounds.center;
	}

	bool StartGather(Color color)
	{
		if (gather == null && !BuildPhaseEffects()) return false;

		Vector3 center = BodyCenter(out float radius);
		gather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		gather.transform.position = center;

		ParticleSystem.MainModule main = gather.main;
		main.startColor = color;
		ParticleSystem.ShapeModule shape = gather.shape;
		shape.radius = radius;
		// Inward at the speed that reaches the middle as the particle dies.
		ParticleSystem.VelocityOverLifetimeModule velocity = gather.velocityOverLifetime;
		velocity.radial = new ParticleSystem.MinMaxCurve(-radius / GatherParticleLife);

		gather.Play();
		return true;
	}

	void BurstRing(Color color)
	{
		if (ring == null) return;

		Vector3 center = BodyCenter(out _);
		ring.transform.position = new Vector3(center.x, targetFloorY + 0.05f, center.z);
		ParticleSystem.MainModule main = ring.main;
		main.startColor = color;
		ring.Emit(RingParticles);
	}

	// The two effects of the transformation, built from code and kept out of
	// the dragon's hierarchy like the charge-up effect: particles that swirl
	// in from all around, and a ring that bursts outward over the floor.
	bool BuildPhaseEffects()
	{
		Material material = SpriteMaterial();
		if (material == null)
		{
			Debug.LogWarning("[DragonBoss] Sprites/Default shader not found -- no transformation effect.");
			return false;
		}

		gather = NewPhaseSystem(name + " Phase Gather", material);
		ParticleSystem.MainModule main = gather.main;
		main.loop = true;
		main.startLifetime = GatherParticleLife;
		main.startSpeed = 0f;
		main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
		// Local, so the swirl stays centered on the dragon.
		main.simulationSpace = ParticleSystemSimulationSpace.Local;
		ParticleSystem.EmissionModule emission = gather.emission;
		emission.rateOverTime = GatherParticlesPerSecond;
		ParticleSystem.ShapeModule sphere = gather.shape;
		sphere.shapeType = ParticleSystemShapeType.Sphere;
		sphere.radiusThickness = 0f; // surface only
		ParticleSystem.VelocityOverLifetimeModule swirl = gather.velocityOverLifetime;
		swirl.enabled = true;
		swirl.space = ParticleSystemSimulationSpace.Local;
		// Every curve in the module has to be the same kind.
		swirl.x = new ParticleSystem.MinMaxCurve(0f);
		swirl.y = new ParticleSystem.MinMaxCurve(0f);
		swirl.z = new ParticleSystem.MinMaxCurve(0f);
		swirl.orbitalX = new ParticleSystem.MinMaxCurve(0f);
		swirl.orbitalY = new ParticleSystem.MinMaxCurve(GatherSwirl);
		swirl.orbitalZ = new ParticleSystem.MinMaxCurve(0f);
		swirl.radial = new ParticleSystem.MinMaxCurve(0f); // set per transformation
		SetFade(gather, 0f, 1f, 1f); // appear out of nothing, brightest as they arrive

		ring = NewPhaseSystem(name + " Phase Ring", material);
		// The circle shape lies in the system's XY plane: tip it flat onto the floor.
		ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		ParticleSystem.MainModule ringMain = ring.main;
		ringMain.loop = false;
		ringMain.startLifetime = RingParticleLife;
		ringMain.startSpeed = RingSpeed;
		ringMain.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
		// World, so the ring keeps spreading from where it burst.
		ringMain.simulationSpace = ParticleSystemSimulationSpace.World;
		ParticleSystem.EmissionModule ringEmission = ring.emission;
		ringEmission.rateOverTime = 0f; // only the burst from BurstRing
		ParticleSystem.ShapeModule circle = ring.shape;
		circle.shapeType = ParticleSystemShapeType.Circle;
		circle.radius = 0.3f;
		circle.radiusThickness = 0f; // edge only
		SetFade(ring, 1f, 1f, 0f);
		return true;
	}

	ParticleSystem NewPhaseSystem(string systemName, Material material)
	{
		var go = new GameObject(systemName);
		ParticleSystem system = go.AddComponent<ParticleSystem>();
		system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

		ParticleSystem.MainModule main = system.main;
		main.playOnAwake = false;

		var renderer = go.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = material;
		renderer.renderMode = ParticleSystemRenderMode.Stretch;
		renderer.velocityScale = 0.05f;
		renderer.lengthScale = 1f;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		return system;
	}

	// Opacity at the start, the middle and the end of a particle's life.
	static void SetFade(ParticleSystem system, float start, float middle, float end)
	{
		ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
		fade.enabled = true;
		var gradient = new Gradient();
		gradient.SetKeys(
			new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
			new[] { new GradientAlphaKey(start, 0f), new GradientAlphaKey(middle, 0.4f), new GradientAlphaKey(end, 1f) });
		fade.color = gradient;
	}

	// One material for every effect drawn with plain sprites, made on first use.
	Material SpriteMaterial()
	{
		if (chargeMaterial == null)
		{
			Shader shader = Shader.Find("Sprites/Default");
			if (shader != null) chargeMaterial = new Material(shader);
		}
		return chargeMaterial;
	}

	// The breath, single-animation version: for Breath Duration the element
	// pours from the mouth over the player. No aiming and no cover check -- a
	// player without the sigil shield up takes a tick of damage every time,
	// wherever they stand. (Two-phase attacks use BreathDuringAnimation instead.)
	IEnumerator BreathRoutine(ElementAttack attack, AttackType type)
	{
		if (IsDead()) yield break;

		PlayerHealth player = target.GetComponentInParent<PlayerHealth>();
		if (player == null)
			Debug.LogWarning("[DragonBoss] No PlayerHealth found on/above the target -- the breath does no damage.");

		int ticks = Mathf.Max(1, Mathf.RoundToInt(type.breathDuration * breathTicksPerSecond));
		float tickInterval = type.breathDuration / ticks;
		int ticksDone = 0;
		
		StartBreath(attack);
		if (animator != null) animator.speed = type.breathAnimatorSpeed;

		for (float t = 0f; t < type.breathDuration && !IsDead(); t += Time.deltaTime)
		{
			if (CurrentAttack() != attack)
			{
				attack = CurrentAttack();
				SetBreathColor(attack.chargeColor);
			}
			AimBreath();

			// Each tick lands in the middle of its share of the breath.
			while (ticksDone < ticks && t >= (ticksDone + 0.5f) * tickInterval)
			{
				ticksDone++;
				// Split so the ticks add up to exactly Breath Damage.
				int damage = type.breathDamage * ticksDone / ticks - type.breathDamage * (ticksDone - 1) / ticks;
				if (player != null && !SpellSign.Sigils.SigilShield.IsUp)
					player.TakeDamage(damage);
			}
			yield return null;
		}
		StopBreath();
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

	// No EnemyTarget = no element; Fire is the plain single shot.
	ElementAttack CurrentAttack() => health == null ? fireAttack : AttackFor(health.CurrentElement);

	ElementAttack AttackFor(Element element)
	{
		switch (element)
		{
			case Element.Water: return waterAttack;
			case Element.Lightning: return lightningAttack;
			default: return fireAttack;
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

	void Shoot(ElementAttack attack, AttackType type, Vector3 aimPoint)
	{
		GameObject prefab = attack.projectilePrefab != null ? attack.projectilePrefab : projectilePrefab;
		if (prefab == null || target == null) return;

		// Facing its flight direction, for projectiles that aren't round.
		Vector3 toTarget = aimPoint - muzzle.position;
		Quaternion facing = toTarget.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toTarget) : Quaternion.identity;

		GameObject proj = Instantiate(prefab, muzzle.position, facing);
		proj.transform.localScale *= type.projectileScale;

		BossProjectile bp = proj.GetComponent<BossProjectile>();
		if (bp != null)
		{
			bp.damageAmount = Mathf.RoundToInt(bp.damageAmount * type.damageScale);
			bp.hitRadius *= type.projectileScale; // a bigger shot connects from further away
			bp.Launch(aimPoint, obstacleMask, target);
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

	void StartBreath(ElementAttack attack)
	{
		if (breath == null && !BuildBreath()) return;

		SetBreathColor(attack.chargeColor);

		ParticleSystem.EmissionModule breathEmission = breath.emission;
		breathEmission.rateOverTime = breathParticlesPerSecond;
		ParticleSystem.ShapeModule cone = breath.shape;
		cone.angle = breathConeAngle;

		ParticleSystem.EmissionModule floorEmission = floorFire.emission;
		floorEmission.rateOverTime = floorFireParticlesPerSecond;
		ParticleSystem.ShapeModule circle = floorFire.shape;
		circle.radius = floorFireRadius;

		AimBreath();
		breath.Play();
		floorFire.Play();
	}

	// The same color throughout a flame's life. Alpha is its strength, which
	// the BreathFlame shader turns into how much of it has burnt away.
	void SetBreathColor(Color color)
	{
		if (breath == null) return;

		var gradient = new Gradient();
		gradient.SetKeys(
			new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
			new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.4f),
					new GradientAlphaKey(0f, 1f) });

		foreach (ParticleSystem system in new[] { breath, floorFire })
		{
			ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
			fade.color = gradient;
		}
	}

	// Keeps the cone pointed from the mouth at the player's head and the floor
	// fire under their feet, every frame of the breath.
	void AimBreath()
	{
		if (breath == null) return;

		Vector3 toTarget = target.position - muzzle.position;
		float distance = toTarget.magnitude;
		Quaternion facing = distance > 0.01f ? Quaternion.LookRotation(toTarget) : muzzle.rotation;
		breath.transform.SetPositionAndRotation(muzzle.position, facing);

		// The breath carries on past the player to the floor behind them; with
		// the shield up it stops at the bubble instead.
		float reach = SpellSign.Sigils.SigilShield.IsUp
			? Mathf.Max(0.1f, distance - SpellSign.Sigils.SigilShield.Radius - ShieldClearance)
			: distance + BreathOvershoot;
		ParticleSystem.MainModule main = breath.main;
		main.startSpeed = reach / BreathParticleLife;

		floorFire.transform.position = new Vector3(target.position.x, targetFloorY, target.position.z);
	}

	// Lets the breath already in the air fade out instead of cutting it off.
	void StopBreath()
	{
		AttackWarningUI.HideWarning();

		if (breath != null) breath.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		if (floorFire != null) floorFire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
		if (animator != null) animator.speed = 1f;
	}

	// Two particle systems sharing one material: the cone from the mouth, and
	// the fire it spreads over the floor around the player. Built from code and
	// kept out of the dragon's hierarchy, like the charge-up effect.
	bool BuildBreath()
	{
		Shader shader = Resources.Load<Shader>("BreathFlame");
		if (shader == null)
		{
			Debug.LogWarning("[DragonBoss] Resources/BreathFlame shader missing -- the breath can't be shown.");
			return false;
		}

		breathNoise = NewNoiseTexture();
		breathMaterial = new Material(shader);
		breathMaterial.SetTexture("_NoiseTex", breathNoise);

		// The cone: big overlapping puffs that tumble as they fly, so the
		// stream looks like one body of flame.
		breath = NewBreathSystem(name + " Breath");
		ParticleSystem.MainModule main = breath.main;
		main.startLifetime = BreathParticleLife;
		main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1f);
		main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
		ParticleSystem.ShapeModule cone = breath.shape;
		cone.shapeType = ParticleSystemShapeType.Cone;
		cone.radius = 0.03f;
		// Small at the mouth, full size by the time it reaches the player.
		ParticleSystem.SizeOverLifetimeModule grow = breath.sizeOverLifetime;
		grow.enabled = true;
		grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));
		ParticleSystem.RotationOverLifetimeModule tumble = breath.rotationOverLifetime;
		tumble.enabled = true;
		tumble.z = new ParticleSystem.MinMaxCurve(-1f, 1f); // radians per second

		// The floor: tall flames standing upright on the ground.
		floorFire = NewBreathSystem(name + " Floor Fire");
		// The circle shape lies in the system's XY plane: tip it flat onto the floor.
		floorFire.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
		ParticleSystem.MainModule floorMain = floorFire.main;
		floorMain.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
		floorMain.startSize3D = true;
		floorMain.startSizeX = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
		floorMain.startSizeY = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
		floorMain.startSizeZ = 1f;
		floorMain.startSpeed = 0f;
		ParticleSystem.ShapeModule circle = floorFire.shape;
		circle.shapeType = ParticleSystemShapeType.Circle;
		// Flames lick upward from wherever they start.
		ParticleSystem.VelocityOverLifetimeModule rise = floorFire.velocityOverLifetime;
		rise.enabled = true;
		rise.space = ParticleSystemSimulationSpace.World;
		// All three axes have to be the same kind of curve.
		rise.x = new ParticleSystem.MinMaxCurve(0f, 0f);
		rise.y = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
		rise.z = new ParticleSystem.MinMaxCurve(0f, 0f);

		var floorRenderer = floorFire.GetComponent<ParticleSystemRenderer>();
		floorRenderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
		floorRenderer.pivot = new Vector3(0f, 0.5f, 0f); // stand on the floor instead of straddling it
		return true;
	}

	// Tiling cloudy noise for the BreathFlame shader: two layers of smoothed
	// random values, the second twice as fine.
	static Texture2D NewNoiseTexture()
	{
		const int size = 64;
		const int cells = 8;
		var random = new System.Random(7);
		var coarse = new float[cells * cells];
		var fine = new float[cells * cells * 4];
		for (int i = 0; i < coarse.Length; i++) coarse[i] = (float)random.NextDouble();
		for (int i = 0; i < fine.Length; i++) fine[i] = (float)random.NextDouble();

		var pixels = new Color32[size * size];
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				float u = (float)x / size, v = (float)y / size;
				float value = 0.65f * SampleTiled(coarse, cells, u, v) + 0.35f * SampleTiled(fine, cells * 2, u, v);
				byte b = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
				pixels[y * size + x] = new Color32(b, b, b, 255);
			}
		}

		// Linear: the values are amounts, not colors.
		var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
		texture.wrapMode = TextureWrapMode.Repeat;
		texture.filterMode = FilterMode.Bilinear;
		texture.SetPixels32(pixels);
		texture.Apply(false, true);
		return texture;
	}

	// Smoothly blended value from a square grid that wraps at the edges.
	static float SampleTiled(float[] grid, int cells, float u, float v)
	{
		float gx = u * cells, gy = v * cells;
		int x0 = Mathf.FloorToInt(gx) % cells, y0 = Mathf.FloorToInt(gy) % cells;
		int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells;
		float tx = Mathf.SmoothStep(0f, 1f, gx - Mathf.Floor(gx));
		float ty = Mathf.SmoothStep(0f, 1f, gy - Mathf.Floor(gy));
		return Mathf.Lerp(Mathf.Lerp(grid[y0 * cells + x0], grid[y0 * cells + x1], tx),
						  Mathf.Lerp(grid[y1 * cells + x0], grid[y1 * cells + x1], tx), ty);
	}

	ParticleSystem NewBreathSystem(string systemName)
	{
		var go = new GameObject(systemName);
		ParticleSystem system = go.AddComponent<ParticleSystem>();
		system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

		ParticleSystem.MainModule main = system.main;
		main.playOnAwake = false;
		main.loop = true;
		// World, so breath already in the air keeps its path when the head moves.
		main.simulationSpace = ParticleSystemSimulationSpace.World;

		ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
		fade.enabled = true;

		var renderer = go.GetComponent<ParticleSystemRenderer>();
		renderer.sharedMaterial = breathMaterial;
		renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		renderer.receiveShadows = false;
		return system;
	}

	// Built from code (Sprites/Default, like DeflectRipple) and kept out of the
	// dragon's hierarchy: EnemyTarget flashes, hides and measures every child
	// renderer, and this one shouldn't be part of that.
	bool BuildCharge()
	{
		Material material = SpriteMaterial();
		if (material == null)
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
		renderer.sharedMaterial = material;
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
