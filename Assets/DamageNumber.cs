using TMPro;
using UnityEngine;

// World-space damage number that pops out of an enemy toward the player: it
// overshoots to full size, settles, drifts up, then fades. Bold with a thick
// dark outline, and drawn on top of everything (overlay text material), so it
// never sinks into the enemy's model. It keeps the same size in view however
// far away the enemy is.
//
// Built from code (TMP's default font); the overlay material is loaded from
// Resources/DamageNumberOverlay (Assets/CombatFeedback/Resources).
public class DamageNumber : MonoBehaviour
{
    const float Lifetime = 1f;
    const float PopTime = 0.12f;       // grow to overshoot
    const float SettleTime = 0.1f;     // shrink back to full size
    const float Overshoot = 1.3f;
    const float StartScale = 0.3f;
    const float RiseSpeed = 0.12f;     // meters per second at 1 m away
    const float FadeStart = 0.65f;     // fraction of lifetime before fading begins
    const float FontSize = 0.9f;       // roughly 9 cm tall at 1 m away
    const float Scatter = 0.08f;       // sideways jitter at 1 m, so repeated hits don't stack

    static Material overlayMaterial;
    static bool overlayMaterialLoaded;

    TextMeshPro text;
    float baseScale;
    float riseSpeed;
    float age;

    // size: 1 for normal hits, bigger for hits that land on the enemy's weakness.
    public static void Spawn(Vector3 position, string label, Color color, float size = 1f)
    {
        Camera cam = Camera.main;
        float distance = cam != null ? Mathf.Max(0.3f, Vector3.Distance(cam.transform.position, position)) : 1f;

        if (cam != null)
        {
            Vector2 jitter = Random.insideUnitCircle * Scatter * distance;
            position += cam.transform.right * jitter.x + cam.transform.up * jitter.y;
        }

        var go = new GameObject("DamageNumber");
        go.transform.position = position;

        var number = go.AddComponent<DamageNumber>();
        number.baseScale = distance * size;
        number.riseSpeed = RiseSpeed * distance;
        go.transform.localScale = Vector3.one * number.baseScale * StartScale;

        number.text = go.AddComponent<TextMeshPro>();
        if (!overlayMaterialLoaded)
        {
            overlayMaterial = Resources.Load<Material>("DamageNumberOverlay");
            overlayMaterialLoaded = true;
            if (overlayMaterial == null)
                Debug.LogWarning("[DamageNumber] Resources/DamageNumberOverlay material missing -- numbers can hide inside the enemy.");
        }
        if (overlayMaterial != null) number.text.fontSharedMaterial = overlayMaterial;

        number.text.text = label;
        number.text.color = color;
        number.text.fontSize = FontSize;
        number.text.fontStyle = FontStyles.Bold;
        number.text.alignment = TextAlignmentOptions.Center;
        number.text.textWrappingMode = TextWrappingModes.NoWrap;
        number.FaceCamera();
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        float scale;
        if (age < PopTime)
        {
            float p = age / PopTime;
            scale = Mathf.Lerp(StartScale, Overshoot, 1f - (1f - p) * (1f - p));
        }
        else if (age < PopTime + SettleTime)
        {
            scale = Mathf.Lerp(Overshoot, 1f, (age - PopTime) / SettleTime);
        }
        else
        {
            scale = 1f;
        }
        transform.localScale = Vector3.one * baseScale * scale;

        text.alpha = t < FadeStart ? 1f : 1f - (t - FadeStart) / (1f - FadeStart);
        FaceCamera();
    }

    // TMP text reads correctly when its forward points away from the viewer.
    void FaceCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
