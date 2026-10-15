using UnityEngine;

// Keeps a panel that stays up while the player casts out of the hands' way:
// beside them, to one side of the line they cast along (from them to the Cast
// Target), below eye level and turned to face them. It comes along when they
// walk off or the target ends up somewhere else, and otherwise holds still so
// its buttons can be pressed.
//
// The sandbox and tutorial menus add this to their panel themselves.
public class BesidePlayer : MonoBehaviour
{
    const float ReanchorDistance = 0.4f; // meters the player walks before the panel comes along
    const float ReanchorAngle = 45f;     // degrees the line to the cast target swings before it does
    const float GlideTime = 0.3f;        // roughly how long it takes to get there, in seconds

    // What the player casts at. The panel keeps to one side of the line to it.
    public Transform castTarget;
    // How far along that line the panel's center sits, in meters from the head.
    public float distance = 0.3f;
    // How far to the right of that line, in meters. Negative = to the left.
    public float side = -0.62f;
    // How far below eye level the panel's center sits, in meters.
    public float heightBelowEyes = 0.45f;

    Vector3 anchor;        // where the player's head was when the panel was last placed
    Vector3 castDirection; // level direction from there to the cast target
    Vector3 velocity;

    // Puts the panel on its spot at once, for when it first appears.
    public void PlaceNow() => Place(true);

    void LateUpdate() => Place(false);

    // The spot is worked out from where the player stood and which way the
    // cast target was at that moment, and only again once they have walked
    // off or the target is somewhere else -- so the buttons hold still.
    void Place(bool atOnce)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 head = cam.transform.position;

        // Toward the target; where the player is looking if there is none right now.
        bool haveTarget = castTarget != null && castTarget.gameObject.activeInHierarchy;
        Vector3 toTarget = haveTarget ? castTarget.position - head : cam.transform.forward;
        toTarget.y = 0f;
        bool haveDirection = toTarget.sqrMagnitude > 0.0001f;

        Vector3 walked = head - anchor;
        walked.y = 0f;
        bool targetMoved = haveDirection && haveTarget && Vector3.Angle(castDirection, toTarget) > ReanchorAngle;
        if (atOnce || walked.magnitude > ReanchorDistance || targetMoved)
        {
            anchor = head;
            if (haveDirection) castDirection = toTarget.normalized;
            else if (castDirection == Vector3.zero) castDirection = Vector3.forward;
        }

        Vector3 right = Vector3.Cross(Vector3.up, castDirection);
        Vector3 spot = anchor + castDirection * distance + right * side + Vector3.down * heightBelowEyes;
        // The panel's +Z points away from the viewer, which is how its text reads correctly.
        Quaternion facing = Quaternion.LookRotation((spot - anchor).normalized);

        if (atOnce)
        {
            transform.SetPositionAndRotation(spot, facing);
            velocity = Vector3.zero;
            return;
        }
        transform.position = Vector3.SmoothDamp(transform.position, spot, ref velocity, GlideTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-Time.deltaTime / (GlideTime * 0.5f)));
    }
}
