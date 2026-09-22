using UnityEngine;
using System.Collections;

/// <summary>
/// Single-player offline twin of NetworkPlayerController (no Photon).
/// One blue player at kickoff (0,1,-8) facing +Z, exactly like the online
/// MasterClient. Space shoots in the direction the player faces, RMB passes,
/// E lunges into a tackle.
/// </summary>
public class OfflinePlayerController : MonoBehaviour
{
    [Header("Movement & Sprint")]
    public float speed = 10f;
    public float sprintMultiplier = 1.6f;
    public float maxStamina = 100f;
    public float staminaDrainRate = 25f;
    public float staminaRegenRate = 15f;
    private float currentStamina;

    [Header("Shooting")]
    public float shootProximityRadius = 1.5f;
    public float shootForce = 15f;

    [Header("Passing")]
    public float passForce = 8f;

    [Header("Tackling")]
    public float tackleCooldown = 1.5f;
    public float tackleDuration = 0.2f;
    public float lungeSpeed = 25f;
    public float tackleHitRadius = 1.5f;

    [Header("Team Color")]
    public Color teamBlueColor = new Color(0.15f, 0.35f, 0.85f);

    [Header("Mouse Look")]
    public float mouseLookSensitivity = 2.5f;

    private Rigidbody rb;
    private OfflineBall nearbyBall;
    private Animator animator;

    private readonly Collider[] overlapResults = new Collider[8];

    private float lastTackleTime = -10f;
    private float tackleTimer = 0f;
    private bool isTackling = false;

    [Header("Tutorial")]
    public bool inputLocked = false;
    public bool HasMoved { get; private set; }
    public bool HasSprinted { get; private set; }
    public bool HasShot { get; private set; }
    public bool HasPassed { get; private set; }
    public bool HasTackled { get; private set; }

    public Vector3 KickoffPosition => new Vector3(0f, 1f, -8f);

    public void LockInput()
    {
        inputLocked = true;
        if (animator != null) animator.SetFloat("Speed", 0f);
    }

    public void UnlockInput()
    {
        inputLocked = false;
    }

    public void ResetActionFlags()
    {
        HasMoved = false;
        HasSprinted = false;
        HasShot = false;
        HasPassed = false;
        HasTackled = false;
    }

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        currentStamina = maxStamina;

        ApplyTeamColor();

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

    private void ApplyTeamColor()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer rend in renderers)
        {
            Material[] mats = rend.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                mats[i] = new Material(mats[i]);
                mats[i].color = teamBlueColor;
            }
            rend.materials = mats;
        }
    }

    private void Update()
    {
        if (Time.timeScale <= 0f) return; // paused via ESC menu

        // Don't allow input during goal freeze
        if (OfflineMatchController.Instance != null && OfflineMatchController.Instance.IsFrozen)
        {
            if (animator != null) animator.SetFloat("Speed", 0f);
            return;
        }

        // Tutorial message is on screen: block all player input.
        if (inputLocked)
        {
            if (animator != null) animator.SetFloat("Speed", 0f);
            return;
        }

        DetectNearbyBall();

        HandleMovementAndSprint();
        HandleShootInput();
        HandlePassInput();
        HandleTackleInput();

        if (OfflineMatchController.Instance != null)
        {
            OfflineMatchController.Instance.SetStamina(currentStamina, maxStamina);
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

        if (move.magnitude > 0.01f) HasMoved = true;
        if (isSprinting) HasSprinted = true;

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

        if (animator != null)
        {
            float animSpeed = isSprinting ? 1.5f : (move.magnitude > 0 ? 1f : 0f);
            animator.SetFloat("Speed", animSpeed);
        }
    }

    /// <summary>
    /// Turns the player horizontally by how much the mouse moved this frame.
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
            OfflineBall ball = overlapResults[i].GetComponent<OfflineBall>();
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

        // Shoot along the direction the player is facing so the player can aim
        // at either goal while dribbling.
        Vector3 shootDir = (transform.forward + Vector3.up * 0.2f).normalized;

        nearbyBall.ReceiveKick(shootDir * shootForce);
        HasShot = true;

        if (OfflineMatchController.Instance != null)
            OfflineMatchController.Instance.ShowShootFeedback();
    }

    private void HandlePassInput()
    {
        if (nearbyBall == null) return;

        if (!Input.GetMouseButtonDown(1)) return;

        Vector3 passDir = (transform.forward + Vector3.up * 0.1f).normalized;
        nearbyBall.ReceiveKick(passDir * passForce);
        HasPassed = true;
    }

    private void HandleTackleInput()
    {
        if (Input.GetKeyDown(KeyCode.E) && Time.time >= lastTackleTime + tackleCooldown)
        {
            isTackling = true;
            tackleTimer = tackleDuration;
            lastTackleTime = Time.time;
            HasTackled = true;

            if (OfflineMatchController.Instance != null)
                OfflineMatchController.Instance.ShowTackleFeedback();
        }
    }

    /// <summary>
    /// Puts the player back at the blue kickoff position, used after a goal.
    /// </summary>
    public void ResetForKickoff()
    {
        Rigidbody body = rb != null ? rb : GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        transform.SetPositionAndRotation(KickoffPosition, Quaternion.LookRotation(Vector3.forward));
    }
}