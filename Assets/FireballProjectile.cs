using UnityEngine;


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
        rb.useGravity = false;

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
        if (Time.time - spawnTime < spawnGracePeriod)
        {
            return;
        }

        // TODO: this is where we check if it hit the monster and deal damage
        Debug.Log("Fireball hit: " + collision.gameObject.name);
        Destroy(gameObject);
    }
}