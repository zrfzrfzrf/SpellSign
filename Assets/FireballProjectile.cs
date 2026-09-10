using UnityEngine;

// Attach this to your Fireball prefab (a sphere with a Rigidbody).
// It just flies forward in a straight line and destroys itself either
// when it hits something, or after a few seconds if it never does.

[RequireComponent(typeof(Rigidbody))]
public class FireballProjectile : MonoBehaviour
{
    [Tooltip("How fast the fireball flies forward, in meters per second")]
    public float speed = 6f;

    [Tooltip("How many seconds before the fireball destroys itself if it never hits anything")]
    public float lifetime = 5f;

    [Tooltip("Brief grace period (seconds) right after spawning where collisions are IGNORED. This prevents the fireball from instantly destroying itself if it spawns overlapping or very close to your own hand -- very likely what's been causing it to 'appear and immediately disappear.'")]
    public float spawnGracePeriod = 0.15f;

    private Rigidbody rb;
    private float spawnTime;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // fireballs fly straight, they don't arc/fall

        // Launch immediately, in the same frame it's created -- no
        // pause sitting at the spawn point before it starts moving.
        rb.linearVelocity = transform.forward * speed;
        spawnTime = Time.time;

        // Safety net: destroy it after a while even if it never hits anything,
        // so you don't end up with fireballs flying forever off into the distance
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Ignore collisions during the brief grace period right after
        // spawning -- this is almost certainly why it was disappearing
        // instantly: it spawned overlapping your own hand and "hit" it
        // before it had a chance to actually fly clear.
        if (Time.time - spawnTime < spawnGracePeriod)
        {
            return;
        }

        // TODO: this is where you'll check if it hit your monster and deal
        // damage -- for now it just disappears on any hit, which is a good
        // first test to confirm it's actually flying and colliding correctly.
        Debug.Log("Fireball hit: " + collision.gameObject.name);
        Destroy(gameObject);
    }
}