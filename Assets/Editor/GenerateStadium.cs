using UnityEngine;
using UnityEditor;

public class GenerateStadium : MonoBehaviour
{
    [MenuItem("Tools/Generate Stadium")]
    public static void Generate()
    {
        // Remove old stadium objects if they exist
        CleanupOldObjects();

        // Root for all stadium environment
        GameObject envRoot = new GameObject("StadiumEnvironment");

        // 1. Pitch
        CreatePitch(envRoot.transform);

        // 2. Pitch Markings
        CreatePitchMarkings(envRoot.transform);

        // 3. Fences
        CreateFences(envRoot.transform);

        // 4. Tiered Stands with Crowd
        CreateStands(envRoot.transform);

        // 5. Advertising Boards
        CreateAdBoards(envRoot.transform);

        // 6. Corner Flags
        CreateCornerFlags(envRoot.transform);

        // 7. Audio
        CreateAudio(envRoot.transform);

        // 8. Lighting
        CreateLighting(envRoot.transform);

        Debug.Log("Stadium generated! Run 'Tools/Scale Stadium 1.75x' next if needed.");
    }

    static void CleanupOldObjects()
    {
        string[] names = { "Stadium Pitch", "Pitch Markings", "Perimeter Fences",
            "Crowd Stands", "Corner Flags", "StadiumEnvironment", "Stadium Sun",
            "Pitch Reflection Probe", "Ad Boards", "CrowdAudioController" };
        foreach (string n in names)
        {
            GameObject go = GameObject.Find(n);
            if (go != null) DestroyImmediate(go);
        }
    }

    // ── Pitch ────────────────────────────────────────────────────────────────
    static void CreatePitch(Transform parent)
    {
        GameObject pitch = GameObject.CreatePrimitive(PrimitiveType.Plane);
        pitch.name = "Stadium Pitch";
        pitch.transform.SetParent(parent, false);
        pitch.transform.localPosition = Vector3.zero;
        pitch.transform.localScale = new Vector3(6f, 1f, 4f); // 60 x 40 m

        string grassPath = "Assets/YughuesFreeGroundMaterials/M_YFGM_Grass01.mat";
        Material grass = AssetDatabase.LoadAssetAtPath<Material>(grassPath);
        if (grass != null)
        {
            pitch.GetComponent<MeshRenderer>().sharedMaterial = grass;
            grass.mainTextureScale = new Vector2(40, 25);
        }
    }

    // ── Pitch Markings ───────────────────────────────────────────────────────
    static void CreatePitchMarkings(Transform parent)
    {
        GameObject root = new GameObject("Pitch Markings");
        root.transform.SetParent(parent, false);

        Material white = CreateWhiteMat("PitchLineMat");

        // Pitch orientation: goals are on the Z ends at Z = +-14.89.
        // So Z is the pitch length (goal-to-goal) and X is the width.
        float halfW = 30f;     // pitch half-width  (X) -> touches sidelines at +-30
        float goalLineZ = 14.89f; // goal line position (Z), aligned with the goals
        float halfL = goalLineZ;   // Z distance from centre to each goal line
        float lineW = 0.15f;   // line thickness
        float y = 0.02f;       // just above grass

        // Touch lines (sidelines) along the X edges, from end line to end line
        CreateFlatQuad(root.transform, "Sideline_Left",  new Vector3(-halfW, y, 0), new Vector2(lineW, halfL * 2), white);
        CreateFlatQuad(root.transform, "Sideline_Right", new Vector3(halfW, y, 0),  new Vector2(lineW, halfL * 2), white);

        // Goal lines at the Z ends, spanning the full width
        CreateFlatQuad(root.transform, "GoalLine_South", new Vector3(0, y, -goalLineZ), new Vector2(halfW * 2, lineW), white);
        CreateFlatQuad(root.transform, "GoalLine_North", new Vector3(0, y,  goalLineZ), new Vector2(halfW * 2, lineW), white);

        // Centre line (across the width at Z=0)
        CreateFlatQuad(root.transform, "Centre_Line", new Vector3(0, y, 0), new Vector2(halfW * 2, lineW), white);

        // Centre circle (radius ~5m)
        CreateRing(root.transform, "Centre_Circle", Vector3.zero, 5f, lineW, y, white, 48);

        // Centre spot
        CreateFlatQuad(root.transform, "Centre_Spot", new Vector3(0, y, 0), new Vector2(0.4f, 0.4f), white);

        // Penalty areas  (16.5m deep inward from each goal line, 40.3m wide)
        float paDepth = 11f;    // inward distance from goal line
        float paHalfW = 17f;    // half width across X
        // South (-Z) penalty area
        CreateFlatQuad(root.transform, "PA_South_Top",    new Vector3(0, y, -goalLineZ + paDepth), new Vector2(paHalfW * 2, lineW), white);
        CreateFlatQuad(root.transform, "PA_South_Left",   new Vector3(-paHalfW, y, -goalLineZ + paDepth / 2), new Vector2(lineW, paDepth), white);
        CreateFlatQuad(root.transform, "PA_South_Right",  new Vector3(paHalfW, y, -goalLineZ + paDepth / 2),  new Vector2(lineW, paDepth), white);

        // North (+Z) penalty area
        CreateFlatQuad(root.transform, "PA_North_Top",    new Vector3(0, y, goalLineZ - paDepth), new Vector2(paHalfW * 2, lineW), white);
        CreateFlatQuad(root.transform, "PA_North_Left",   new Vector3(-paHalfW, y, goalLineZ - paDepth / 2), new Vector2(lineW, paDepth), white);
        CreateFlatQuad(root.transform, "PA_North_Right",  new Vector3(paHalfW, y, goalLineZ - paDepth / 2),  new Vector2(lineW, paDepth), white);

        // Goal areas  (5.5m deep x 18.3m wide, i.e. 9.15 half width)
        float gaDepth = 3.5f;
        float gaHalfW = 8f;
        // South goal area
        CreateFlatQuad(root.transform, "GA_South_Top",   new Vector3(0, y, -goalLineZ + gaDepth), new Vector2(gaHalfW * 2, lineW), white);
        CreateFlatQuad(root.transform, "GA_South_Left",  new Vector3(-gaHalfW, y, -goalLineZ + gaDepth / 2), new Vector2(lineW, gaDepth), white);
        CreateFlatQuad(root.transform, "GA_South_Right", new Vector3(gaHalfW, y, -goalLineZ + gaDepth / 2),  new Vector2(lineW, gaDepth), white);

        // North goal area
        CreateFlatQuad(root.transform, "GA_North_Top",   new Vector3(0, y, goalLineZ - gaDepth), new Vector2(gaHalfW * 2, lineW), white);
        CreateFlatQuad(root.transform, "GA_North_Left",  new Vector3(-gaHalfW, y, goalLineZ - gaDepth / 2), new Vector2(lineW, gaDepth), white);
        CreateFlatQuad(root.transform, "GA_North_Right", new Vector3(gaHalfW, y, goalLineZ - gaDepth / 2),  new Vector2(lineW, gaDepth), white);

        // Penalty spots (11m from goal line)
        float penMarkDist = 11f;
        CreateFlatQuad(root.transform, "PenSpot_South", new Vector3(0, y, -goalLineZ + penMarkDist), new Vector2(0.35f, 0.35f), white);
        CreateFlatQuad(root.transform, "PenSpot_North", new Vector3(0, y, goalLineZ - penMarkDist),  new Vector2(0.35f, 0.35f), white);

        // Penalty arcs (radius ~6m from penalty spot, bulging toward centre)
        // angle 0 = +X, 90 = +Z, 180 = -X, 270 = -Z
        // South arc bulges toward +Z (centre): span around 90 deg
        CreateArcSegment(root.transform, "PenArc_South", new Vector3(0, y, -goalLineZ + penMarkDist), 6f, 40f, 140f, lineW, white, 20);
        // North arc bulges toward -Z (centre): span around -90 deg
        CreateArcSegment(root.transform, "PenArc_North", new Vector3(0, y, goalLineZ - penMarkDist),  6f, -140f, -40f, lineW, white, 20);
    }

    // ── Fences ───────────────────────────────────────────────────────────────
    static void CreateFences(Transform parent)
    {
        string fencePath = "Assets/Fence Modular System/Prefabs/sectionB_V1_PREFAB.prefab";
        GameObject fencePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(fencePath);
        if (fencePrefab == null) return;

        GameObject fenceRoot = new GameObject("Perimeter Fences");
        fenceRoot.transform.SetParent(parent, false);

        for (float z = -20f; z <= 20f; z += 3f)
        {
            InstantiatePrefab(fencePrefab, new Vector3(-30f, 0, z), Quaternion.Euler(0, 90, 0), fenceRoot.transform);
            InstantiatePrefab(fencePrefab, new Vector3(30f, 0, z), Quaternion.Euler(0, -90, 0), fenceRoot.transform);
        }
        for (float x = -30f; x <= 30f; x += 3f)
        {
            InstantiatePrefab(fencePrefab, new Vector3(x, 0, -20f), Quaternion.Euler(0, 0, 0), fenceRoot.transform);
            InstantiatePrefab(fencePrefab, new Vector3(x, 0, 20f), Quaternion.Euler(0, 180, 0), fenceRoot.transform);
        }
    }

    // ── Tiered Stands with Crowd ─────────────────────────────────────────────
    static void CreateStands(Transform parent)
    {
        GameObject standsRoot = new GameObject("Crowd Stands");
        standsRoot.transform.SetParent(parent, false);

        string crowdPath = "Assets/Floreswa/Models/male01_1.fbx";
        GameObject crowdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(crowdPath);

        Material seatMatA = CreateColoredMat("SeatMat_A", new Color(0.15f, 0.25f, 0.6f));  // blue
        Material seatMatB = CreateColoredMat("SeatMat_B", new Color(0.7f, 0.15f, 0.15f));  // red

        // Build stands on both long sides (Z+ and Z-, along the X direction)
        BuildStand(standsRoot.transform, crowdPrefab, seatMatA, new Vector3(0, 0, 22f), Quaternion.Euler(0, 180, 0), 21f);
        BuildStand(standsRoot.transform, crowdPrefab, seatMatB, new Vector3(0, 0, -22f), Quaternion.identity, -21f);

        // Smaller end stands behind each goal (X+ and X-, along the Z direction)
        BuildEndStand(standsRoot.transform, crowdPrefab, seatMatA, new Vector3(-31f, 0, 0), Quaternion.Euler(0, 90, 0), false);
        BuildEndStand(standsRoot.transform, crowdPrefab, seatMatB, new Vector3(31f, 0, 0), Quaternion.Euler(0, -90, 0), true);
    }

    static void BuildStand(Transform parent, GameObject crowdPrefab, Material seatMat, Vector3 basePos, Quaternion crowdFace, float zDir)
    {
        GameObject stand = new GameObject("Stand_Side");
        stand.transform.SetParent(parent, false);
        stand.transform.localPosition = basePos;

        int tiers = 5;
        float tierDepth = 1.2f;
        float tierHeight = 0.55f;
        float standWidth = 40f;

        for (int t = 0; t < tiers; t++)
        {
            float z = t * tierDepth * Mathf.Sign(zDir);
            float y = t * tierHeight;

            // Tier step (bleacher)
            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Tier_" + t;
            step.transform.SetParent(stand.transform, false);
            step.transform.localPosition = new Vector3(0, y + tierHeight / 2f, z + tierDepth / 2f * Mathf.Sign(zDir));
            step.transform.localScale = new Vector3(standWidth, tierHeight, tierDepth);
            step.GetComponent<MeshRenderer>().sharedMaterial = seatMat;
            DestroyImmediate(step.GetComponent<Collider>());

            // Crowd on this tier
            if (crowdPrefab != null)
            {
                float spacing = 1.6f;
                for (float x = -standWidth / 2f + 1f; x <= standWidth / 2f - 1f; x += spacing)
                {
                    Vector3 pos = new Vector3(x, y + tierHeight + 0.6f, z + 0.3f * Mathf.Sign(zDir));
                    InstantiatePrefab(crowdPrefab, pos, crowdFace, stand.transform);
                }
            }
        }
    }

    static void BuildEndStand(Transform parent, GameObject crowdPrefab, Material seatMat, Vector3 basePos, Quaternion crowdFace, bool facingPositive)
    {
        GameObject stand = new GameObject("Stand_End");
        stand.transform.SetParent(parent, false);
        stand.transform.localPosition = basePos;

        int tiers = 4;
        float tierDepth = 1.2f;
        float tierHeight = 0.55f;
        float standWidth = 24f;
        float sign = facingPositive ? 1f : -1f;

        for (int t = 0; t < tiers; t++)
        {
            float x = t * tierDepth * sign;
            float y = t * tierHeight;

            GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Tier_" + t;
            step.transform.SetParent(stand.transform, false);
            step.transform.localPosition = new Vector3(x + tierDepth / 2f * sign, y + tierHeight / 2f, 0);
            step.transform.localScale = new Vector3(tierDepth, tierHeight, standWidth);
            step.GetComponent<MeshRenderer>().sharedMaterial = seatMat;
            DestroyImmediate(step.GetComponent<Collider>());

            if (crowdPrefab != null)
            {
                float spacing = 1.6f;
                for (float z = -standWidth / 2f + 1f; z <= standWidth / 2f - 1f; z += spacing)
                {
                    Vector3 pos = new Vector3(x + 0.3f * sign, y + tierHeight + 0.6f, z);
                    InstantiatePrefab(crowdPrefab, pos, crowdFace, stand.transform);
                }
            }
        }
    }

    // ── Advertising Boards ───────────────────────────────────────────────────
    static void CreateAdBoards(Transform parent)
    {
        GameObject root = new GameObject("Ad Boards");
        root.transform.SetParent(parent, false);

        Material[] boardMats = new Material[]
        {
            CreateColoredMat("AdMat_1", new Color(0.1f, 0.5f, 0.8f)),   // blue
            CreateColoredMat("AdMat_2", new Color(0.9f, 0.75f, 0.0f)),  // yellow
            CreateColoredMat("AdMat_3", new Color(0.1f, 0.7f, 0.2f)),   // green
            CreateColoredMat("AdMat_4", new Color(0.85f, 0.2f, 0.15f)), // red
        };

        float boardH = 0.8f;
        float boardD = 0.1f;
        float spacing = 5f;

        // Along both long sides, just outside the fence
        for (float x = -25f; x <= 25f; x += spacing)
        {
            Material mat = boardMats[Mathf.Abs((int)(x / spacing)) % boardMats.Length];
            // Z+ side
            CreateAdBoard(root.transform, new Vector3(x, boardH / 2f + 0.05f, 21f), new Vector3(spacing * 0.9f, boardH, boardD), mat);
            // Z- side
            CreateAdBoard(root.transform, new Vector3(x, boardH / 2f + 0.05f, -21f), new Vector3(spacing * 0.9f, boardH, boardD), mat);
        }

        // Shorter ends
        for (float z = -18f; z <= 18f; z += spacing)
        {
            Material mat = boardMats[Mathf.Abs((int)(z / spacing) + 2) % boardMats.Length];
            CreateAdBoard(root.transform, new Vector3(-29f, boardH / 2f + 0.05f, z), new Vector3(boardD, boardH, spacing * 0.9f), mat);
            CreateAdBoard(root.transform, new Vector3(29f, boardH / 2f + 0.05f, z), new Vector3(boardD, boardH, spacing * 0.9f), mat);
        }
    }

    static void CreateAdBoard(Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "AdBoard";
        board.transform.SetParent(parent, false);
        board.transform.localPosition = pos;
        board.transform.localScale = scale;
        board.GetComponent<MeshRenderer>().sharedMaterial = mat;
        DestroyImmediate(board.GetComponent<Collider>());
    }

    // ── Corner Flags ─────────────────────────────────────────────────────────
    static void CreateCornerFlags(Transform parent)
    {
        GameObject root = new GameObject("Corner Flags");
        root.transform.SetParent(parent, false);

        Material flagMat = CreateColoredMat("CornerFlagMat", new Color(1f, 0.35f, 0.05f)); // orange
        Material poleMat = CreateWhiteMat("FlagPoleMat");

        Vector3[] corners = new Vector3[]
        {
            new Vector3(-30f, 0, -14.89f),
            new Vector3(-30f, 0,  14.89f),
            new Vector3( 30f, 0, -14.89f),
            new Vector3( 30f, 0,  14.89f),
        };

        for (int i = 0; i < corners.Length; i++)
        {
            GameObject flag = new GameObject("CornerFlag_" + i);
            flag.transform.SetParent(root.transform, false);
            flag.transform.localPosition = corners[i];

            // Pole
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(flag.transform, false);
            pole.transform.localPosition = new Vector3(0, 0.75f, 0);
            pole.transform.localScale = new Vector3(0.04f, 0.75f, 0.04f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = poleMat;
            DestroyImmediate(pole.GetComponent<Collider>());

            // Flag (small quad)
            GameObject f = new GameObject("Flag");
            f.transform.SetParent(flag.transform, false);
            f.transform.localPosition = new Vector3(0.2f, 1.35f, 0);
            MeshFilter mf = f.AddComponent<MeshFilter>();
            MeshRenderer mr = f.AddComponent<MeshRenderer>();
            mf.sharedMesh = CreateQuadMesh();
            mr.sharedMaterial = flagMat;
        }
    }

    // ── Audio ────────────────────────────────────────────────────────────────
    static void CreateAudio(Transform parent)
    {
        GameObject audioObj = new GameObject("CrowdAudioController");
        audioObj.transform.SetParent(parent, false);
        AudioSource src = audioObj.AddComponent<AudioSource>();
        audioObj.AddComponent<CrowdAudioController>().crowdAudioSource = src;
    }

    // ── Lighting ─────────────────────────────────────────────────────────────
    static void CreateLighting(Transform parent)
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var l in lights)
        {
            if (l.type == LightType.Directional) DestroyImmediate(l.gameObject);
        }

        GameObject sunObj = new GameObject("Stadium Sun");
        sunObj.transform.SetParent(parent, false);
        Light sun = sunObj.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.intensity = 1.3f;
        sun.color = new Color(1f, 0.96f, 0.88f); // warm sunlight
        sunObj.transform.rotation = Quaternion.Euler(50, 30, 0);

        // Fill light (softer, from opposite side)
        GameObject fillObj = new GameObject("Fill Light");
        fillObj.transform.SetParent(parent, false);
        Light fill = fillObj.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.35f;
        fill.color = new Color(0.75f, 0.85f, 1f); // cool sky fill
        fillObj.transform.rotation = Quaternion.Euler(30, 210, 0);

        // Reflection probe
        GameObject probeObj = new GameObject("Pitch Reflection Probe");
        probeObj.transform.SetParent(parent, false);
        probeObj.transform.position = new Vector3(0, 5, 0);
        ReflectionProbe probe = probeObj.AddComponent<ReflectionProbe>();
        probe.size = new Vector3(70, 20, 50);
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static void CreateFlatQuad(Transform parent, string name, Vector3 center, Vector2 size, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mf.sharedMesh = CreateQuadMesh();
        mr.sharedMaterial = mat;
        go.transform.localScale = new Vector3(size.x, 1f, size.y);
    }

    static void CreateRing(Transform parent, string name, Vector3 center, float radius, float width, float y, Material mat, int segments)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        Mesh mesh = new Mesh();
        mesh.name = name + "_Mesh";

        int innerVerts = segments;
        int outerVerts = segments;
        int totalVerts = innerVerts + outerVerts;
        int totalTris = segments * 6;

        Vector3[] verts = new Vector3[totalVerts];
        int[] tris = new int[totalTris];
        Vector2[] uvs = new Vector2[totalVerts];

        float innerR = radius - width / 2f;
        float outerR = radius + width / 2f;

        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            verts[i] = new Vector3(cos * innerR, y, sin * innerR);
            verts[i + segments] = new Vector3(cos * outerR, y, sin * outerR);

            uvs[i] = new Vector2((float)i / segments, 0);
            uvs[i + segments] = new Vector2((float)i / segments, 1);
        }

        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int ti = i * 6;

            tris[ti] = i;
            tris[ti + 1] = next;
            tris[ti + 2] = i + segments;

            tris[ti + 3] = next;
            tris[ti + 4] = next + segments;
            tris[ti + 5] = i + segments;
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;
    }

    static void CreateArcSegment(Transform parent, string name, Vector3 center, float radius, float startDeg, float endDeg, float width, Material mat, int segments)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        Mesh mesh = new Mesh();
        mesh.name = name + "_Mesh";

        float innerR = radius - width / 2f;
        float outerR = radius + width / 2f;
        int totalVerts = (segments + 1) * 2;
        int totalTris = segments * 6;

        Vector3[] verts = new Vector3[totalVerts];
        int[] tris = new int[totalTris];
        Vector2[] uvs = new Vector2[totalVerts];

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            verts[i * 2] = new Vector3(cos * innerR, 0.02f, sin * innerR);
            verts[i * 2 + 1] = new Vector3(cos * outerR, 0.02f, sin * outerR);

            uvs[i * 2] = new Vector2(t, 0);
            uvs[i * 2 + 1] = new Vector2(t, 1);
        }

        for (int i = 0; i < segments; i++)
        {
            int ti = i * 6;
            int v = i * 2;

            tris[ti] = v;
            tris[ti + 1] = v + 2;
            tris[ti + 2] = v + 1;

            tris[ti + 3] = v + 2;
            tris[ti + 4] = v + 3;
            tris[ti + 5] = v + 1;
        }

        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mf.sharedMesh = mesh;
    }

    static Mesh CreateQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Quad";
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, 0, 0),
            new Vector3(0.5f, 0, 0),
            new Vector3(-0.5f, 0, 1),
            new Vector3(0.5f, 0, 1),
        };
        mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1)
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    static Material CreateWhiteMat(string name)
    {
        Material m = new Material(Shader.Find("Standard"));
        m.name = name;
        m.color = Color.white;
        return m;
    }

    static Material CreateColoredMat(string name, Color color)
    {
        Material m = new Material(Shader.Find("Standard"));
        m.name = name;
        m.color = color;
        return m;
    }

    static void InstantiatePrefab(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
    {
        GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (obj == null) return;
        obj.transform.position = pos;
        obj.transform.rotation = rot;
        obj.transform.SetParent(parent);
    }
}
