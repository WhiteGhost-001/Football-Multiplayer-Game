using UnityEngine;

/// <summary>
/// Runtime script that replaces primitive cube goal posts with proper
/// FIFA-proportioned posts and procedural net meshes.
/// Attach to an empty GameObject or let GoalPostSetup.cs handle it.
/// </summary>
public class GoalPostBuilder : MonoBehaviour
{
    [Header("Goal Dimensions (unscaled, will be multiplied by parent scale)")]
    public float goalWidth = 6.0f;
    public float goalHeight = 2.9f;
    public float postRadius = 0.06f;
    public float netDepth = 2.2f;
    public float netSideDepth = 1.2f;

    [Header("Goal Z Positions (unscaled)")]
    public float goalZPositive = 14.89f;
    public float goalZNegative = -14.59f;

    [Header("Pitch Boundaries (unscaled half-extents, x parent scale = world)")]
    public float pitchHalfX = 30f;
    public float pitchHalfZ = 20f;

    void Start()
    {
        ReplaceGoalPosts();
    }

    public void ReplaceGoalPosts()
    {
        EnsurePitchBoundaries();

        // Always purge legacy cube posts so they can never linger alongside the
        // proper goals (works even when proper goals are already in the scene).
        DestroyOldGoalPosts();

        Transform envRoot = FindStadiumRoot();

        // If proper goals already exist but were baked under a stadium of a
        // different scale (the ids can linger after the stadium is resized),
        // drop them so they get rebuilt at the correct size below.
        if (GoalsAreMisScaled(envRoot))
        {
            Transform north = GameObject.Find("Goal_North")?.transform;
            Transform south = GameObject.Find("Goal_South")?.transform;
            if (north != null) DestroyImmediate(north.gameObject);
            if (south != null) DestroyImmediate(south.gameObject);
        }

        // If proper goals already exist, don't rebuild (avoids duplicates)
        if (GameObject.Find("Goal_North") != null && GameObject.Find("Goal_South") != null)
            return;

        // Find or create parent
        Transform parent = transform;
        if (envRoot != null) parent = envRoot;

        // Create both goals.
        // Goal structure faces +Z local (mouth open at local Z=0, net at local -Z).
        // North goal (+Z, field is to its -Z): rotate 180 so net points to +Z (outside).
        // South goal (-Z, field is to its +Z): identity so net points to -Z (outside).
        CreateGoal(parent, "Goal_North", new Vector3(0, 0, goalZPositive), Quaternion.Euler(0, 180, 0));
        CreateGoal(parent, "Goal_South", new Vector3(0, 0, goalZNegative), Quaternion.identity);

        Debug.Log("Goal posts replaced with proper FIFA-proportioned posts and nets.");
    }

    /// <summary>
    /// Locates the stadium root regardless of minor naming differences
    /// ("StadiumEnvironment", "StadiumEnvironment (1)", "Stadium_Environment"...).
    /// Falls back to the most likely candidate whose name starts with "Stadium".
    /// </summary>
    public static Transform FindStadiumRoot()
    {
        string[] preferred =
        {
            "StadiumEnvironment (1)",
            "StadiumEnvironment",
            "Stadium_Environment"
        };

        // Only root-level objects count: the scaled root is "StadiumEnvironment (1)",
        // while "Stadium_Environment" is usually a NESTED child and must never win.
        Transform best = null;
        int bestIndex = int.MaxValue;
        Transform prefix = null;
        int bestChildren = -1;

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go == null || go.transform.parent != null) continue; // root objects only

            int idx = System.Array.IndexOf(preferred, go.name);
            if (idx >= 0)
            {
                if (idx < bestIndex)
                {
                    bestIndex = idx;
                    best = go.transform;
                }
            }
            else if (go.name.StartsWith("Stadium", System.StringComparison.OrdinalIgnoreCase)
                     && go.transform.childCount > bestChildren)
            {
                bestChildren = go.transform.childCount;
                prefix = go.transform;
            }
        }

        return best != null ? best : prefix;
    }

    /// <summary>
    /// True when a baked goal's world scale no longer matches the current stadium
    /// scale, meaning its posts/nets are stretched the wrong size.
    /// </summary>
    private bool GoalsAreMisScaled(Transform envRoot)
    {
        Transform goal = GameObject.Find("Goal_North")?.transform;
        if (goal == null) return false;

        float expected = 1f;
        if (envRoot != null && !float.IsNaN(envRoot.lossyScale.x) && envRoot.lossyScale.x > 0.01f)
            expected = envRoot.lossyScale.x;

        float actual = goal.lossyScale.x;
        if (float.IsNaN(actual)) return false;
        return Mathf.Abs(actual - expected) > 0.25f;
    }

    void DestroyOldGoalPosts()
    {
        string[] oldNames = { "goal post", "goal post (1)" };
        foreach (string n in oldNames)
        {
            GameObject go = GameObject.Find(n);
            if (go != null) DestroyImmediate(go);
        }
    }

    // ── Pitch Boundaries ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds tall invisible walls around the green playing area so the ball
    /// always bounces back onto the field. Any boundaries baked into the scene
    /// at an older stadium scale are removed first, keeping the walls aligned
    /// whatever scale the stadium is set to.
    /// </summary>
    private void EnsurePitchBoundaries()
    {
#if UNITY_EDITOR
        // Only build physical boundaries at runtime; doing this in the editor
        // would leave unsaved scene objects behind.
        if (!Application.isPlaying) return;
#endif

        Transform envRoot = FindStadiumRoot();
        float scale = GetEnvironmentScale(envRoot);

        // Discard boundaries baked at a previous scale so they can't interfere.
        Transform baked = envRoot != null ? envRoot.Find("PitchBoundaries") : null;
        if (baked != null) Destroy(baked.gameObject);

        if (GameObject.Find("RuntimePitchBoundaries") != null) return;

        // The visible green pitch occupies X +-pitchHalfX and Z +-pitchHalfZ
        // (unscaled). Convert to world space for the current stadium scale.
        float halfW = pitchHalfX * scale;
        float halfL = pitchHalfZ * scale;

        // Tall enough that no lobbed shot can clear it.
        const float wallTop = 200f;
        const float wallBottom = -20f;
        const float wallThickness = 3f;
        const float margin = 1.5f;

        GameObject root = new GameObject("RuntimePitchBoundaries");
        root.transform.position = Vector3.zero;

        // Slight bounce so the ball rebounds onto the field instead of dying
        // against the boards.
        PhysicsMaterial bounce = new PhysicsMaterial("PitchBoundary_Bounce");
        bounce.bounciness = 0.55f;
        bounce.dynamicFriction = 0.05f;
        bounce.staticFriction = 0.05f;
        bounce.frictionCombine = PhysicsMaterialCombine.Minimum;
        bounce.bounceCombine = PhysicsMaterialCombine.Maximum;

        Vector3 center = new Vector3(0, (wallTop + wallBottom) / 2f, 0);

        CreateBoundaryWall(root.transform, "RuntimeWall_Top",
            new Vector3(center.x, center.y, halfL + margin),
            new Vector3(halfW * 2f + wallThickness * 2f, wallTop - wallBottom, wallThickness), bounce);
        CreateBoundaryWall(root.transform, "RuntimeWall_Bottom",
            new Vector3(center.x, center.y, -halfL - margin),
            new Vector3(halfW * 2f + wallThickness * 2f, wallTop - wallBottom, wallThickness), bounce);
        CreateBoundaryWall(root.transform, "RuntimeWall_Right",
            new Vector3(halfW + margin, center.y, center.z),
            new Vector3(wallThickness, wallTop - wallBottom, halfL * 2f), bounce);
        CreateBoundaryWall(root.transform, "RuntimeWall_Left",
            new Vector3(-halfW - margin, center.y, center.z),
            new Vector3(wallThickness, wallTop - wallBottom, halfL * 2f), bounce);
    }

    private static float GetEnvironmentScale(Transform envRoot)
    {
        if (envRoot == null) return 1f;
        float scale = Mathf.Abs(envRoot.lossyScale.y);
        if (scale <= 0f || float.IsInfinity(scale) || float.IsNaN(scale)) return 1f;
        return scale;
    }

    private static void CreateBoundaryWall(Transform parent, string name, Vector3 pos, Vector3 size, PhysicsMaterial mat)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.position = pos;

        BoxCollider col = wall.AddComponent<BoxCollider>();
        col.size = size;
        col.sharedMaterial = mat;
    }

    // ── Goal Construction ────────────────────────────────────────────────────

    void CreateGoal(Transform parent, string name, Vector3 pos, Quaternion rotation)
    {
        GameObject goal = new GameObject(name);
        goal.transform.SetParent(parent, false);
        goal.transform.localPosition = pos;
        goal.transform.localRotation = rotation;

        Material postMat = CreatePostMaterial();
        Material netMat = CreateNetMaterial();

        float halfW = goalWidth / 2f;
        float postD = postRadius * 2f;

        // ── Posts & Crossbar ──
        // Left post
        CreatePost(goal.transform, "LeftPost", new Vector3(-halfW, goalHeight / 2f, 0),
            new Vector3(postD, goalHeight, postD), postMat);

        // Right post
        CreatePost(goal.transform, "RightPost", new Vector3(halfW, goalHeight / 2f, 0),
            new Vector3(postD, goalHeight, postD), postMat);

        // Crossbar
        CreatePost(goal.transform, "Crossbar", new Vector3(0, goalHeight, 0),
            new Vector3(goalWidth + postD, postD, postD), postMat);

        // ── Back support bars ──
        // Top back bar
        CreatePost(goal.transform, "BackBar_Top", new Vector3(0, goalHeight, -netDepth),
            new Vector3(goalWidth + postD, postD * 0.6f, postD * 0.6f), postMat);

        // Left back bar (angled from top of post to back)
        Vector3 lbMid = new Vector3(-halfW, goalHeight * 0.7f, -netDepth * 0.5f);
        CreatePost(goal.transform, "BackBar_Left", lbMid,
            new Vector3(postD * 0.6f, postD * 0.6f, netDepth * 1.1f), postMat);

        // Right back bar
        Vector3 rbMid = new Vector3(halfW, goalHeight * 0.7f, -netDepth * 0.5f);
        CreatePost(goal.transform, "BackBar_Right", rbMid,
            new Vector3(postD * 0.6f, postD * 0.6f, netDepth * 1.1f), postMat);

        // ── Ground support bars ──
        // Left ground bar
        CreatePost(goal.transform, "GroundBar_Left", new Vector3(-halfW, postRadius, -netDepth / 2f),
            new Vector3(postD * 0.5f, postD * 0.5f, netDepth), postMat);

        // Right ground bar
        CreatePost(goal.transform, "GroundBar_Right", new Vector3(halfW, postRadius, -netDepth / 2f),
            new Vector3(postD * 0.5f, postD * 0.5f, netDepth), postMat);

        // Back ground bar
        CreatePost(goal.transform, "GroundBar_Back", new Vector3(0, postRadius, -netDepth),
            new Vector3(goalWidth + postD, postD * 0.5f, postD * 0.5f), postMat);

        // ── Net Meshes ──
        // Back net panel
        CreateNetPanel(goal.transform, "Net_Back",
            new Vector3(0, goalHeight / 2f, -netDepth),
            new Vector2(goalWidth, goalHeight), netMat);

        // Top net panel (angled from crossbar to back)
        CreateNetPanel(goal.transform, "Net_Top",
            new Vector3(0, goalHeight, -netDepth / 2f),
            new Vector2(goalWidth, netDepth),
            netMat, Quaternion.Euler(90f, 0f, 0f));

        // Left side net
        CreateSideNet(goal.transform, "Net_Left",
            new Vector3(-halfW, goalHeight / 2f, -netDepth / 2f),
            new Vector2(netDepth, goalHeight), netMat, true);

        // Right side net
        CreateSideNet(goal.transform, "Net_Right",
            new Vector3(halfW, goalHeight / 2f, -netDepth / 2f),
            new Vector2(netDepth, goalHeight), netMat, false);
    }

    // ── Post Creation ────────────────────────────────────────────────────────

    void CreatePost(Transform parent, string name, Vector3 localPos, Vector3 boxSize, Material mat)
    {
        // Build the post as a round cylinder along its longest axis so it reads
        // as a real goalpost tube instead of a flat cube.
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = name;
        post.transform.SetParent(parent, false);
        post.transform.localPosition = localPos;

        float len = Mathf.Max(boxSize.x, Mathf.Max(boxSize.y, boxSize.z));
        float diam = Mathf.Max(0.01f, Mathf.Min(boxSize.x, Mathf.Min(boxSize.y, boxSize.z)));

        Vector3 euler = Vector3.zero;
        if (Mathf.Approximately(len, boxSize.x))
            euler = new Vector3(0f, 0f, 90f);    // length along X
        else if (Mathf.Approximately(len, boxSize.z))
            euler = new Vector3(90f, 0f, 0f);    // length along Z

        post.transform.localEulerAngles = euler;
        post.transform.localScale = new Vector3(diam, len / 2f, diam);
        post.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Keep colliders on main posts so ball can bounce off them
        if (name != "LeftPost" && name != "RightPost" && name != "Crossbar")
        {
            Destroy(post.GetComponent<Collider>());
        }
    }

    // ── Net Panels ───────────────────────────────────────────────────────────

    void CreateNetPanel(Transform parent, string name, Vector3 localPos, Vector2 size, Material mat, Quaternion? extraRot = null)
    {
        GameObject net = new GameObject(name);
        net.transform.SetParent(parent, false);
        net.transform.localPosition = localPos;
        if (extraRot.HasValue) net.transform.localRotation = extraRot.Value;

        MeshFilter mf = net.AddComponent<MeshFilter>();
        MeshRenderer mr = net.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        int cols = Mathf.CeilToInt(size.x / 0.3f);
        int rows = Mathf.CeilToInt(size.y / 0.3f);

        mf.sharedMesh = CreateNetMesh(cols, rows, size.x, size.y);
    }

    void CreateSideNet(Transform parent, string name, Vector3 localPos, Vector2 size, Material mat, bool isLeft)
    {
        GameObject net = new GameObject(name);
        net.transform.SetParent(parent, false);
        net.transform.localPosition = localPos;

        // Side nets taper from full height at the post to shorter at the back
        MeshFilter mf = net.AddComponent<MeshFilter>();
        MeshRenderer mr = net.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        int cols = Mathf.CeilToInt(size.x / 0.3f);
        int rows = Mathf.CeilToInt(size.y / 0.3f);

        Mesh mesh = new Mesh();
        mesh.name = name + "_Mesh";

        int vertCount = (cols + 1) * (rows + 1);
        Vector3[] verts = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] tris = new int[cols * rows * 6];

        float halfH = size.y / 2f;
        float xSign = isLeft ? -1f : 1f;

        for (int r = 0; r <= rows; r++)
        {
            float t = (float)r / rows; // 0 at post, 1 at back
            float z = (t - 0.5f) * size.x;
            // Taper: height reduces toward the back
            float heightScale = Mathf.Lerp(1f, 0.4f, t);
            float currentHalfH = halfH * heightScale;

            for (int c = 0; c <= cols; c++)
            {
                float s = (float)c / cols; // 0 at bottom, 1 at top
                float y = Mathf.Lerp(-currentHalfH, currentHalfH, s);
                float x = xSign * netSideDepth * t * 0.3f; // slight inward lean

                int idx = r * (cols + 1) + c;
                verts[idx] = new Vector3(x, y, z);
                uvs[idx] = new Vector2((float)c / cols, (float)r / rows);
            }
        }

        int ti = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int bl = r * (cols + 1) + c;
                int br = bl + 1;
                int tl = bl + cols + 1;
                int tr = tl + 1;

                tris[ti++] = bl;
                tris[ti++] = tl;
                tris[ti++] = br;

                tris[ti++] = br;
                tris[ti++] = tl;
                tris[ti++] = tr;
            }
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;
    }

    // ── Net Mesh Generator ───────────────────────────────────────────────────

    Mesh CreateNetMesh(int cols, int rows, float width, float height)
    {
        Mesh mesh = new Mesh();
        mesh.name = "NetMesh";

        int vertCount = (cols + 1) * (rows + 1);
        Vector3[] verts = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] tris = new int[cols * rows * 6];

        float halfW = width / 2f;
        float halfH = height / 2f;

        for (int r = 0; r <= rows; r++)
        {
            for (int c = 0; c <= cols; c++)
            {
                float x = Mathf.Lerp(-halfW, halfW, (float)c / cols);
                float y = Mathf.Lerp(-halfH, halfH, (float)r / rows);
                int idx = r * (cols + 1) + c;
                verts[idx] = new Vector3(x, y, 0);
                uvs[idx] = new Vector2((float)c / cols, (float)r / rows);
            }
        }

        int ti = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int bl = r * (cols + 1) + c;
                int br = bl + 1;
                int tl = bl + cols + 1;
                int tr = tl + 1;

                tris[ti++] = bl;
                tris[ti++] = tl;
                tris[ti++] = br;

                tris[ti++] = br;
                tris[ti++] = tl;
                tris[ti++] = tr;
            }
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        return mesh;
    }

    // ── Materials ────────────────────────────────────────────────────────────

    Material CreatePostMaterial()
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.name = "GoalPost_White";
        mat.color = new Color(0.95f, 0.95f, 0.97f); // bright white with slight cool tint
        mat.SetFloat("_Glossiness", 0.7f);
        mat.SetFloat("_Metallic", 0.3f);
        return mat;
    }

    Material CreateNetMaterial()
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.name = "GoalNet_Mesh";
        mat.color = Color.white;
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.SetFloat("_Glossiness", 0.1f);
        mat.SetFloat("_Metallic", 0f);

        // Procedural netting texture: translucent cells with light grid lines,
        // so the panels look like actual mesh rather than a flat white sheet.
        int res = 64;
        Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tex.name = "GoalNet_Grid";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;

        int line = 3;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color lineCol = new Color(0.85f, 0.88f, 0.92f, 0.9f);
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                bool isLine = x < line || x >= res - line || y < line || y >= res - line;
                tex.SetPixel(x, y, isLine ? lineCol : clear);
            }
        }
        tex.Apply();

        mat.SetTexture("_MainTex", tex);
        mat.SetTextureScale("_MainTex", new Vector2(16f, 10f));
        return mat;
    }
}
