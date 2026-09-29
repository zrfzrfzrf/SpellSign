using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SpinWhileFlying : MonoBehaviour
{
    [Tooltip("How fast it spins around its own forward axis, in degrees per second")]
    public float spinSpeed = 360f;

    void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        // Spin around the object's own forward (Z) axis, using physics so it
        // doesn't fight with the Rigidbody that's already driving its flight.
        rb.angularVelocity = transform.forward * (spinSpeed * Mathf.Deg2Rad);
    }
}