using TMPro;
using UnityEngine;

// World-space damage text that bursts out of an enemy: grows from small to
// full size while drifting up and outward, then fades. Built entirely from
// code (uses TMP's default font), so it needs no prefab or scene setup.
public class DamageNumber : MonoBehaviour
{
    const float Lifetime = 0.9f;
    const float StartScale = 0.2f;
    const float Speed = 0.8f;       // meters per second
    const float Spread = 0.6f;      // sideways scatter relative to upward motion
    const float FadeStart = 0.6f;   // fraction of lifetime before fading begins
    const float FontSize = 3f;      // roughly 0.3 m tall at full scale

    TextMeshPro text;
    Vector3 velocity;
    float age;

    public static void Spawn(Vector3 position, string label, Color color)
    {
        var go = new GameObject("DamageNumber");
        go.transform.position = position;
        go.transform.localScale = Vector3.one * StartScale;

        var number = go.AddComponent<DamageNumber>();
        number.text = go.AddComponent<TextMeshPro>();
        number.text.text = label;
        number.text.color = color;
        number.text.fontSize = FontSize;
        number.text.fontStyle = FontStyles.Bold;
        number.text.alignment = TextAlignmentOptions.Center;

        Vector2 side = Random.insideUnitCircle * Spread;
        number.velocity = new Vector3(side.x, 1f, side.y).normalized * Speed;
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

        transform.position += velocity * Time.deltaTime;

        float easeOut = 1f - (1f - t) * (1f - t);
        transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, easeOut);

        text.alpha = t < FadeStart ? 1f : 1f - (t - FadeStart) / (1f - FadeStart);

        // TMP text reads correctly when its forward points away from the viewer.
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
