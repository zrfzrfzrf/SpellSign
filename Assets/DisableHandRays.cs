using UnityEngine;

// Attach this to any GameObject in your scene (e.g. your GestureDetector).
// It forcibly disables hand ray pointers shortly after the app starts,
// which is useful when something else in the Interaction SDK rig keeps
// re-enabling them even after you've disabled them manually in the Editor.
//
// Drag in every "HandRayInteractor" GameObject (both hands) from your
// Hierarchy into the rayObjects list below.

public class DisableHandRays : MonoBehaviour
{
    [Tooltip("Drag every HandRayInteractor GameObject here (both left and right hand)")]
    public GameObject[] rayObjects;

    void Start()
    {
        // A short delay ensures this runs AFTER whatever system in the
        // Interaction SDK rig might try to enable these on startup.
        Invoke(nameof(DisableRays), 0.5f);
    }

    void DisableRays()
    {
        foreach (var obj in rayObjects)
        {
            if (obj != null)
                obj.SetActive(false);
        }
    }

    // Also keep re-checking every second, in case something keeps
    // turning them back on repeatedly rather than just once at startup.
    void Update()
    {
        if (Time.frameCount % 60 == 0) // roughly once a second
        {
            foreach (var obj in rayObjects)
            {
                if (obj != null && obj.activeSelf)
                    obj.SetActive(false);
            }
        }
    }
}
