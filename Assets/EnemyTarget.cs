using UnityEngine;
using System.Collections;

// Attach to any GameObject to make it a hittable placeholder enemy.
// FireballProjectile and LightningBolt both look for this component
// (via GetComponentInParent, so it can sit on a parent above the actual
// hit collider) and call TakeHit() on it -- that's the only wiring needed.
//
// Works for both a single-mesh enemy (like the capsule) and a multi-part
// model (like a dragon split into separate Cube/Cube.002 child meshes) --
// it flashes every Renderer found anywhere under this object, not just one
// on this exact GameObject.

public class EnemyTarget : MonoBehaviour
{
    [Tooltip("How long the hit-flash lasts, in seconds.")]
    public float flashDuration = 0.15f;

    [Tooltip("Color to flash to when hit.")]
    public Color flashColor = Color.white;

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
    }

    public void TakeHit()
    {
        Debug.Log($"{gameObject.name} was hit!");

        if (renderers == null || renderers.Length == 0) return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
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