using System.Collections.Generic;
using UnityEngine;

// Light aim assist, shared by every spell. When a spell is cast, its direction
// is bent toward a target (the dragon, a tutorial crystal) that lies within a
// small cone around where the hand aimed: all the way when the aim was nearly
// on it, less and less toward the edge of the cone, not at all outside it --
// so a shot that was meant elsewhere still goes there. Only the launch
// direction changes; nothing steers the spell in flight.
//
// A spell that travels (a fireball) is aimed a little ahead of a moving
// target, by how far the target moves while the spell flies. With the room
// blocking spells (RoomWalls), a target behind a real wall isn't aimed at.
public static class AimAssist
{
    const float VelocitySmoothing = 8f;  // how fast the measured target speed follows the raw one, per second
    const float TowardCenter = 0.25f;    // aim this share of the way from the nearest edge of a hitbox to its middle
    const float MaxLeadTime = 1.5f;      // seconds ahead a moving target is led, at most

    class Tracked
    {
        public Vector3 position;
        public Vector3 velocity;
        public float time;
    }

    static readonly Dictionary<MonoBehaviour, Tracked> tracked = new Dictionary<MonoBehaviour, Tracked>();
    static readonly List<MonoBehaviour> targets = new List<MonoBehaviour>();
    static readonly RaycastHit[] wallHits = new RaycastHit[16];
    static int trackedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        tracked.Clear();
        trackedFrame = -1;
    }

    // Measures how the targets move. Call every frame (both hands may; it runs
    // once per frame).
    public static void Track()
    {
        if (trackedFrame == Time.frameCount) return;
        trackedFrame = Time.frameCount;

        foreach (MonoBehaviour target in Targets())
        {
            Vector3 position = Middle(target);
            if (!tracked.TryGetValue(target, out Tracked t))
            {
                tracked[target] = new Tracked { position = position, time = Time.time };
                continue;
            }
            float dt = Time.time - t.time;
            if (dt <= 0f) continue;
            Vector3 raw = (position - t.position) / dt;
            t.velocity = Vector3.Lerp(t.velocity, raw, 1f - Mathf.Exp(-VelocitySmoothing * dt));
            t.position = position;
            t.time = Time.time;
        }
    }

    // The direction to cast along, from `origin`, for a spell aimed along
    // `direction`. speed: how fast the spell flies (m/s), 0 for one that
    // arrives at once. coneAngle: degrees off the aim a target can be and
    // still draw the spell. fullShare: share of that cone in which the spell
    // goes straight for the target. `bentToward` is the target aimed at, if any.
    public static Vector3 Adjust(Vector3 origin, Vector3 direction, float speed, float coneAngle, float fullShare,
                                 out MonoBehaviour bentToward)
    {
        bentToward = null;
        direction.Normalize();
        if (coneAngle <= 0f) return direction;

        float bestAngle = coneAngle;
        Vector3 bestPoint = default;
        foreach (MonoBehaviour target in Targets())
        {
            foreach (Collider c in target.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger || c.bounds.size.sqrMagnitude < 0.0001f) continue;
                if (c is MeshCollider mesh && !mesh.convex) continue; // ClosestPoint needs a convex shape

                // The point of this hitbox nearest the line the hand aimed along.
                Vector3 middle = c.bounds.center;
                float along = Vector3.Dot(middle - origin, direction);
                if (along <= 0f) continue; // behind the hand
                Vector3 nearest = c.ClosestPoint(origin + direction * along);
                if (Vector3.Angle(direction, nearest - origin) < 0.05f) return direction; // on target already

                Vector3 point = Vector3.Lerp(nearest, middle, TowardCenter);
                float angle = Vector3.Angle(direction, point - origin);
                if (angle >= bestAngle || Blocked(origin, point)) continue;
                bestAngle = angle;
                bestPoint = point + Lead(target, Vector3.Distance(origin, point), speed);
                bentToward = target;
            }
        }
        if (bentToward == null) return direction;

        float fullAngle = coneAngle * fullShare;
        float strength = bestAngle <= fullAngle ? 1f : 1f - (bestAngle - fullAngle) / (coneAngle - fullAngle);
        return Vector3.Slerp(direction, (bestPoint - origin).normalized, strength).normalized;
    }

    // Everything a spell can be aimed at: the living dragon and unbroken crystals.
    static List<MonoBehaviour> Targets()
    {
        targets.Clear();
        foreach (EnemyTarget enemy in Object.FindObjectsByType<EnemyTarget>())
            if (!enemy.IsDead) targets.Add(enemy);
        foreach (ElementCrystal crystal in Object.FindObjectsByType<ElementCrystal>())
            if (!crystal.Broken) targets.Add(crystal);
        return targets;
    }

    static Vector3 Middle(MonoBehaviour target)
    {
        if (target is EnemyTarget enemy && enemy.TryGetVisibleBounds(out Bounds b)) return b.center;
        return target.transform.position;
    }

    // How far a target moves while a spell of this speed flies this far.
    static Vector3 Lead(MonoBehaviour target, float distance, float speed)
    {
        if (speed <= 0f || !tracked.TryGetValue(target, out Tracked t)) return Vector3.zero;
        return t.velocity * Mathf.Min(distance / speed, MaxLeadTime);
    }

    // True when the scanned room stands between the hand and the point, and
    // spells don't pass through it.
    static bool Blocked(Vector3 origin, Vector3 point)
    {
        if (RoomWalls.SpellsPassThrough) return false;
        Vector3 to = point - origin;
        int count = Physics.RaycastNonAlloc(origin, to.normalized, wallHits, to.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (RoomWalls.IsRoom(wallHits[i].collider)) return true;
        return false;
    }
}
