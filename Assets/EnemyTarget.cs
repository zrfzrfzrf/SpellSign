using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// A hittable enemy. Spells call TakeHit(element); how much it hurts depends
// on the enemy's current element (see ElementRules). The element follows
// remaining health: top third Water, middle third Fire, bottom third
// Lightning. SetElement() lets other code switch it directly.
//
// Lives on the root of the enemy; spells find it with GetComponentInParent,
// so the hit collider can be on any child part of the model.
public class EnemyTarget : MonoBehaviour
{
    [Header("Flash")]
    public float flashDuration = 0.15f;
    public Color flashColor = Color.white;

    [Header("Health")]
    public int maxHealth = 30;
    [Tooltip("Optional UI Image with Image Type = Filled.")]
    public Image healthBarFill;

    [Header("Damage numbers")]
    public Color normalColor = Color.white;
    public Color effectiveColor = new Color(0.94f, 0.62f, 0.15f);
    [Tooltip("Size of the number for hits on the enemy's weakness, relative to normal hits.")]
    public float effectiveSize = 1.4f;
    [Tooltip("How far in front of the hit point (toward the player) numbers appear, in meters.")]
    public float numberLift = 0.25f;

    public int CurrentHealth { get; private set; }
    public Element CurrentElement { get; private set; }

    // Raised whenever the element changes -- hook element-specific visuals here.
    public event Action<Element> ElementChanged;

    Renderer[] renderers;
    Color[] originalColors;
    Collider hitCollider;
    Coroutine flashRoutine;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            originalColors[i] = renderers[i].material.color;

        hitCollider = GetComponentInChildren<Collider>();

        CurrentHealth = maxHealth;
        CurrentElement = ElementForHealth(maxHealth);
        UpdateHealthBar();
    }

    // hitPoint: where the spell struck, for the damage number / deflect ripple.
    // Without it they appear on the side of the enemy facing the player.
    public void TakeHit(Element attack, Vector3? hitPoint = null)
    {
        int damage = ElementRules.Damage(attack, CurrentElement);
        Vector3 point = hitPoint ?? PointFacingPlayer();

        if (damage == ElementRules.Immune)
        {
            SpellSign.CombatFeedback.DeflectRipple.Spawn(point);
            return;
        }
        ShowDamageNumber(damage, point);

        Flash();

        Element phaseBefore = ElementForHealth(CurrentHealth);
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);

        if (CurrentHealth == 0)
        {
            // Demo mode: refill so testing can continue.
            Debug.Log($"{name} defeated -- resetting health.");
            CurrentHealth = maxHealth;
        }
        UpdateHealthBar();

        Element phaseAfter = ElementForHealth(CurrentHealth);
        if (phaseAfter != phaseBefore)
            SetElement(phaseAfter);
    }

    public void SetElement(Element element)
    {
        if (element == CurrentElement) return;
        CurrentElement = element;
        Debug.Log($"{name} element -> {element}");
        ElementChanged?.Invoke(element);
    }

    Element ElementForHealth(int health)
    {
        if (health * 3 > maxHealth * 2) return Element.Water;
        if (health * 3 > maxHealth) return Element.Fire;
        return Element.Lightning;
    }

    void ShowDamageNumber(int damage, Vector3 point)
    {
        bool effective = damage == ElementRules.Effective;
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
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material.color = originalColors[i];
    }
}
