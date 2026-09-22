using UnityEngine;

/// <summary>
/// Smart AI Goalkeeper that tracks the ball position, moves to intercept,
/// and dives/lunges when the ball is close and moving fast.
/// </summary>
public class AIGoalkeeper : MonoBehaviour
{
    [Header("Patrol")]
    public float leftLimit = -3.5f;
    public float rightLimit = 3.5f;
    public float speed = 4f;

    [Header("Ball Tracking")]
    public float trackingSpeed = 6f;
    public float diveSpeed = 12f;
    public float diveDistance = 2.5f;
    public float diveCooldown = 2f;
    public float reactionDistance = 12f;  // How far away the ball needs to be before keeper reacts

    [Header("Goal Line")]
    public float goalLineZ = 0f; // Set this to the keeper's Z position in the inspector

    [Header("Visual")]
    public string visualPrefabPath = "Floreswa/male01_1";
    public Color kitColor = new Color(0.65f, 0.95f, 0.2f);
    public float visualYaw = 0f;

    private Transform ballTransform;
    private Rigidbody ballRb;
    private float lastDiveTime = -10f;
    private bool isDiving = false;
    private Vector3 diveTarget;
    private float diveTimer = 0f;
    private Vector3 homePosition;

    private void Start()
    {
        MakeColliderBouncy();
        Reconfigure();
        Invoke(nameof(Reconfigure), 0.5f); // Re-run once goals are rebuilt at startup
    }

    /// <summary>
    /// Re-reads the (possibly just rebuilt) goal and re-sizes/regrounds the
    /// keeper so both keepers always match the final goal state, regardless of
    /// object start order at scene load.
    /// </summary>
    public void Reconfigure()
    {
        homePosition = transform.position;
        goalLineZ = transform.position.z;
        BuildPlayerVisual();
        AdaptPatrolToGoal();
    }

    /// <summary>
    /// Gives the keeper's body collider a bounce so the ball rebounds back on
    /// contact instead of just stopping dead.
    /// </summary>
    private void MakeColliderBouncy()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        PhysicsMaterial mat = new PhysicsMaterial("Keeper_Bounce");
        mat.bounciness = 0.6f;
        mat.dynamicFriction = 0.1f;
        mat.staticFriction = 0.1f;
        mat.bounceCombine = PhysicsMaterialCombine.Maximum;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        col.sharedMaterial = mat;
    }

    private float GetStadiumScale()
    {
        Transform root = transform;
        while (root.parent != null) root = root.parent;

        float scale = root.lossyScale.y;
        if (scale <= 0f || float.IsInfinity(scale) || float.IsNaN(scale)) scale = 1f;
        return scale;
    }

    private float GetGroundY()
    {
        GameObject pitch = GameObject.Find("Stadium Pitch");
        return pitch != null ? pitch.transform.position.y : 0f;
    }

    /// <summary>
    /// Widens the patrol area so it covers the actual (possibly scaled) goal
    /// mouth on this keeper's side of the pitch.
    /// </summary>
    private void AdaptPatrolToGoal()
    {
        Transform goal = FindNearestGoal();
        if (goal == null) return;

        Transform leftPost = goal.Find("LeftPost");
        Transform rightPost = goal.Find("RightPost");
        if (leftPost == null || rightPost == null) return;

        float postHalfWidth = Mathf.Abs(rightPost.position.x - leftPost.position.x) / 2f;
        float usable = Mathf.Max(0.5f, postHalfWidth - 0.4f);

        leftLimit = -usable;
        rightLimit = usable;
        reactionDistance = Mathf.Max(reactionDistance, postHalfWidth * 3f);
    }

    private Transform FindNearestGoal()
    {
        Transform nearest = null;
        float bestDist = float.MaxValue;

        foreach (string name in new[] { "Goal_North", "Goal_South" })
        {
            Transform goal = GameObject.Find(name)?.transform;
            if (goal == null) continue;

            float d = Mathf.Abs(goal.position.z - transform.position.z);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = goal;
            }
        }
        return nearest;
    }

    private void BuildPlayerVisual()
    {
        Renderer capsuleRenderer = GetComponent<Renderer>();
        GameObject prefab = Resources.Load<GameObject>(visualPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"Goalkeeper visual prefab '{visualPrefabPath}' not found - keeping capsule.");
            return;
        }

        GameObject model = transform.Find("GoalkeeperModel")?.gameObject;
        if (model == null)
        {
            model = Instantiate(prefab, transform);
            model.name = "GoalkeeperModel";
            TintModel(model);
        }

        // Step 1 - Cancel the keeper root's non-uniform local scale so the
        // model renders at a uniform world scale of 1.
        Vector3 lossy = transform.lossyScale;
        lossy.x = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
        lossy.y = Mathf.Max(0.0001f, Mathf.Abs(lossy.y));
        lossy.z = Mathf.Max(0.0001f, Mathf.Abs(lossy.z));

        Vector3 baseScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);
        model.transform.localRotation = Quaternion.Euler(0f, visualYaw, 0f);

        // Step 2 - Measure the model's natural height, then scale it so the
        // keeper is exactly as tall as the goal mouth (never taller than the
        // crossbar), regardless of the model base size or stadium scale.
        // Scale is always recomputed from baseScale so repeated calls stay
        // idempotent (no compounding growth).
        float naturalHeight = ComputeModelBounds(model).size.y;
        float targetHeight = GetGoalOpeningHeight();
        if (naturalHeight > 0.0001f && !float.IsInfinity(naturalHeight))
        {
            float scaleFactor = targetHeight / naturalHeight;
            model.transform.localScale = new Vector3(
                baseScale.x * scaleFactor,
                baseScale.y * scaleFactor,
                baseScale.z * scaleFactor);

            // Step 2b - Resize the keeper's invisible body collider to match the
            // visual so shots that hit any part of the keeper bounce back.
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.height = targetHeight / lossy.y;
                capsule.center = new Vector3(0f, (targetHeight / 2f) / lossy.y, 0f);
            }
        }
        else
        {
            model.transform.localScale = baseScale;
        }

        // Step 3 - Ground the model: place its feet exactly on the pitch
        // surface so the keeper doesn't float in mid-air under a scaled stadium.
        Bounds finalBounds = ComputeModelBounds(model);
        if (!float.IsInfinity(finalBounds.min.y))
        {
            model.transform.position += Vector3.up * (GetGroundY() - finalBounds.min.y);
        }

        if (capsuleRenderer != null) capsuleRenderer.enabled = false;
    }

    private void TintModel(GameObject model)
    {
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.materials;
            bool tinted = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                mats[i] = new Material(mats[i]);
                mats[i].color = kitColor;
                tinted = true;
            }
            if (tinted) r.materials = mats;
        }
    }

    private Bounds ComputeModelBounds(GameObject model)
    {
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    /// <summary>
    /// Height the keeper should be: the distance from the ground to the bottom
    /// of the crossbar on the nearest goal.
    /// </summary>
    private float GetGoalOpeningHeight()
    {
        Transform goal = FindNearestGoal();
        if (goal != null)
        {
            Transform crossbar = goal.Find("Crossbar");
            if (crossbar != null)
            {
                Collider bar = crossbar.GetComponent<Collider>();
                if (bar != null) return Mathf.Max(0.5f, bar.bounds.min.y);
                return Mathf.Max(0.5f, crossbar.position.y);
            }
        }
        return Mathf.Max(0.5f, 2.9f * GetStadiumScale());
    }

    private void Update()
    {
        // Find ball if we don't have it
        if (ballTransform == null)
        {
            BallController ball = FindAnyObjectByType<BallController>();
            if (ball != null)
            {
                ballTransform = ball.transform;
                ballRb = ball.GetComponent<Rigidbody>();
            }
            else
            {
                // Offline mode (Training scene): a plain physics ball named "Ball"
                GameObject offlineBall = GameObject.Find("Ball");
                if (offlineBall != null)
                {
                    ballTransform = offlineBall.transform;
                    ballRb = offlineBall.GetComponent<Rigidbody>();
                }
                else
                {
                    // No ball yet, just patrol
                    Patrol();
                    return;
                }
            }
        }

        if (isDiving)
        {
            HandleDive();
            return;
        }

        float distToBall = Vector3.Distance(transform.position, ballTransform.position);
        
        // Check if ball is coming toward this goal
        bool ballApproaching = IsBallApproaching();

        if (ballApproaching && distToBall < reactionDistance)
        {
            // Active tracking — move laterally to match the ball's X position
            TrackBall();

            // Dive if ball is very close and moving fast
            if (distToBall < diveDistance && ballRb != null && ballRb.linearVelocity.magnitude > 3f
                && Time.time > lastDiveTime + diveCooldown)
            {
                StartDive();
            }
        }
        else
        {
            // Ball is far away or moving away — return to patrol
            ReturnToHome();
        }
    }

    private bool IsBallApproaching()
    {
        if (ballRb == null) return false;
        
        // Check if the ball's Z velocity is heading toward our goal line
        float ballZVel = ballRb.linearVelocity.z;
        
        // If our goal is at positive Z, ball should have positive Z velocity
        // If our goal is at negative Z, ball should have negative Z velocity
        if (goalLineZ > 0)
            return ballZVel > 0.5f || Mathf.Abs(ballTransform.position.z - goalLineZ) < 6f;
        else
            return ballZVel < -0.5f || Mathf.Abs(ballTransform.position.z - goalLineZ) < 6f;
    }

    private void TrackBall()
    {
        // Move laterally (X axis) to match ball position, clamped to goal width
        float targetX = Mathf.Clamp(ballTransform.position.x, 
            homePosition.x + leftLimit, homePosition.x + rightLimit);
        
        Vector3 targetPos = new Vector3(targetX, transform.position.y, transform.position.z);
        transform.position = Vector3.MoveTowards(transform.position, targetPos, trackingSpeed * Time.deltaTime);

        // Face the ball
        Vector3 lookDir = ballTransform.position - transform.position;
        lookDir.y = 0;
        if (lookDir.magnitude > 0.1f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, 
                Quaternion.LookRotation(lookDir), Time.deltaTime * 8f);
        }
    }

    private void StartDive()
    {
        isDiving = true;
        lastDiveTime = Time.time;
        diveTimer = 0.5f;
        
        // Dive toward the ball's predicted position
        Vector3 predictedPos = ballTransform.position;
        if (ballRb != null)
        {
            predictedPos += ballRb.linearVelocity * 0.15f; // Predict 150ms ahead
        }
        
        diveTarget = new Vector3(
            Mathf.Clamp(predictedPos.x, homePosition.x + leftLimit - 1f, homePosition.x + rightLimit + 1f),
            transform.position.y,
            transform.position.z
        );
    }

    private void HandleDive()
    {
        diveTimer -= Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, diveTarget, diveSpeed * Time.deltaTime);

        if (diveTimer <= 0)
        {
            isDiving = false;
        }
    }

    private void ReturnToHome()
    {
        // Slowly drift back to home position
        Vector3 target = new Vector3(homePosition.x, transform.position.y, homePosition.z);
        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
    }

    private void Patrol()
    {
        // Simple fallback patrol if no ball exists
        float pingPong = Mathf.PingPong(Time.time * speed * 0.3f, rightLimit - leftLimit) + leftLimit;
        Vector3 pos = transform.position;
        pos.x = homePosition.x + pingPong;
        transform.position = pos;
    }
}