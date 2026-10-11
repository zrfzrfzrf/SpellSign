using UnityEngine;

// Keeps a menu panel within the player's reach. The panel stays put, so its
// buttons hold still to be pressed, until the player has walked off or turned
// well to the side; then it glides to the spot in front of them and stays
// there. The menus add this to their panel themselves.
public class MenuFollow : MonoBehaviour
{
    const float FollowDistance = 1.1f; // meters from the head before the panel moves
    const float FollowAngle = 70f;     // degrees off to the side before it does
    const float GlideTime = 0.3f;      // roughly how long the glide takes, in seconds
    const float Arrived = 0.01f;       // meters from the spot that counts as there

    // Same meaning as on the menu that owns the panel.
    public float distance = 0.65f;
    public float heightBelowEyes = 0.2f;

    bool gliding;
    Vector3 spot;
    Quaternion facing;
    Vector3 velocity;

    // A panel that is shown again starts wherever its menu just placed it.
    void OnEnable()
    {
        gliding = false;
        velocity = Vector3.zero;
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        if (!gliding)
        {
            Vector3 toPanel = transform.position - cam.transform.position;
            toPanel.y = 0f;
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;

            if (toPanel.magnitude <= FollowDistance && Vector3.Angle(forward, toPanel) <= FollowAngle) return;

            // Fixed for the whole glide: chasing every small head movement
            // would keep the buttons drifting under the player's finger.
            if (!MenuParts.SpotInFrontOfPlayer(distance, heightBelowEyes, out spot, out facing)) return;
            gliding = true;
        }

        transform.position = Vector3.SmoothDamp(transform.position, spot, ref velocity, GlideTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-Time.deltaTime / (GlideTime * 0.5f)));

        if ((transform.position - spot).sqrMagnitude <= Arrived * Arrived)
        {
            transform.SetPositionAndRotation(spot, facing);
            velocity = Vector3.zero;
            gliding = false;
        }
    }
}
