using UnityEngine;
using System.Collections;

// Attach to any GameObject with a Collider (a Capsule/Cube primitive works
// fine) to make it a hittable placeholder enemy. FireballProjectile and
// LightningBolt both look for this component on whatever they hit and call
// TakeHit() on it -- that's the only wiring needed.

public class EnemyTarget : MonoBehaviour
{
    [Tooltip("How long the hit-flash lasts, in seconds.")]
    public float flashDuration = 0.15f;

    [Tooltip("Color to flash to when hit.")]
    public Color flashColor = Color.white;

    private Renderer rend;
    private Color originalColor;
    private Coroutine flashRoutine;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        if (rend != null)
            originalColor = rend.material.color;
    }

    public void TakeHit()
    {
        Debug.Log($"{gameObject.name} was hit!");

        if (rend == null) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        rend.material.color = flashColor;
        yield return new WaitForSeconds(flashDuration);
        rend.material.color = originalColor;
    }
}
