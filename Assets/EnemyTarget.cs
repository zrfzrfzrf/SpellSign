using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// A hittable enemy. Spells call TakeHit(element); how much it hurts depends
// on the enemy's current element (see ElementRules). The element follows
// remaining health: top third Water, middle third Fire, bottom third
// Lightning. SetElement() lets other code switch it directly.
//
// A hit that takes the enemy into its next phase raises PhaseChangeStarted
// and leaves the element as it was: whoever listens (DragonBoss) plays the
// change and calls FinishPhaseChange() when the new element should take over.
// With nobody listening the element switches on the spot.
//
// At zero health the enemy dies: it stops taking hits, blinks a few times and
// deactivates. Revive() brings it back in place at full health.
//
// Lives on the root of the enemy; spells find it with GetComponentInParent,
// so the hit collider can be on any child part of the model.
public class EnemyTarget : MonoBehaviour, ISpellTarget
{
	[Header("Flash")]
	public float flashDuration = 0.15f;
	public Color flashColor = Color.white;

	[Header("Death")]
	public int deathBlinks = 4;
	public float blinkInterval = 0.15f;

	[Header("Health")]
	public int maxHealth = 30;
	[Tooltip("Optional UI Image with Image Type = Filled.")]
	public Image healthBarFill;

	[Header("Head")]
	[Tooltip("The hit collider on the head. Hits on it deal extra damage, and the health bar sits above it. Empty = no headshots, and the bar sits over the middle of the model.")]
	public BoxCollider headCollider;

	[Header("Damage numbers")]
	public Color normalColor = Color.white;
	public Color effectiveColor = new Color(0.94f, 0.62f, 0.15f);
	[Tooltip("Size of the number for hits on the enemy's weakness, relative to normal hits.")]
	public float effectiveSize = 1.4f;
	[Tooltip("How far in front of the hit point (toward the player) numbers appear, in meters.")]
	public float numberLift = 0.25f;

	public int CurrentHealth { get; private set; }

	public Element CurrentElement { get; private set;  }

	// whiten: 0 = the element's own colors, 1 = all white. For the flash of a
	// phase change.
	public void ChangeElementVisual(Element element, float whiten = 0f)
	{
		switch (element)
		{
			case Element.Fire:
				ChangeElementColorFromIndex(0, whiten);
				return;

			case Element.Lightning:
				ChangeElementColorFromIndex(1, whiten);
				return;

			case Element.Water:
				ChangeElementColorFromIndex(2, whiten);
				return;
		}
	}

	[Serializable]
	public class RendererColor
	{
		public Renderer[] renderers;
		public int materialIndex = 0;
		public Color color = Color.white;
	}

	[Serializable]
	public class CreatureMode
	{
		public string name;
		public RendererColor[] parts;
	}


	[SerializeField]
	private CreatureMode[] dragonModeColors;

	void ChangeElementColorFromIndex(int elementIndex, float whiten)
	{
		CreatureMode chosenMode = dragonModeColors[elementIndex];

		for (int i = 0; i < chosenMode.parts.Length; i++)
		{
			for (int j = 0; j < chosenMode.parts[i].renderers.Length; j++)
			{
				var mat = chosenMode.parts[i].renderers[j].materials[chosenMode.parts[i].materialIndex];
				Color color = chosenMode.parts[i].color;
				Color shown = Color.Lerp(color, Color.white, whiten);
				shown.a = color.a;
				mat.SetColor("_BaseColor", shown);
			}
		}
	}
	public bool IsDead { get; private set; }

	// How many phases the health is split into, for the health bar's marks.
	public const int Phases = 3;

	// While true, spells bounce off as if the enemy were immune to them.
	public bool Invulnerable { get; set; }

	// True from the hit that crossed into the next phase until FinishPhaseChange().
	public bool ChangingPhase { get; private set; }

	// Raised by the hit that takes health into the next phase, with that
	// phase's element. The element itself doesn't change until FinishPhaseChange().
	public event Action<Element> PhaseChangeStarted;

	// Raised whenever the element changes -- hook element-specific visuals here.
	public event Action<Element> ElementChanged;
	// Hooks for death/revive animations.
	public event Action Died;
	public event Action Revived;

	Renderer[] renderers;
	Color[] originalColors;
	Collider[] colliders;
	Collider hitCollider;
	Coroutine flashRoutine;

	void Awake()
	{
		renderers = GetComponentsInChildren<Renderer>();
		originalColors = new Color[renderers.Length];
		for (int i = 0; i < renderers.Length; i++)
			originalColors[i] = renderers[i].material.color;

		colliders = Array.FindAll(GetComponentsInChildren<Collider>(), c => c.enabled); // ones you disabled stay off after a revive
		hitCollider = colliders.Length > 0 ? colliders[0] : null;

		CurrentHealth = maxHealth;
		CurrentElement = ElementForHealth(maxHealth);
		ChangeElementVisual(ElementForHealth(maxHealth));
		UpdateHealthBar();
	}

	// hitPoint: where the spell struck, for the damage number / deflect ripple.
	// Without it they appear on the side of the enemy facing the player.
	// struck: the collider the spell hit, to tell a headshot from a body hit.
	public void TakeHit(Element attack, Vector3? hitPoint = null, Collider struck = null)
	{
		if (IsDead) return;

		int damage = ElementRules.Damage(attack, CurrentElement);
		Vector3 point = hitPoint ?? PointFacingPlayer();

		if (damage == ElementRules.Immune || Invulnerable)
		{
			SpellSign.CombatFeedback.DeflectRipple.Spawn(point);
			return;
		}

		bool effective = damage == ElementRules.Effective;
		// A headshot adds a random bonus: at least 1, at most double the damage.
		if (headCollider != null && struck == headCollider)
			damage = UnityEngine.Random.Range(damage + 1, damage * 2 + 1); // the upper end is left out
		ShowDamageNumber(damage, effective, point);

		//Flash();

		Element phaseBefore = ElementForHealth(CurrentHealth);
		CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
		UpdateHealthBar();

		if (CurrentHealth == 0)
		{
			Die();
			return;
		}

		Element phaseAfter = ElementForHealth(CurrentHealth);
		if (phaseAfter == phaseBefore || ChangingPhase) return; // a change under way picks up the newest phase when it finishes

		if (PhaseChangeStarted != null)
		{
			ChangingPhase = true;
			PhaseChangeStarted(phaseAfter);
		}
		else
			SetElement(phaseAfter);
	}

	// Switches to the element that goes with the health left, ending a phase
	// change. Also puts the element's colors back after a whitened flash.
	public void FinishPhaseChange()
	{
		ChangingPhase = false;
		SetElement(ElementForHealth(CurrentHealth));
	}

	public void Revive()
	{
		// No "only if dead" guard: a level restart also uses this to reset a boss that is still alive.

		StopAllCoroutines(); // in case it's revived mid-blink
		IsDead = false;
		Invulnerable = false;
		ChangingPhase = false;
		CurrentHealth = maxHealth;
		UpdateHealthBar();
		SetElement(ElementForHealth(maxHealth));
		SetVisible(true);
		foreach (var c in colliders) c.enabled = true;

		gameObject.SetActive(true);
		Debug.Log($"{name} revived.");
		Revived?.Invoke();
	}

	void Die()
	{
		IsDead = true;
		Debug.Log($"{name} defeated.");

		// Spells pass straight through while it blinks out.
		foreach (var c in colliders) c.enabled = false;

		// A hit flash cut short by deactivation would leave the model white.
		if (flashRoutine != null) StopCoroutine(flashRoutine);
		RestoreColors();
		// Those are the model's own colors: put the element's back, so it
		// blinks out looking the way it did when it died.
		ChangeElementVisual(CurrentElement);

		Died?.Invoke();
		StartCoroutine(BlinkOut());
	}

	IEnumerator BlinkOut()
	{
		for (int i = 0; i < deathBlinks; i++)
		{
			SetVisible(false);
			yield return new WaitForSeconds(blinkInterval);
			SetVisible(true);
			yield return new WaitForSeconds(blinkInterval);
		}
		gameObject.SetActive(false);
	}

	void SetVisible(bool visible)
	{
		foreach (var r in renderers) r.enabled = visible;
	}

	public void SetElement(Element element)
	{
		Debug.Log("SETTING?");

		ChangeElementVisual(element);

		if (element == CurrentElement) return;

		CurrentElement = element;
		Debug.Log($"{name} element -> {element}");
		ElementChanged?.Invoke(element);
	}

	// The element that goes with the health left right now. Differs from
	// CurrentElement while a phase change is under way.
	public Element PhaseElement => ElementForHealth(CurrentHealth);

	Element ElementForHealth(int health)
	{
		Debug.Log("CALLING FOR HEALTH");

		if (health * 3 > maxHealth * 2) return Element.Water;
		if (health * 3 > maxHealth) return Element.Fire;
		return Element.Lightning;
	}

	void ShowDamageNumber(int damage, bool effective, Vector3 point)
	{
		Camera cam = Camera.main;
		if (cam != null)
			point += (cam.transform.position - point).normalized * numberLift;

		DamageNumber.Spawn(point, $"-{damage}", effective ? effectiveColor : normalColor,
						   effective ? effectiveSize : 1f);
	}

	// The point on the enemy's visible bounds closest to the player's head.
	Vector3 PointFacingPlayer()
	{
		Camera cam = Camera.main;
		if (cam == null || !TryGetVisibleBounds(out Bounds b))
			return hitCollider != null ? hitCollider.bounds.center : transform.position;
		return b.ClosestPoint(cam.transform.position);
	}

	// World bounds of the enemy's model. Uses only the skinned (animated) parts
	// when there are any: the dragon's teeth are separate static meshes that
	// don't follow the skeleton and sit well away from the body.
	public bool TryGetVisibleBounds(out Bounds bounds)
	{
		bounds = default;
		bool skinnedOnly = false;
		foreach (var r in renderers)
			if (r is SkinnedMeshRenderer) { skinnedOnly = true; break; }

		bool any = false;
		foreach (var r in renderers)
		{
			if (skinnedOnly && !(r is SkinnedMeshRenderer)) continue;
			if (!any) { bounds = r.bounds; any = true; }
			else bounds.Encapsulate(r.bounds);
		}
		return any;
	}

	// World bounds of the head's hit collider, worked out from its transform so
	// they are right this frame and while the collider is off (a dying enemy).
	public bool TryGetHeadBounds(out Bounds bounds)
	{
		bounds = default;
		if (headCollider == null) return false;

		Transform head = headCollider.transform;
		Vector3 half = headCollider.size * 0.5f;
		bounds = new Bounds(head.TransformPoint(headCollider.center), Vector3.zero);
		for (int i = 0; i < 8; i++)
		{
			var corner = new Vector3((i & 1) == 0 ? -half.x : half.x,
									 (i & 2) == 0 ? -half.y : half.y,
									 (i & 4) == 0 ? -half.z : half.z);
			bounds.Encapsulate(head.TransformPoint(headCollider.center + corner));
		}
		return true;
	}

	void UpdateHealthBar()
	{
		if (healthBarFill != null)
			healthBarFill.fillAmount = (float)CurrentHealth / maxHealth;
	}

	void Flash()
	{
		if (renderers.Length == 0) return;
		if (flashRoutine != null) StopCoroutine(flashRoutine);
		flashRoutine = StartCoroutine(FlashRoutine());
	}

	IEnumerator FlashRoutine()
	{
		foreach (var r in renderers) r.material.color = flashColor;
		yield return new WaitForSeconds(flashDuration);
		RestoreColors();
	}

	void RestoreColors()
	{
		for (int i = 0; i < renderers.Length; i++)
			renderers[i].material.color = originalColors[i];
	}
}
