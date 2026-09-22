using UnityEngine;
using Photon.Pun;
using Photon.Realtime;


[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PhotonView))]
public class BallController : MonoBehaviourPun, IPunOwnershipCallbacks
{
    public static BallController Instance { get; private set; }

    [Tooltip("Drag applied to the ball per second (slows rolling over time).")]
    public float linearDrag = 0.5f;

    [Tooltip("Angular drag applied to the ball per second.")]
    public float angularDrag = 0.5f;


    private Rigidbody rb;
    private Vector3   pendingKickForce;
    private bool      kickPending;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.linearDamping  = linearDrag;
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

    private void OnEnable()
    {
        PhotonNetwork.AddCallbackTarget(this);
    }

    private void OnDisable()
    {
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    private void Start()
    {
        SetKinematic(!photonView.IsMine);
    }

    public void ReceiveKick(Vector3 force)
    {
        if (photonView.IsMine)
        {
            ApplyKick(force);
        }
        else
        {
            pendingKickForce = force;
            kickPending      = true;
            photonView.RequestOwnership();
        }
    }

    /// <summary>
    /// Broadcast RPC that brings the ball to the centre spot for kickoff.
    /// Only the current owner performs the repositioning so the
    /// PhotonRigidbodyView syncs it to every client.
    /// </summary>
    [PunRPC]
    public void ResetBallForKickoff(Vector3 centrePos)
    {
        if (!photonView.IsMine) return;

        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = centrePos;
        rb.rotation = Quaternion.identity;
    }


    private void ApplyKick(Vector3 force)
    {
        SetKinematic(false);
        rb.AddForce(force, ForceMode.Impulse);
    }

    private void SetKinematic(bool kinematic)
    {
        rb.isKinematic = kinematic;
    }


    public void OnOwnershipRequest(PhotonView targetView, Player requestingPlayer)
    {
        // The ball is a shared item: any player who asks for it gets it, so
        // their kick/pass can be applied. Without this grant the request is
        // dropped and only the host (current owner) could ever kick/pass.
        if (targetView == photonView)
            targetView.TransferOwnership(requestingPlayer);
    }

    public void OnOwnershipTransfered(PhotonView targetView, Player previousOwner)
    {
        if (targetView != photonView) return;

        if (photonView.IsMine)
        {
            SetKinematic(false);

            if (kickPending)
            {
                kickPending = false;
                ApplyKick(pendingKickForce);
            }
        }
        else
        {
            kickPending = false;
            SetKinematic(true);
        }
    }

    public void OnOwnershipTransferFailed(PhotonView targetView, Player senderOfFailedRequest)
    {
        kickPending = false;
        Debug.LogWarning("[BallController] Ownership transfer failed.", this);
    }
}
