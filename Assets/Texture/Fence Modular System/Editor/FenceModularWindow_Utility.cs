using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{
    // Register scene-opened callback at project start (works without tool window)
    [InitializeOnLoadMethod]
    static void RegisterPipelineSwapOnSceneOpen()
    {
        EditorSceneManager.sceneOpened -= OnSceneOpenedStatic;
        EditorSceneManager.sceneOpened += OnSceneOpenedStatic;
        // Also swap right after package import / domain reload, so the prefabs
        // are pipeline-correct before the user ever opens a scene.
        EditorApplication.delayCall += EnsurePipelineMaterials;
    }

    static void OnSceneOpenedStatic(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += EnsurePipelineMaterials;
    }

    // The Asset Store package must ship with the prefabs referencing the Shared
    // (Standard) materials — the swap must therefore never run in the project
    // the package is uploaded from. Publisher machines are detected via the
    // Asset Store Publishing Tools (or their Library cache); buyers have neither.
    static bool IsPublisherProject()
    {
        if (System.IO.Directory.Exists("Library/AssetStoreToolsCache")) return true;
        if (AssetDatabase.IsValidFolder("Assets/AssetStoreTools")) return true;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            if (asm.GetName().Name.StartsWith("asset-store-tools")) return true;
        return false;
    }

    // Static material cache: Shared name → pipeline-correct material
    static Dictionary<string, Material> s_pipelineMatCache;

    static void BuildPipelineMatCache()
    {
        s_pipelineMatCache = new Dictionary<string, Material>();
        // Pipeline-specific folder first; Shared (Standard) as the only
        // fallback — never a foreign pipeline's materials via a Root-wide scan.
        string[] folders = { PipelineRoot, Root + "/Shared" };

        foreach (var folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var mg in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var mp = AssetDatabase.GUIDToAssetPath(mg);
                var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (m && !s_pipelineMatCache.ContainsKey(m.name))
                    s_pipelineMatCache[m.name] = m;
            }
        }
    }

    // Swap prefab materials to match active pipeline and save to disk
    static void EnsurePipelineMaterials()
    {
        if (IsPublisherProject()) return;
        if (!AssetDatabase.IsValidFolder(Root)) return;
        BuildPipelineMatCache();

        bool anyChanged = false;
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) continue;

            bool prefabChanged = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (SwapRendererMats(r)) prefabChanged = true;
            }
            if (prefabChanged) { EditorUtility.SetDirty(prefab); anyChanged = true; }
        }

        if (anyChanged) AssetDatabase.SaveAssets();
    }

    static bool SwapRendererMats(Renderer r)
    {
        if (s_pipelineMatCache == null) return false;
        var mats = r.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < mats.Length; i++)
        {
            if (!mats[i]) continue;
            if (s_pipelineMatCache.TryGetValue(mats[i].name, out var found) && found != mats[i])
            { mats[i] = found; changed = true; }
        }
        if (changed) r.sharedMaterials = mats;
        return changed;
    }
bool IsPillarInstance(GameObject go)
{
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;

    // Matches post_V1E_PREFAB, post_V2E_PREFAB, post_V1M_PREFAB, etc.
    return n.StartsWith("post_") && n.EndsWith("_PREFAB");
}

bool IsTopInstance(GameObject go)
{
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;
    return n == TopPrefabNames[0] ||
           n == TopPrefabNames[1] ||
           n == TopPrefabNames[2] ||
           n == TopPrefabNames[3];
}

void RemoveTopFromPillar(Transform pillar)
{
    for (int i = pillar.childCount - 1; i >= 0; i--)
    {
        var c = pillar.GetChild(i).gameObject;
        if (IsTopInstance(c))
            Undo.DestroyObjectImmediate(c);
    }
}

void CenterFencePivot(GameObject root)
{
    var renderers = root.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0)
        return;

    Bounds bounds = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++)
        bounds.Encapsulate(renderers[i].bounds);

Vector3 center = new Vector3(
    bounds.center.x,
    bounds.min.y,
    bounds.center.z
);
    Vector3 delta = root.transform.position - center;

    // move root to center
    root.transform.position = center;

    // keep children world positions
    foreach (Transform child in root.transform)
        child.position += delta;
}

// Parity with the full version: center a finalized fence's pivot when its root is
// selected ("beim Anklicken des Roots"). Self-heals fences whose pivot sits on the
// first placed element. Guarded so it never fires during a build / continue build.
void EnsureFencePivotCentered(GameObject root)
{
    if (!root || buildMode || continueAnchorActive || continueTargetFence != null) return;
    var renderers = root.GetComponentsInChildren<Renderer>();
    if (renderers.Length == 0) return;
    Bounds b = renderers[0].bounds;
    for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
    Vector3 center = new Vector3(b.center.x, b.min.y, b.center.z);
    if (Vector3.Distance(root.transform.position, center) < 0.01f) return; // already centered
    Undo.RegisterFullObjectHierarchyUndo(root, "Center Fence Pivot");
    Vector3 delta = root.transform.position - center;
    root.transform.position = center;
    foreach (Transform child in root.transform)
        child.position += delta;
}

void SetFenceGizmosVisible(bool visible)
{
    GizmoUtility.SetGizmoEnabled(typeof(MeshCollider), visible, false);
    GizmoUtility.SetGizmoEnabled(typeof(BoxCollider),  visible, false);
    GizmoUtility.SetGizmoEnabled(typeof(LODGroup),     visible, false);
}

bool IsExplicitFenceRootSelected()
{
    return GetUiTargetFenceRoot() != null;
}

GameObject FindFenceRootFromSelection(GameObject go)
{
    if (!go) return null;

    // First try: check existing tracked fences
    Transform t = go.transform;
    while (t != null)
    {
        if (finalizedFences.Contains(t.gameObject))
            return t.gameObject;
        t = t.parent;
    }

    // Lazy discovery: walk up to find a Fence_ root not yet tracked
    t = go.transform;
    while (t != null)
    {
        if (t.parent == null && t.name.StartsWith("Fence_"))
        {
            // Verify it has fence content before adding
            bool hasContent = false;
            foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
            {
                if (child == t) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (!src) continue;
                string n = src.name;
                if ((n.StartsWith("post_") || n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
                     n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) && n.EndsWith("_PREFAB"))
                { hasContent = true; break; }
            }

            if (hasContent)
            {
                finalizedFences.Add(t.gameObject);
                RebuildChainIndexForRoot(t.gameObject);
                RebuildProtectedPillarIdCache();
                return t.gameObject;
            }
        }
        t = t.parent;
    }

    return null;
}

GameObject GetUiTargetFenceRoot()
{
    return FindFenceRootFromSelection(Selection.activeGameObject);
}

GameObject GetStartPillar(GameObject fenceRoot)
{
    if (!fenceRoot)
        return null;

    fenceStartPillars.TryGetValue(fenceRoot, out var pillar);
    return pillar;
}

Transform FindOwningFenceRoot(Transform t)
{
    if (!t) return null;
    var root = FindFenceRootFromSelection(t.gameObject);
    return root ? root.transform : null;
}

static bool IsPillarMPrefab(string prefabName)
{
    // Matches post_V1M_PREFAB, post_V2M_PREFAB, etc.
    return prefabName.StartsWith("post_") && prefabName.Contains("M_") && prefabName.EndsWith("_PREFAB");
}

string GetPillarPrefabName(char type)
{
    // type: 'E', 'M', 'T', 'C', '4' (45°)
    string v = VariantTag;

    return type switch
    {
        'E' => $"post_{v}E_PREFAB",
        'M' => $"post_{v}M_PREFAB",
        'T' => $"post_{v}T_PREFAB",
        'C' => $"post_{v}C_PREFAB",
        '4' => $"post_{v}C45_PREFAB",
        _   => null
    };
}

bool IsSnapFree(Transform pillar, string snapName)
{
    var snap = FindSnap(pillar, snapName);
    if (!snap)
        return false;

    // Check if any rail or curved rail snap occupies this position
    var allSnaps = FenceIds.FindAll<Transform>();
    foreach (var t in allSnaps)
    {
        if (t == snap)
            continue;

        if (t.name != RailStartSnap && t.name != RailEndSnap)
            continue;

        if (Vector3.Distance(t.position, snap.position) < 0.001f)
            return false;
    }

    return true;
}

Transform FindLastRealRailFreeSnap()
{
    // 1) Session objects
    for (int i = currentBuildObjects.Count - 1; i >= 0; i--)
    {
        var go = currentBuildObjects[i];
        if (!go) continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!src) continue;

if (src.name.StartsWith("sectionB_") && src.name.EndsWith("_PREFAB"))
            return FindSnap(go.transform, RailEndSnap);

if (src.name.StartsWith("sectionBCrvd_") && src.name.EndsWith("_PREFAB") && !string.IsNullOrEmpty(curvedOutSnapName))
            return FindSnap(go.transform, curvedOutSnapName);
    }

    // 2) Fallback: existing fence (Continue Abort without commits)
    if (continueTargetFence)
    {
        var rails = continueTargetFence.GetComponentsInChildren<Transform>(true);
        for (int i = rails.Length - 1; i >= 0; i--)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(rails[i]);
            if (!src) continue;

if (src.name.StartsWith("sectionB_") && src.name.EndsWith("_PREFAB"))
                return FindSnap(rails[i], RailEndSnap);

if (src.name.StartsWith("sectionBCrvd_") && src.name.EndsWith("_PREFAB") && !string.IsNullOrEmpty(curvedOutSnapName))
                return FindSnap(rails[i], curvedOutSnapName);
        }
    }

    return null;
}

void FinalizeAbortedNormalBuild()
{
    currentBuildObjects.Clear();
}

void FinalizeAbortedContinueBuild()
{
    currentBuildObjects.Clear();
}

    static void AlignRailToTarget(Transform root, Transform railSnap, Vector3 toTargetDir, Transform targetSnap)
    {
        if (!root || !railSnap || !targetSnap) return;
        root.rotation = YawDelta(railSnap.right, toTargetDir) * root.rotation;
        root.position += targetSnap.position - railSnap.position;
    }

    static void SetFullDetailOnObject(GameObject go, bool enable)
    {
        if (!go) return;

        // Disable LODGroups recursively
        foreach (var lod in go.GetComponentsInChildren<LODGroup>(true))
            lod.enabled = !enable;

        // Show/hide all descendants whose name contains LOD but not LOD0
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.Contains("LOD") && !n.Contains("LOD0"))
                t.gameObject.SetActive(!enable);
        }
    }

    static void ApplyFullDetailToFence(GameObject root, bool enable)
    {
        if (!root) return;

        SetFullDetailOnObject(root, enable);

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root.transform) continue;
            SetFullDetailOnObject(t.gameObject, enable);
        }
    }

    void ApplyFullDetailToAllFences(bool enable)
    {
        foreach (var root in finalizedFences)
            ApplyFullDetailToFence(root, enable);
    }

    void ApplyFullDetailToCurrentBuild()
    {
        if (!fullDetailMode) return;
        foreach (var go in currentBuildObjects)
        {
            if (!go) continue;
            // Process object AND all children (e.g. tops with own LODGroup)
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                SetFullDetailOnObject(t.gameObject, true);
        }
    }

    static Vector3 MouseOnPlane(Vector2 mouse, Vector3 fallback)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mouse);
        var plane = new Plane(Vector3.up, new Vector3(0, fallback.y, 0));
        return plane.Raycast(ray, out var d) ? ray.GetPoint(d) : fallback;
    }

    static Vector3 MouseOnSurface(Vector2 mouse, Vector3 fallback)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mouse);

        // Raycast against all scene colliders
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
            return hit.point;

        // No surface hit -> snap to Y0 plane
        return MouseOnPlane(mouse, new Vector3(fallback.x, 0f, fallback.z));
    }

    static Vector3 PickDir(Vector3 delta, Vector3 last)
    {
        delta.y = 0;
        if (delta.sqrMagnitude < 0.0001f) return last;
        delta.Normalize();

        float rx = Vector3.Dot(delta, Vector3.right);
        float lx = Vector3.Dot(delta, Vector3.left);
        float fz = Vector3.Dot(delta, Vector3.forward);
        float bz = Vector3.Dot(delta, Vector3.back);

        if (rx >= lx && rx >= fz && rx >= bz) return Vector3.right;
        if (lx >= fz && lx >= bz) return Vector3.left;
        if (fz >= bz) return Vector3.forward;
        return Vector3.back;
    }

static void DrawDirGizmos(Vector3 pos, Vector3 active)
{
    DrawArrow(pos, Vector3.right,   active == Vector3.right);
    DrawArrow(pos, Vector3.left,    active == Vector3.left);
    DrawArrow(pos, Vector3.forward, active == Vector3.forward);
    DrawArrow(pos, Vector3.back,    active == Vector3.back);
}

static void DrawArrow(
    Vector3 pos,
    Vector3 dir,
    bool active,
    Color? overrideColor = null,
    float lengthMul = 1f
)
{
    float s = HandleUtility.GetHandleSize(pos);
    Handles.color = overrideColor ?? (active ? ActiveCol : BaseCol);

    float len = ArrowLength * lengthMul * s;
    Vector3 end = pos + dir.normalized * len;

    Handles.ConeHandleCap(
        0,
        end,
        Quaternion.LookRotation(dir),
        ArrowHeadSize * (active ? 1.15f : 1f) * s,
        EventType.Repaint
    );
}

static void DrawArcArrow90(
    Vector3 center,
    Vector3 fromDir,
    Vector3 normal,
    float radius,
    bool clockwise,
    Color col
)
{
    Handles.color = col;

float sweep = clockwise ? -45f : 45f;

Vector3 arcNormal = normal.normalized;

const float startTrimDeg = -30f; // trims the arc start
Vector3 startDir =
    Quaternion.AngleAxis(clockwise ? startTrimDeg : -startTrimDeg, arcNormal)
    * fromDir.normalized;

    Handles.DrawWireArc(
        center,
        arcNormal,
        startDir,
        sweep,
        radius
    );

    // arrow head
    Vector3 endDir =
        Quaternion.AngleAxis(sweep, arcNormal) * startDir;

    Vector3 endPos = center + endDir * radius;

Vector3 arrowDir = Vector3.Cross(arcNormal, endDir).normalized;
if (clockwise) arrowDir = -arrowDir;

Handles.ConeHandleCap(
    0,
    endPos,
    Quaternion.LookRotation(arrowDir, arcNormal),
    ArrowHeadSize * 1.2f * HandleUtility.GetHandleSize(endPos),
    EventType.Repaint
);
}

static void DrawVisualTurnArc90(
    Vector3 pos,
    Vector3 activeDir,
    bool turnRight,
    float radius,
    Color col
)
{
    Vector3 up = Vector3.up;
    Vector3 dir = activeDir.normalized;
    Vector3 side = Vector3.Cross(up, dir).normalized;

// 1) shared base starting point (both arrows same)
Vector3 sharedOrigin =
    pos
    + dir * radius;

// shared forward offset
float forwardOffset = radius * 0.01f;
sharedOrigin += dir * forwardOffset;

// 2) individual lateral offset PER arrow
float sideOffset = radius * -1.01f;

Vector3 localOrigin =
    turnRight
        ? sharedOrigin + side * sideOffset
        : sharedOrigin - side * sideOffset;

// 3) circle center relative to LOCAL origin
Vector3 center =
    turnRight
        ? localOrigin + side * radius
        : localOrigin - side * radius;

// 4) Start tangent stays local
const float startTrimDeg = -120f; // <-- HERE trim start

Vector3 rawDir = turnRight ? -side : side;
Vector3 fromDir =
    Quaternion.AngleAxis(
        turnRight ? startTrimDeg : -startTrimDeg,
        up
    ) * rawDir;

    Handles.color = col;

    float sweep = turnRight ? -45f : 45f;

    Handles.DrawWireArc(
        center,
        up,
        fromDir,
        sweep,
        radius
    );

    // Arrow head
    Vector3 endDir =
        Quaternion.AngleAxis(sweep, up) * fromDir;

    Vector3 endPos = center + endDir * radius;

    Vector3 arrowDir = Vector3.Cross(up, endDir).normalized;
    if (turnRight) arrowDir = -arrowDir;

    Handles.ConeHandleCap(
        0,
        endPos,
        Quaternion.LookRotation(arrowDir, up),
        ArrowHeadSize * 1.1f * HandleUtility.GetHandleSize(endPos),
        EventType.Repaint
    );
}

    static Transform FindSnap(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

T FindAsset<T>(string exactName) where T : Object
{
    // Prefabs: search only prefabs
    if (typeof(T) == typeof(GameObject))
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset)
                continue;

            if (asset.name == exactName)
                return asset as T;
        }
        return null;
    }

    // Materials & other assets: search pipeline-specific folder first (HDRP or URP)
    string pipelineFolder = PipelineRoot;
    string[] searchFolders = pipelineFolder != Root
        ? new[] { pipelineFolder, Root }  // pipeline folder first, then fallback
        : new[] { Root };                  // Built-in: only general root

    foreach (var folder in searchFolders)
    {
        if (!AssetDatabase.IsValidFolder(folder)) continue;

        var allGuids = AssetDatabase.FindAssets(exactName, new[] { folder });
        foreach (var guid in allGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) continue;

            if (asset.name == exactName)
                return asset;
        }
    }

    return null;
}

bool IsRailInstance(GameObject go)
{
    if (!go) return false;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;

    string n = src.name;

    bool isRail =
        (n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_")) &&
        n.EndsWith("_PREFAB");

    return isRail;
}

// Returns 0=not a gate, 1=single gate, 2=double gate
int GetGateType(GameObject go)
{
    if (!go) return 0;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return 0;
    if (src.name.StartsWith("single_gate_")) return 1;
    if (src.name.StartsWith("double_gate_")) return 2;
    return 0;
}

static long PosKey(Vector3 p)
{
    // Quantize to avoid floating point noise (0.0001 units)
    int x = Mathf.RoundToInt(p.x * 10000f);
    int y = Mathf.RoundToInt(p.y * 10000f);
    int z = Mathf.RoundToInt(p.z * 10000f);

    // Pack into a single 64-bit key
    // Note: Using 21 bits per axis (fits typical scene scale). This is fine for modular placement.
    long key = 0;
    key |= ((long)(x & 0x1FFFFF) << 42);
    key |= ((long)(y & 0x1FFFFF) << 21);
    key |= ((long)(z & 0x1FFFFF));
    return key;
}

    static Quaternion YawDelta(Vector3 from, Vector3 to)
    {
        from.y = 0; to.y = 0;
        if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.LookRotation(to.normalized, Vector3.up) *
               Quaternion.Inverse(Quaternion.LookRotation(from.normalized, Vector3.up));
    }

    // Measure pillar M snap-to-snap width for accurate chain segment length
    float MeasurePillarWidth()
    {
        var prefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
        if (!prefab) return 0f;
        var tmp = CreateGhost(prefab);
        var s1 = FindSnap(tmp.transform, PillarSnapName);
        var s2 = FindSnap(tmp.transform, "SnapPoint2");
        float w = (s1 && s2) ? Vector3.Distance(s1.position, s2.position) : 0f;
        DestroyImmediate(tmp);
        return w;
    }

    // Instantiate prefab (used by Variant/Chain swap code)
    static GameObject InstantiateAndSwap(GameObject prefab)
    {
        return (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    }

    // Ground Y at worldPos, ignoring upper colliders (pillars etc.) by picking the lowest hit.
    // Returns NaN if no hit.
    static float GroundYAt(Vector3 worldPos)
    {
        var hits = Physics.RaycastAll(new Vector3(worldPos.x, worldPos.y + 100f, worldPos.z),
                                      Vector3.down, 200f);
        float lowest = float.NaN;
        for (int i = 0; i < hits.Length; i++)
            if (float.IsNaN(lowest) || hits[i].point.y < lowest) lowest = hits[i].point.y;
        return lowest;
    }

}
} // namespace WB3DAssets.FenceModularSystem
