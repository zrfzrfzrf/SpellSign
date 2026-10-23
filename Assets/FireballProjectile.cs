using UnityEngine;


[RequireComponent(typeof(Rigidbody))]
public class FireballProjectile : MonoBehaviour
{
    [Tooltip("Element this projectile deals. Shared by the Fireball (Fire) and WaterWave (Water) prefabs.")]
    public Element element = Element.Fire;

    [Tooltip("How fast the fireball flies forward, in meters per second")]
    public float speed = 6f;

    [Tooltip("How many seconds before the fireball destroys itself if it never hits anything")]
    public float lifetime = 5f;

    [Tooltip("Brief grace period (seconds) right after spawning where a collision with your OWN hand/body (tagged 'Player') is ignored -- in case the fireball spawns overlapping it. This never ignores real-world furniture or an enemy, no matter how close or how soon.")]
    public float spawnGracePeriod = 0.15f;

    [Tooltip("Tag used on your own hand/body colliders, if any. Collisions with objects carrying this tag are the ONLY thing the grace period ever ignores. If nothing in your hand rig has a collider (or none is tagged this way), this branch simply never triggers -- which is correct.")]
    public string selfTag = "Player";

    private Rigidbody rb;
    private float spawnTime;
    private bool hasHit;
    private Vector3 launchVelocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;

        // Launch immediately, in the same frame it's created -- no
        // pause sitting at the spawn point before it starts moving.
        launchVelocity = transform.forward * speed;
        rb.linearVelocity = launchVelocity;
        spawnTime = Time.time;

        // Safety net: destroy it after a while even if it never hits anything,
        // so you don't end up with fireballs flying forever off into the distance
        Destroy(gameObject, lifetime);

        // With spells set to pass through walls, the scanned room is not there
        // for this shot at all.
        if (RoomWalls.SpellsPassThrough)
            foreach (Collider room in RoomWalls.RoomColliders())
                foreach (Collider own in GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(own, room);
    }

    void OnCollisionEnter(Collision collision)
    {
        // Only ignore a collision here if it's tagged as YOUR OWN hand/body,
        // and only within the short grace window right after spawn. Real-world
        // furniture (EffectMesh colliders) and the enemy are never exempted,
        // no matter how soon the hit happens -- that was the bug: everything
        // nearby was getting swallowed in that window, not just your hand.
        bool isSelf = !string.IsNullOrEmpty(selfTag) && collision.gameObject.CompareTag(selfTag);
        if (isSelf && Time.time - spawnTime < spawnGracePeriod)
        {
            Debug.Log("[FireballProjectile] Ignored early self-collision with: " + collision.gameObject.name);
            return;
        }

        // A piece of the room that appeared after launch: fly on through it.
        if (RoomWalls.Ignores(collision.collider))
        {
            Physics.IgnoreCollision(collision.GetContact(0).thisCollider, collision.collider);
            rb.linearVelocity = launchVelocity; // the contact already knocked it off course
            return;
        }

        // Destroy() only takes effect at the end of the frame, and a shot that
        // touches two colliders at once (the dragon's overlapping hitboxes, or
        // the dragon and a wall) gets a callback for each. Count the first only.
        if (hasHit) return;
        hasHit = true;

        Debug.Log("Fireball hit: " + collision.gameObject.name);

        // GetComponentInParent, not GetComponent -- the hit collider may be
        // on a CHILD part of a multi-mesh model (e.g. the dragon's Cube/
        // Cube.002), while EnemyTarget sits on the root object.
        ISpellTarget target = collision.gameObject.GetComponentInParent<ISpellTarget>();
        if (target != null)
            target.TakeHit(element, collision.GetContact(0).point, collision.collider);

        Destroy(gameObject);
    }
}