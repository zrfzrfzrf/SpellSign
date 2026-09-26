using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Attach to any GameObject to make it a hittable enemy. FireballProjectile
// and LightningBolt both look for this component (via GetComponentInParent,
// so it can sit on a parent above the actual hit collider) and call
// TakeHit() on it -- that's the only wiring needed on the spell side.
//
// Works for both a single-mesh enemy (like the capsule) and a multi-part
// model (like a dragon split into separate Cube/Cube.002 child meshes) --
// it flashes every Renderer found anywhere under this object, not just one
// on this exact GameObject.
//
// Now also tracks health and (optionally) drives a boss health bar UI
// Image. If healthBarFill is left unassigned, health is still tracked and
// logged, just nothing visual updates -- so this is safe to use on the
// capsule too without wiring a UI for it.

public class EnemyTarget : MonoBehaviour
{
    [Header("Flash")]
    [Tooltip("How long the hit-flash lasts, in seconds.")]
    public float flashDuration = 0.15f;

    [Tooltip("Color to flash to when hit.")]
    public Color flashColor = Color.white;

    [Header("Health")]
    public int maxHealth = 100;

    [Tooltip("Optional -- a UI Image with Image Type = Filled. Its fill amount is kept in sync with current health. Leave empty if this enemy doesn't have a health bar.")]
    public Image healthBarFill;

    public int CurrentHealth { get; private set; }
    public bool Defeated { get; private set; }

    private Renderer[] renderers;
    private Color[] originalColors;
    private Coroutine flashRoutine;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].material.color;
        }

        CurrentHealth = maxHealth;
        UpdateHealthBar();
    }

    // damage defaults to 10 so existing calls like target.TakeHit() (no
    // arguments) from FireballProjectile/LightningBolt keep compiling and
    // working exactly as before -- pass an explicit amount if you want
    // different spells to hit for different amounts.
    public void TakeHit(int damage = 10){
        Debug.Log($"{gameObject.name} was hit for {damage}!");
    
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        UpdateHealthBar();
    
        if (renderers != null && renderers.Length > 0)
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }
    
        if (CurrentHealth <= 0)
        {
            Debug.Log($"{gameObject.name} defeated -- resetting health so you can keep testing hits.");
            CurrentHealth = maxHealth;
            UpdateHealthBar();
        }
    }   

    private void UpdateHealthBar()
    {
        if (healthBarFill != null)
            healthBarFill.fillAmount = maxHealth > 0 ? (float)CurrentHealth / maxHealth : 0f;
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material.color = flashColor;

        yield return new WaitForSeconds(flashDuration);

        for (int i = 0; i < renderers.Length; i++)
            renderers[i].material.color = originalColors[i];
    }
}
