using UnityEngine;

// This forcibly disables hand ray pointers
// Drag in every "HandRayInteractor" GameObject (both hands) from your
// Hierarchy into the rayObjects list below.

public class DisableHandRays : MonoBehaviour
{
    [Tooltip("Drag every HandRayInteractor GameObject here (both left and right hand)")]
    public GameObject[] rayObjects;

    void Start()
    {
        DisableRays();
    }

    void DisableRays()
    {
        foreach (var obj in rayObjects)
        {
            if (obj != null)
                obj.SetActive(false);
        }
    }

    // LateUpdate runs AFTER every other GameObject's Update() this frame,
    // so it wins the race against whatever in the Interaction SDK rig is 
    // re-enabling these - Update() couldn't guarantee that since Unity
    // doesn't guarantee Update() execution order between scripts.
    // Checked every frame (not just once/sec) so there's no visible flicker.
    void LateUpdate()
    {
        foreach (var obj in rayObjects)
        {
            if (obj != null && obj.activeSelf)
                obj.SetActive(false);
        }
    }
}