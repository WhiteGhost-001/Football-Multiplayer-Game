using UnityEngine;

/// <summary>
/// Offline twin of BallController (no Photon). Lives on the plain physics ball
/// named "Ball" in the training scene. Receives kicks directly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class OfflineBall : MonoBehaviour
{
    public static OfflineBall Instance { get; private set; }

    [Tooltip("Drag applied to the ball per second (slows rolling over time).")]
    public float linearDrag = 0.5f;

    [Tooltip("Angular drag applied to the ball per second.")]
    public float angularDrag = 0.5f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping = linearDrag;
        rb.angularDamping = angularDrag;

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ReceiveKick(Vector3 force)
    {
        rb.isKinematic = false;
        rb.AddForce(force, ForceMode.Impulse);
    }

    /// <summary>
    /// Brings the ball back to the centre spot for kickoff.
    /// </summary>
    public void ResetBallForKickoff(Vector3 centrePos)
    {
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = centrePos;
        rb.rotation = Quaternion.identity;
    }

    public Rigidbody GetRigidbody()
    {
        return rb;
    }
}