using UnityEngine;
using Photon.Pun;

/// <summary>
/// Placed on trigger colliders behind each goal line.
/// When the ball enters, fires a goal event via RPC to all clients.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class GoalDetector : MonoBehaviour
{
    [Tooltip("Which team's goal is this? 0 = MasterClient's goal (scoring for Guest), 1 = Guest's goal (scoring for MasterClient)")]
    public int goalOwnerTeam = 0; // 0 = Team A's goal, 1 = Team B's goal

    [Tooltip("Sign of the Z axis the goal opens toward (+1 = +Z goal, -1 = -Z goal).")]
    public float goalDirection = 1f;

    [Tooltip("Only count the goal when the ball is actually traveling into the net.")]
    public bool requireCrossing = true;

    private void OnTriggerEnter(Collider other)
    {
        // Only the MasterClient processes goal detection to avoid double-counting
        if (!PhotonNetwork.IsMasterClient) return;

        BallController ball = other.GetComponent<BallController>();
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

        // Determine which team scored (the opposite team of the goal owner)
        int scoringTeam = (goalOwnerTeam == 0) ? 1 : 0;

        // Notify all clients
        MatchManager.Instance.photonView.RPC(nameof(MatchManager.OnGoalScoredRPC), RpcTarget.All, scoringTeam);
    }
}
