using UnityEngine;

/// <summary>
/// Offline twin of GoalDetector (no Photon). Placed on trigger colliders
/// behind each goal line in the training scene. When the offline ball crosses
/// the goal mouth it reports a goal to the OfflineMatchController.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class OfflineGoalDetector : MonoBehaviour
{
    [Tooltip("Sign of the Z axis the goal opens toward (+1 = +Z goal, -1 = -Z goal).")]
    public float goalDirection = 1f;

    [Tooltip("Only count the goal when the ball is actually traveling into the net.")]
    public bool requireCrossing = true;

    private void OnTriggerEnter(Collider other)
    {
        OfflineBall ball = other.GetComponent<OfflineBall>();
        if (ball == null) return;

        // Ignore balls that are sitting behind the goal or moving away from it
        // (e.g. knocked back into the trigger from the net side).
        if (requireCrossing)
        {
            Rigidbody rb = other.attachedRigidbody;
            if (rb != null && Mathf.Abs(rb.linearVelocity.z) > 0.3f
                && Mathf.Sign(rb.linearVelocity.z) != Mathf.Sign(goalDirection))
            {
                return;
            }
        }

        if (OfflineMatchController.Instance != null)
        {
            OfflineMatchController.Instance.OnGoalScored();
        }
    }
}