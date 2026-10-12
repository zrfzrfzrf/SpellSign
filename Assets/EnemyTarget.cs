using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// A hittable enemy. Spells call TakeHit(element); how much it hurts depends
// on the enemy's current element (see ElementRules). The element follows
// remaining health: top third Water, middle third Fire, bottom third
// Lightning. SetElement() lets other code switch it directly.
//
// At zero health the enemy dies: it stops taking hits, blinks a few times and
// deactivates. Revive() brings it back in place at full health.
//
// Lives on the root of the enemy; spells find it with GetComponentInParent,
// so the hit collider can be on any child part of the model.
public class EnemyTarget : MonoBehaviour
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

	public void ChangeElementVisual(Element element)
	{
		switch (element)
		{
			case Element.Fire:
				ChangeElementColorFromIndex(0);
				return;

			case Element.Lightning:
				ChangeElementColorFromIndex(1);
				return;

			case Element.Water:
				ChangeElementColorFromIndex(2);
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

	void ChangeElementColorFromIndex(int elementIndex)
	{
		CreatureMode chosenMode = dragonModeColors[elementIndex];

		for (int i = 0; i < chosenMode.parts.Length; i++)
		{
			for (int j = 0; j < chosenMode.parts[i].renderers.Length; j++)
			{
				var mat = chosenMode.parts[i].renderers[j].materials[chosenMode.parts[i].materialIndex];
				mat.SetColor("_BaseColor", chosenMode.parts[i].color);
			}
		}
	}
	public bool IsDead { get; private set; }

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

		if (damage == ElementRules.Immune)
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
		if (phaseAfter != phaseBefore)
			SetElement(phaseAfter);
	}

	public void Revive()
	{
		// No "only if dead" guard: a level restart also uses this to reset a boss that is still alive.

		StopAllCoroutines(); // in case it's revived mid-blink
		IsDead = false;
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
