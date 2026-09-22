using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Single-player offline twin of NetworkLauncher.StartMatch (no Photon).
/// Attach to the Main Camera in the Training scene. Builds the goal posts,
/// goal triggers, HUD controller, the blue player, the physics ball, the
/// follow camera, and a few training props (cones + static dummies).
/// </summary>
public class TrainingSceneBootstrap : MonoBehaviour
{
    private static readonly Color TeamRed = new Color(0.85f, 0.2f, 0.2f);
    private static readonly Color ConeOrange = new Color(1f, 0.5f, 0.05f);

    /// <summary>
    /// When the Training scene is loaded additively just to borrow its stadium
    /// (HowToPlay), set this to true before loading so this bootstrap skips the
    /// full training setup (no player, ball, cones, dummies, HUD).
    /// </summary>
    public static bool SuppressStart = false;

    private bool suppressed;

    private void Awake()
    {
        // Consume the flag here — Awake runs synchronously during LoadScene,
        // before the HowToPlay code continues, guaranteeing the flag is always
        // cleared (so a later normal Training session starts) regardless of
        // whether this object even lives to run Start().
        suppressed = SuppressStart;
        SuppressStart = false;
    }

    private void Start()
    {
        if (suppressed) return;

        gameObject.AddComponent<PauseMenu>();

        EnsureGoalPosts();
        EnsureMatchController();
        SetupGoalDetectors();

        GameObject player = SpawnPlayer();
        SpawnBall();

        BuildCameraFollow(player.transform);

        SpawnTrainingProps();
    }

    // =========================================================================
    // GOAL POSTS + GOAL DETECTORS
    // =========================================================================

    private void EnsureGoalPosts()
    {
        GameObject host = GameObject.Find("GoalPostSetup");
        if (host == null)
        {
            host = new GameObject("GoalPostSetup");
        }
        GoalPostBuilder builder = host.GetComponent<GoalPostBuilder>();
        if (builder == null)
        {
            builder = host.AddComponent<GoalPostBuilder>();
        }
        builder.ReplaceGoalPosts();
    }

    private void EnsureMatchController()
    {
        if (OfflineMatchController.Instance != null) return;

        GameObject go = new GameObject("OfflineMatchController");
        go.AddComponent<OfflineMatchController>();
    }

    private void SetupGoalDetectors()
    {
        // Both goals count for the trainee; +Z is Goal_North, -Z is Goal_South.
        CreateGoalTriggerForGoal("Goal_North", "GoalZone_Training_North", 1f);
        CreateGoalTriggerForGoal("Goal_South", "GoalZone_Training_South", -1f);
    }

    private void CreateGoalTriggerForGoal(string goalName, string zoneName, float direction)
    {
        if (GameObject.Find(zoneName) != null) return;

        Transform goal = GameObject.Find(goalName)?.transform;
        if (goal != null)
        {
            Transform leftPost  = goal.Find("LeftPost");
            Transform rightPost = goal.Find("RightPost");
            Transform crossbar  = goal.Find("Crossbar");

            // Crossbar may be nested (e.g. under "top post") — search recursively.
            if (crossbar == null)
                crossbar = FindDeepChild(goal, "Crossbar");

            if (leftPost != null && rightPost != null)
            {
                float leftX     = leftPost.position.x;
                float rightX    = rightPost.position.x;
                float goalLineZ = leftPost.position.z;
                float width     = Mathf.Abs(rightX - leftX);
                float height    = crossbar != null
                    ? Mathf.Max(0.5f, crossbar.position.y)
                    : 2.9f * GetStadiumScale();

                Vector3 center = new Vector3(
                    (leftX + rightX) / 2f,
                    height / 2f,
                    goalLineZ + direction * 0.6f);
                CreateGoalTrigger(zoneName, center, new Vector3(width, height, 1.2f), direction);
                return;
            }
        }

        float scale    = GetStadiumScale();
        float fbWidth  = 6f * scale;
        float fbHeight = 2.9f * scale;
        float fbLine   = 14.7f * scale;
        CreateGoalTrigger(zoneName,
            new Vector3(0f, fbHeight / 2f, fbLine * direction + direction * 0.6f),
            new Vector3(fbWidth, fbHeight, 1.2f),
            direction);
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private float GetStadiumScale()
    {
        Transform envRoot = GoalPostBuilder.FindStadiumRoot();
        if (envRoot == null) return 1f;
        float scale = Mathf.Abs(envRoot.lossyScale.y);
        if (scale <= 0f || float.IsInfinity(scale) || float.IsNaN(scale)) return 1f;
        return scale;
    }

    private void CreateGoalTrigger(string name, Vector3 pos, Vector3 size, float direction)
    {
        if (GameObject.Find(name) != null) return;

        GameObject zone = new GameObject(name);
        zone.transform.position = pos;

        BoxCollider col = zone.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = size;

        OfflineGoalDetector detector = zone.AddComponent<OfflineGoalDetector>();
        detector.goalDirection = direction;
        detector.requireCrossing = true;
    }

    // =========================================================================
    // PLAYER
    // =========================================================================

    private GameObject SpawnPlayer()
    {
        GameObject player = new GameObject("TrainingPlayer");
        player.transform.SetPositionAndRotation(
            new Vector3(0f, 1f, -8f), Quaternion.LookRotation(Vector3.forward));

        CapsuleCollider col = player.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 2f;
        col.direction = 1;
        col.center = Vector3.zero;

        Rigidbody rb = player.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationY
                       | RigidbodyConstraints.FreezeRotationZ;

        player.AddComponent<OfflinePlayerController>();

        // Visual + animator, mirrors the NetworkPlayer prefab structure
        // (child model "PlayerVisuals" at 0.9 scale, offset -1 on Y).
        GameObject modelPrefab = Resources.Load<GameObject>("Floreswa/male01_1");
        if (modelPrefab != null)
        {
            GameObject model = Instantiate(modelPrefab, player.transform);
            model.name = "PlayerVisuals";
            model.transform.localPosition = new Vector3(0f, -1f, 0f);
            model.transform.localScale = Vector3.one * 0.9f;

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            RuntimeAnimatorController controller =
                Resources.Load<RuntimeAnimatorController>("PlayerAnimatorController");
            if (controller != null) animator.runtimeAnimatorController = controller;
        }

        return player;
    }

    // =========================================================================
    // BALL
    // =========================================================================

    private void SpawnBall()
    {
        if (GameObject.Find("Ball") != null) return; // AIGoalkeeper expects this exact name

        GameObject ball = new GameObject("Ball");
        ball.transform.position = new Vector3(0f, 0.5f, 0f);
        ball.transform.localScale = Vector3.one * 1.8f;

        // Copy the mesh + material straight from the read-only SoccerBall prefab
        // (never instantiate it — that one carries Photon components).
        GameObject sourcePrefab = Resources.Load<GameObject>("SoccerBall_01");
        if (sourcePrefab != null)
        {
            MeshFilter sourceMesh = sourcePrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer sourceRenderer = sourcePrefab.GetComponentInChildren<MeshRenderer>();

            MeshFilter mf = ball.AddComponent<MeshFilter>();
            if (sourceMesh != null) mf.sharedMesh = sourceMesh.sharedMesh;

            MeshRenderer mr = ball.AddComponent<MeshRenderer>();
            if (sourceRenderer != null) mr.sharedMaterial = sourceRenderer.sharedMaterial;
        }
        else
        {
            ball.AddComponent<MeshRenderer>();
        }

        SphereCollider col = ball.AddComponent<SphereCollider>();
        col.radius = 0.10357375f;

        Rigidbody rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.45f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        ball.AddComponent<OfflineBall>();
    }

    // =========================================================================
    // CAMERA
    // =========================================================================

    private void BuildCameraFollow(Transform target)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        ThirdPersonCameraFollow follow = cam.GetComponent<ThirdPersonCameraFollow>();
        if (follow == null) follow = cam.gameObject.AddComponent<ThirdPersonCameraFollow>();
        follow.target = target;
        follow.SnapToTarget();
    }

    // =========================================================================
    // TRAINING PROPS (CONES + DUMMIES)
    // =========================================================================

    private void SpawnTrainingProps()
    {
        // Slalom cones spread across the pitch (world pitch is
        // roughly x[-30,30], z[-60,60]).
        SpawnCone(new Vector3( 5f, 0.6f, 10f));
        SpawnCone(new Vector3(-6f, 0.6f, 18f));
        SpawnCone(new Vector3( 7f, 0.6f, 26f));
        SpawnCone(new Vector3(-5f, 0.6f, 34f));
        SpawnCone(new Vector3( 8f, 0.6f, 42f));
        SpawnCone(new Vector3(-7f, 0.6f, 48f));

        // Static blocking dummies (red kits) in the attacking third.
        SpawnDummy(new Vector3( 12f, 1f, 32f));
        SpawnDummy(new Vector3(-10f, 1f, 32f));
        SpawnDummy(new Vector3( 0f, 1f, 40f));
        SpawnDummy(new Vector3( 9f, 1f, 48f));
        SpawnDummy(new Vector3(-9f, 1f, 52f));
    }

    private void SpawnCone(Vector3 pos)
    {
        GameObject cone = new GameObject("TrainingCone");
        cone.transform.position = pos;
        cone.transform.rotation = Quaternion.identity;

        MeshRenderer rend = cone.AddComponent<MeshRenderer>();
        rend.sharedMaterial = new Material(Shader.Find("Standard"))
        {
            color = ConeOrange
        };

        MeshFilter filter = cone.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildConeMesh(height: 1.2f, radius: 0.45f);

        // Static blocker so the player and ball must weave around it.
        CapsuleCollider col = cone.AddComponent<CapsuleCollider>();
        col.radius = 0.4f;
        col.height = 1.1f;
        col.direction = 1;
        col.center = new Vector3(0f, 0.55f, 0f);
    }

    private void SpawnDummy(Vector3 pos)
    {
        GameObject root = new GameObject("TrainingDummy");
        root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.back));

        // Body collider: static, same proportions as the player capsule.
        CapsuleCollider col = root.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 2f;
        col.direction = 1;
        col.center = Vector3.zero;

        GameObject modelPrefab = Resources.Load<GameObject>("Floreswa/male01_1");
        if (modelPrefab == null) return;

        GameObject model = Instantiate(modelPrefab, root.transform);
        model.transform.localPosition = new Vector3(0f, -1f, 0f);
        model.transform.localScale = Vector3.one * 0.9f;
        TintModel(model, TeamRed);
    }

    private static void TintModel(GameObject model, Color color)
    {
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.materials;
            bool tinted = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                mats[i] = new Material(mats[i]);
                mats[i].color = color;
                tinted = true;
            }
            if (tinted) r.materials = mats;
        }
    }

    /// <summary>
    /// Builds a simple closed cone pointing up, feet at its local origin.
    /// </summary>
    private static Mesh BuildConeMesh(float height, float radius, int segments = 20)
    {
        Mesh mesh = new Mesh();
        mesh.name = "TrainingConeMesh";

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector3> normals = new List<Vector3>();

        int apex = 0;
        verts.Add(new Vector3(0f, height, 0f));
        normals.Add(Vector3.up);

        int baseCenter = 1 + segments;
        verts.Add(new Vector3(0f, 0f, 0f));
        normals.Add(Vector3.down);

        for (int i = 0; i < segments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segments;
            verts.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            normals.Add(new Vector3(Mathf.Cos(angle) * 0.5f, 0.75f, Mathf.Sin(angle) * 0.5f));
        }

        for (int i = 0; i < segments; i++)
        {
            int cur  = 2 + i;
            int next = 2 + ((i + 1) % segments);
            tris.Add(apex);
            tris.Add(cur);
            tris.Add(next);
        }

        for (int i = 0; i < segments; i++)
        {
            int cur  = 2 + i;
            int next = 2 + ((i + 1) % segments);
            tris.Add(baseCenter);
            tris.Add(next);
            tris.Add(cur);
        }

        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.normals = normals.ToArray();
        return mesh;
    }
}