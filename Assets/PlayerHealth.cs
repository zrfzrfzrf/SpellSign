using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Put this on the CenterEyeAnchor (or anywhere under it), alongside the
// World Space Canvas that holds your HUD. BossProjectile calls
// TakeDamage() on whatever it finds via
// playerTarget.GetComponentInParent<PlayerHealth>() when one of its shots
// gets close enough to your head -- see BossProjectile.cs.

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 100;

    [Header("UI")]
    [Tooltip("Full-screen red Image, alpha 0 at rest. Flashes on damage.")]
    public Image damageFlashImage;
    [Tooltip("UI Image with Image Type = Filled. Kept in sync with current health.")]
    public Image healthBarFill;

    [Header("Flash")]
    public float flashAlpha = 0.35f;
    public float flashFadeTime = 0.3f;

    public int CurrentHealth { get; private set; }

    private Coroutine flashRoutine;

    void Awake()
    {
        CurrentHealth = maxHealth;
        UpdateHealthBar();

        if (damageFlashImage != null)
        {
            Color c = damageFlashImage.color;
            c.a = 0f;
            damageFlashImage.color = c;
        }
    }

    public void TakeDamage(int amount)
    {
        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        UpdateHealthBar();

        if (damageFlashImage != null)
        {
            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        if (CurrentHealth <= 0)
        {
            Debug.Log("[PlayerHealth] Player defeated!");
        }
    }

    private void UpdateHealthBar()
    {
        if (healthBarFill != null)
            healthBarFill.fillAmount = maxHealth > 0 ? (float)CurrentHealth / maxHealth : 0f;
    }

    private IEnumerator FlashRoutine()
    {
        Color c = damageFlashImage.color;

        c.a = flashAlpha;
        damageFlashImage.color = c;

        float t = 0f;
        while (t < flashFadeTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(flashAlpha, 0f, t / flashFadeTime);
            damageFlashImage.color = c;
            yield return null;
        }

        c.a = 0f;
        damageFlashImage.color = c;
    }
}
