using UnityEngine;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Controls the local player's movement, sprinting, shooting, passing, and tackling.
/// Also assigns team color based on MasterClient vs Guest.
/// </summary>
public class NetworkPlayerController : MonoBehaviourPun
{
    [Header("Movement & Sprint")]
    public float speed = 10f;
    public float sprintMultiplier = 1.6f;
    public float maxStamina = 100f;
    public float staminaDrainRate = 25f;
    public float staminaRegenRate = 15f;
    private float currentStamina;

    [Header("Shooting")]
    public float shootProximityRadius = 2f;
    public float shootForce = 18f;

    [Header("Passing")]
    public float passForce = 8f;

    [Header("Tackling")]
    public float tackleCooldown = 1.5f;
    public float tackleDuration = 0.2f;
    public float lungeSpeed = 25f;
    public float tackleHitRadius = 1.5f;
    public float tacklePushbackForce = 10f;

    [Header("Team Colors")]
    public Color teamBlueColor = new Color(0.15f, 0.35f, 0.85f);
    public Color teamRedColor  = new Color(0.85f, 0.2f, 0.2f);

    [Header("Mouse Look")]
    public float mouseLookSensitivity = 2.5f;

    private Rigidbody rb;
    private BallController nearbyBall;
    private Animator animator;

    private readonly Collider[] overlapResults = new Collider[8];

    private float lastTackleTime = -10f;
    private float tackleTimer = 0f;
    private bool isTackling = false;
    private bool isStunned = false;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        currentStamina = maxStamina;

        // Assign team color to the visual model
        ApplyTeamColor();

        // Attach camera only for the local player
        if (photonView.IsMine)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                ThirdPersonCameraFollow camFollow = mainCam.GetComponent<ThirdPersonCameraFollow>();
                if (camFollow == null)
                {
                    camFollow = mainCam.gameObject.AddComponent<ThirdPersonCameraFollow>();
                }
                
                camFollow.target = this.transform;
                camFollow.SnapToTarget();
            }
        }
    }

    private void ApplyTeamColor()
    {
        // MasterClient = Blue (Team A), Guest = Red (Team B)
        Color teamColor = photonView.Owner.IsMasterClient ? teamBlueColor : teamRedColor;

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            // Create a new material instance so we don't affect shared materials
            Material[] mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = new Material(mats[i]);
                mats[i].color = teamColor;
            }
            rend.materials = mats;
        }
    }

    private void Update()
    {
        if (!photonView.IsMine) return;
        if (Time.timeScale <= 0f) return; // paused via ESC menu

        // Don't allow input during goal freeze
        if (MatchManager.Instance != null && MatchManager.Instance.IsFrozen)
        {
            if (animator != null) animator.SetFloat("Speed", 0f);
            return;
        }

        DetectNearbyBall();

        if (isStunned) return;

        HandleMovementAndSprint();
        HandleShootInput();
        HandlePassInput();
        HandleTackleInput();

        // Update stamina bar on HUD
        if (MatchManager.Instance != null)
        {
            MatchManager.Instance.SetStamina(currentStamina, maxStamina);
        }
    }

    private void HandleMovementAndSprint()
    {
        // The character always faces wherever the mouse cursor points.
        ApplyMouseFacing();

        if (isTackling)
        {
            tackleTimer -= Time.deltaTime;
            rb.MovePosition(rb.position + transform.forward * lungeSpeed * Time.deltaTime);
            CheckTackleHit();

            if (tackleTimer <= 0)
            {
                isTackling = false;
            }
            return;
        }

        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        Vector3 move = new Vector3(moveX, 0f, moveZ);
        if (move.magnitude > 0.01f) move = move.normalized;

        bool wantsToSprint = Input.GetKey(KeyCode.LeftShift);
        bool isSprinting = wantsToSprint && currentStamina > 0 && move.magnitude > 0;

        if (isSprinting)
        {
            currentStamina -= staminaDrainRate * Time.deltaTime;
        }
        else
        {
            currentStamina += staminaRegenRate * Time.deltaTime;
        }
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);

        float currentSpeed = isSprinting ? speed * sprintMultiplier : speed;

        // Movement is relative to the facing direction, so W walks toward the cursor.
        Vector3 worldMove = transform.forward * move.z + transform.right * move.x;
        rb.MovePosition(rb.position + worldMove * currentSpeed * Time.deltaTime);

        // Update Animations
        if (animator != null)
        {
            float animSpeed = isSprinting ? 1.5f : (move.magnitude > 0 ? 1f : 0f);
            animator.SetFloat("Speed", animSpeed);
        }
    }

    /// <summary>
    /// Turns the player horizontally by how much the mouse moved this frame.
    /// The player only rotates while the cursor is actually moving and stops
    /// the moment the cursor stops.
    /// </summary>
    private void ApplyMouseFacing()
    {
        float mouseX = Input.GetAxis("Mouse X");
        if (Mathf.Abs(mouseX) < 0.001f) return;

        transform.Rotate(0f, mouseX * mouseLookSensitivity, 0f, Space.Self);
    }

    private void DetectNearbyBall()
    {
        nearbyBall = null;
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, shootProximityRadius, overlapResults);

        for (int i = 0; i < hitCount; i++)
        {
            BallController ball = overlapResults[i].GetComponent<BallController>();
            if (ball != null)
            {
                nearbyBall = ball;
                break;
            }
        }
    }

    private void HandleShootInput()
    {
        if (nearbyBall == null) return;
        if (!Input.GetKeyDown(KeyCode.Space)) return;

        // Goals are at the ends of the Z-axis. MasterClient shoots Forward (+Z), Guest shoots Back (-Z).
        Vector3 shootDir = PhotonNetwork.IsMasterClient ? Vector3.forward : Vector3.back;
        shootDir = (shootDir + Vector3.up * 0.2f).normalized;

        nearbyBall.ReceiveKick(shootDir * shootForce);

        if (MatchManager.Instance != null)
            MatchManager.Instance.ShowShootFeedback();
    }

    private void HandlePassInput()
    {
        if (nearbyBall == null) return;

        // Right mouse button for a softer directional pass
        if (!Input.GetMouseButtonDown(1)) return;

        // Pass in the direction the player is currently facing
        Vector3 passDir = (transform.forward + Vector3.up * 0.1f).normalized;
        nearbyBall.ReceiveKick(passDir * passForce);
    }

    private void HandleTackleInput()
    {
        if (Input.GetKeyDown(KeyCode.E) && Time.time >= lastTackleTime + tackleCooldown)
        {
            isTackling = true;
            tackleTimer = tackleDuration;
            lastTackleTime = Time.time;

            if (MatchManager.Instance != null)
                MatchManager.Instance.ShowTackleFeedback();
        }
    }

    private void CheckTackleHit()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, tackleHitRadius, overlapResults);
        for (int i = 0; i < hitCount; i++)
        {
            NetworkPlayerController opponent = overlapResults[i].GetComponent<NetworkPlayerController>();
            
            if (opponent != null && opponent != this && !opponent.photonView.IsMine)
            {
                Vector3 pushDir = (opponent.transform.position - transform.position).normalized;
                pushDir.y = 0.5f;
                
                opponent.photonView.RPC(nameof(ReceiveTackle), RpcTarget.All, pushDir.normalized * tacklePushbackForce);

                if (nearbyBall != null)
                {
                    Vector3 stealDir = (transform.forward + Vector3.up * 0.2f).normalized;
                    nearbyBall.ReceiveKick(stealDir * 4f);
                }

                isTackling = false;
                break;
            }
        }
    }

    [PunRPC]
    public void ReceiveTackle(Vector3 force, PhotonMessageInfo info)
    {
        if (photonView.IsMine)
        {
            StartCoroutine(StunRoutine(force));
        }
    }

    private IEnumerator StunRoutine(Vector3 force)
    {
        isStunned = true;
        rb.linearVelocity = Vector3.zero;
        rb.AddForce(force, ForceMode.Impulse);
        yield return new WaitForSeconds(0.6f); 
        isStunned = false;
    }
}