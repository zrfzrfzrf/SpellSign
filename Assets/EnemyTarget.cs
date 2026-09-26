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
    public Color immuneColor = new Color(0.6f, 0.6f, 0.6f);
    public Color normalColor = Color.white;
    public Color effectiveColor = new Color(1f, 0.25f, 0.2f);

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

    public void TakeHit(Element attack)
    {
        int damage = ElementRules.Damage(attack, CurrentElement);
        ShowDamageNumber(damage);
        if (damage == ElementRules.Immune) return;

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

    void ShowDamageNumber(int damage)
    {
        string label = damage == ElementRules.Immune ? "immune" : $"-{damage}";
        Color color = damage == ElementRules.Immune ? immuneColor
                    : damage == ElementRules.Effective ? effectiveColor
                    : normalColor;

        Vector3 origin = transform.position;
        if (hitCollider != null)
        {
            Bounds b = hitCollider.bounds;
            origin = b.center + Vector3.up * b.extents.y;
        }
        DamageNumber.Spawn(origin, label, color);
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
