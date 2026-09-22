using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow : EditorWindow
{
    enum State { Idle, FreeMove, DirectionSelect, RailPreview, CornerSelect }

enum ContinueVariant
{
    V1,
    V2,
    V3,
    V4,
    V5
}

bool variantV1Available;

bool variantV2Available;

Texture2D variantV1Preview;

Texture2D variantV2Preview;

Texture2D variantV3Preview;

Texture2D variantV4Preview;

Texture2D variantV5Preview;

string VariantTag => selectedVariant switch
{
    ContinueVariant.V2 => "V2",
    ContinueVariant.V3 => "V3",
    ContinueVariant.V4 => "V4",
    ContinueVariant.V5 => "V5",
    _ => "V1"
};

//Cache element positions per fence root + variant tag for instant restore on switch-back
Dictionary<(GameObject root, string tag), List<(int sibling, Vector3 pos, Quaternion rot)>> variantPosCache
    = new Dictionary<(GameObject, string), List<(int, Vector3, Quaternion)>>();

Texture2D[] topPreviews;

int topPreviewIndex = 0;

ContinueVariant selectedVariant = ContinueVariant.V1;

// Type shown in the carousel. May point at a type this LITE build does not
// ship; selectedVariant - the one every prefab lookup uses - never does.
ContinueVariant previewVariant = ContinueVariant.V1;

bool textureVariantIsWorn = false;

int newTextureIndex = 0; // 0 = new1, 1 = new2, 2 = new3, 3 = new4
int wornTextureIndex = 0; // 0 = worn1, 1 = worn2, 2 = worn3, 3 = worn4

    const string Root = "Assets/Fence Modular System";

    const string RootHDRP = Root + "/HDRP";

    const string RootURPBuiltIn = Root + "/URP & Built-In";
    const string RootURP  = RootURPBuiltIn + "/URP";
    const string RootBuiltIn = RootURPBuiltIn + "/Built-In";

    static string PipelineRoot
    {
        get
        {
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            if (rp == null) return RootBuiltIn;
            string typeName = rp.GetType().Name;
            if (typeName.Contains("HDRenderPipeline")) return RootHDRP;
            return RootURP;
        }
    }

    const string GhostMatName     = "GhostPreview_MAT";
    static readonly bool hasCurvedRails = false; // Set to true for fence types with curved sections

    const string PillarSnapName   = "SnapPoint1"; // X+ = build dir

    const string RailStartSnap    = "SnapPoint1"; // Rail SnapPoint1 local X+ points to start pillar

const string RailEndSnap      = "SnapPoint2";

const string TopSnapName      = "SnapPointTop";

static readonly string[] TopPrefabNames =
{
    "top1_PREFAB",
    "top2_PREFAB",
    "top3_PREFAB",
    "top4_PREFAB",
};

const float ArrowLength = 0.4f;

const float ArrowHeadSize = 0.12f;

static readonly int ContinueGizmoIdHint = "BMS_ContinueDoubleArrow".GetHashCode();
static readonly int GateSingleGizmoIdHint = "FMS_GateSingle".GetHashCode();
static readonly int GateDoubleGizmoIdHint = "FMS_GateDouble".GetHashCode();

const float Dot22_5 = 0.9238795f; // cos(22.5°)

const float Dot67_5 = 0.3826834f; // cos(67.5°)

    static readonly Color BaseCol     = new(0.45f, 0.75f, 1f, 0.55f);
    static readonly Color GateTextCol = new(1f, 1f, 1f, 1f);

    static readonly Color ActiveCol = new(0.65f, 1f, 0.30f, 0.95f);

    State state;

    bool buildMode;

bool canContinueBuild;
GameObject selectedContinuePillar;

const float FenceListHeight = 100f;

readonly List<GameObject> finalizedFences = new();

readonly Dictionary<GameObject, GameObject> fenceStartPillars = new();

readonly Dictionary<GameObject, ChainIndexCache> chainIndexByRoot = new();

readonly Dictionary<GameObject, List<FenceId>> deletedRailIdsByRoot = new();

// Ghost snaps: when a rail is deleted, store its SnapPoint positions so the path can bridge gaps
readonly Dictionary<GameObject, List<(Vector3 snap1, Vector3 snap2)>> deletedRailSnapsByRoot = new();
// Temporary: snap positions of currently selected rails (captured before potential delete)
readonly Dictionary<FenceId, (Vector3 snap1, Vector3 snap2)> pendingRailSnaps = new();

const string StartMarkerName = "__BMS_START__";

const string VariantLockMarkerName = "__BMS_VARIANT_LOCKED__";

int selectedFenceIndex = -1;


System.Action onHierarchyChanged;

bool suppressDeleteUndo;

readonly HashSet<FenceId> protectedPillarIds = new();

bool lastSelectionWasRail;

bool lastSelectionWasPillar;

GameObject lastRailDeleteFenceRoot;
readonly Dictionary<FenceId, GameObject> railIdToFenceRoot = new(); // instanceId → fenceRoot, survives selection clear

readonly HashSet<FenceId> railCoSelectedPillarIds = new(); // Pillars visually co-selected with rail
bool suppressSelectionChanged; // Prevent recursion when setting Selection.objects

    GameObject ghost;

    Material ghostMat;

static MaterialPropertyBlock ghostPropBlock;

static readonly Color GhostInvalidCol = new(1f, 0.45f, 0.4f, 0.65f);  // bright reddish warning

static readonly int PropBaseColor  = Shader.PropertyToID("_BaseColor");   // HDRP Lit + URP

static readonly int PropUnlitColor = Shader.PropertyToID("_UnlitColor");  // HDRP Unlit

static readonly int PropColor      = Shader.PropertyToID("_Color");       // Built-in fallback

bool fullDetailMode = true;

    GameObject lastPlacedPillarM;

GameObject lastPlacedPillarE;

readonly List<GameObject> currentBuildObjects = new();

    Vector3 frozenPos;

    Vector3 activeDir = Vector3.right;

    Transform lastPillarSnap;

bool continueDirOverrideActive;

Vector3 continueDirOverride;

string continueTSnapOverride; // "SnapPointT1" or "SnapPointT2"

bool isV1MContinueMode; // Flag for reduced CornerSelect from V1M
bool continueFlipElements; // Flip when continuing from start pillar

readonly List<GameObject> ghostPillarsM = new();

    GameObject railPreviewRoot;

    readonly List<GameObject> railSegs = new();

    Vector3 railAnchorPos;

    float segLen;

GameObject curvedGhostRoot;

GameObject ghostCurvedRail;

GameObject ghostCurvedPillar;

bool curvedGhostActive;

int consecutiveUnmirroredCurvedRails = 0;

int consecutiveUnmirroredPillarM = 0;

Transform curvedGhostEndSnap;

string curvedOutSnapName;

GameObject hover90Root;

GameObject hover90EndPillarE;

GameObject hover90Rail;

GameObject hover90PillarM;

readonly List<GameObject> hoverChainRails = new();

readonly List<GameObject> hoverChainPillarsM = new();

Vector3 hoverChainAnchorPos;

Vector3 hoverChainDir;

float hoverChainSegLen;

Transform hoverChainStartSnap; // Cached start snap for corner pillars

GameObject dirSelectHoverRoot;

readonly List<GameObject> dirSelectHoverRails = new();

readonly List<GameObject> dirSelectHoverPillars = new();

float dirSelectSegLen;

GameObject continueAnchorPillar;   // hidden V1E

bool continueAnchorActive;


Transform continueSnapProxy;

GameObject continueTargetFence;
Vector3 continueScale = Vector3.one; // Scale inherited from target fence

int continueUndoGroup = -1; // Undo group captured at Continue Build start, collapsed on finalize

int continueTopIndex = -1; // -1 = no tops

GameObject continueGhostPillarM;

// Close loop detection
GameObject closeLoopTargetPillar;
bool closeLoopDetected;
char closeLoopReplacementType; // 'E','M','C','4'(C45),'T' or '\0'
Vector3 closeLoopApproachDir;
Quaternion closeLoopFitRotation;
Vector3 closeLoopFitPosition;
GameObject closeLoopReplacementGhost; // visual swap ghost
GameObject closeLoopOriginalGhost;    // hidden original last ghost
static readonly Color GhostCloseLoopCol = new(0.2f, 1f, 0.35f, 0.9f);

bool ENABLE_CONTINUE_BUILD = true;
bool ENABLE_V1M_VARIANT_LOCK = false; // Set true to re-enable V1M variant lock dialog

// ===================== FREE VERSION (Asset Store sampler) =====================
// Paid Asset Store listing the upsell button opens. TODO: set the real URL.
const string FullVersionUrl = "https://assetstore.unity.com/packages/3d/environments/fence-modular-system-370338";

bool _freeComputed;
readonly HashSet<ContinueVariant> _availTypes = new HashSet<ContinueVariant>();
// "V1|new|0" style keys: which individual finish slots ship in this build
readonly HashSet<string> _availFinish = new HashSet<string>();

static string FinishKey(string tag, bool worn, int idx) =>
    tag + "|" + (worn ? "worn" : "new") + "|" + idx;

void EnsureFreeAvailability()
{
    if (_freeComputed && _availTypes.Count > 0) return; // self-heal: recompute while still empty
    _availTypes.Clear();
    _availFinish.Clear();
    foreach (ContinueVariant v in System.Enum.GetValues(typeof(ContinueVariant)))
    {
        string tag = VariantTagOf(v);
        if (FindAsset<GameObject>("post_" + tag + "M_PREFAB") != null) _availTypes.Add(v);
        for (int i = 1; i <= 4; i++)
        {
            if (FindAsset<Material>("fence_" + tag + "_new"  + i + "_MAT") != null)
                _availFinish.Add(FinishKey(tag, false, i - 1));
            if (FindAsset<Material>("fence_" + tag + "_worn" + i + "_MAT") != null)
                _availFinish.Add(FinishKey(tag, true,  i - 1));
        }
    }
    // Only cache once assets are actually present. If this runs during a domain
    // reload (before the AssetDatabase is ready) nothing is found - leave it
    // uncached so the next OnGUI frame retries instead of caching an empty result.
    if (_availTypes.Count > 0) _freeComputed = true;
}

readonly Dictionary<string, int> _firstNewIdx = new Dictionary<string, int>();
readonly Dictionary<string, int> _firstWornIdx = new Dictionary<string, int>();

// First available finish index (0-3) for a type, e.g. V1->0 (new1), V3->2 (new3).
// Caches only once found, so an early call (before assets load) safely retries.
int FirstFinishIndex(string tag, bool worn)
{
    var cache = worn ? _firstWornIdx : _firstNewIdx;
    if (cache.TryGetValue(tag, out var idx)) return idx;
    for (int i = 1; i <= 4; i++)
        if (FindAsset<Material>("fence_" + tag + "_" + (worn ? "worn" : "new") + i + "_MAT") != null)
        {
            cache[tag] = i - 1;
            return i - 1;
        }
    return 0;
}

bool IsTypeAvailable(ContinueVariant v)
{
    EnsureFreeAvailability();
    return _availTypes.Contains(v);
}

int lastVariantSeen = -1;

// Pulls the carousel onto the working type only when that type actually
// changed (fence selected, variant swapped). Must NOT be a plain per-frame
// assignment: ApplyUiStateFromFenceRoot runs on every OnGUI while a fence is
// selected and would reset the browse position between two arrow clicks, so
// the user could never step past a locked type.
void SyncTypePreviewIndex()
{
    if ((int)selectedVariant != lastVariantSeen)
    { lastVariantSeen = (int)selectedVariant; previewVariant = selectedVariant; }
}

// The carousel steps through ALL five types so a LITE user can see what the
// full version adds. Locked types are preview-only: selectedVariant stays on
// a type that actually ships here, so no build path can look up a missing prefab.
void CycleFenceType(int dir)
{
    int n = System.Enum.GetValues(typeof(ContinueVariant)).Length;
    previewVariant = (ContinueVariant)((((int)previewVariant + dir) % n + n) % n);

    if (!IsTypeAvailable(previewVariant)) return;

    var prev = selectedVariant;
    selectedVariant = previewVariant;
    lastVariantSeen = (int)selectedVariant;
    if (selectedVariant != prev) SwitchFenceType(prev, selectedVariant);
}

// --- finish carousel: browse index vs. working index, same idea as the types ---
int newPreviewIndex;
int wornPreviewIndex;
int lastNewIdxSeen = -1;
int lastWornIdxSeen = -1;

// The browse indices follow the working ones whenever those change (fence
// selected, type switched, defaults applied), so none of the places that set
// newTextureIndex/wornTextureIndex has to know about them.
void SyncFinishPreviewIndices()
{
    if (newTextureIndex != lastNewIdxSeen)
    { lastNewIdxSeen = newTextureIndex; newPreviewIndex = newTextureIndex; }
    if (wornTextureIndex != lastWornIdxSeen)
    { lastWornIdxSeen = wornTextureIndex; wornPreviewIndex = wornTextureIndex; }
}

bool IsFinishAvailable(string tag, bool worn, int idx)
{
    EnsureFreeAvailability();
    return _availFinish.Contains(FinishKey(tag, worn, idx));
}

// Steps through all four finishes; only one that ships here is ever applied.
void CycleFinish(bool worn, int dir)
{
    int p = (((worn ? wornPreviewIndex : newPreviewIndex) + dir) % 4 + 4) % 4;
    if (worn) wornPreviewIndex = p; else newPreviewIndex = p;

    if (!IsFinishAvailable(VariantTag, worn, p)) return;

    if (worn)
    {
        string oldToken = "worn" + (wornTextureIndex + 1);
        wornTextureIndex = p;
        lastWornIdxSeen  = p;
        ApplyTextureVariantToSelectedFence(oldToken, "worn" + (p + 1));
    }
    else
    {
        string oldToken = "new" + (newTextureIndex + 1);
        newTextureIndex = p;
        lastNewIdxSeen  = p;
        ApplyTextureVariantToSelectedFence(oldToken, "new" + (p + 1));
    }
}

static string TypeNameOf(ContinueVariant v) => v switch
{
    ContinueVariant.V2 => "Privacy Fence",
    ContinueVariant.V3 => "Wrought Iron Fence",
    ContinueVariant.V4 => "Split Rail Fence",
    ContinueVariant.V5 => "Concrete Fence",
    _ => "Picket Fence"
};

// Badge sizes live in the stylesheet (.lock-badge-large / .lock-badge-small).
static readonly Color LockGoldCol = new Color(1f, 0.882f, 0.467f, 1f); // house gold #ffe177

Texture2D lockIconTex;
bool lockIconIsCustom; // shipped icon is drawn as authored, the fallback gets tinted

// Uses the padlock shipped with the package if there is one, otherwise falls
// back to Unity's built-in lock texture so the badge is never missing.
// The null check re-resolves after a re-import or domain reload (which destroy
// the cached texture object) and retries while the assets are still loading.
Texture2D GetLockIcon()
{
    if (lockIconTex != null) return lockIconTex;
    lockIconTex = FindAsset<Texture2D>("fence_LOCK_ICON");
    lockIconIsCustom = lockIconTex != null;
    if (lockIconTex == null)
    {
        var c = EditorGUIUtility.IconContent("IN LockButton on");
        if (c != null) lockIconTex = c.image as Texture2D;
    }
    return lockIconTex;
}

    [MenuItem("Tools/Fence Modular System")]
    static void Open()
    {
        var w = GetWindow<FenceModularWindow>("Fence Modular System");
        // Lower bound only. The former fixed 300x560 pinned the window even on a
        // large monitor and squeezed the preview panels; the layout scrolls now.
        w.minSize = new Vector2(300f, 420f);
        w.maxSize = new Vector2(4000f, 4000f);
        w.Show();
    }

void OnEnable()
{
    Selection.selectionChanged += OnSelectionChanged;
SceneView.duringSceneGui += OnSceneGUI_Overlay;
onHierarchyChanged = () =>
{
    CleanupFinalizedFences();
    RebuildProtectedPillarIdCache();
    RefreshUi();
};
EditorApplication.hierarchyChanged += onHierarchyChanged;
ObjectChangeEvents.changesPublished += OnObjectChanges_BlockPillarDelete;
Undo.undoRedoPerformed += OnUndoRedoPerformed;

// Scan immediately (works when scene is already loaded)
ScanSceneForExistingFences();
foreach (var root in finalizedFences)
    RebuildChainIndexForRoot(root);
RebuildProtectedPillarIdCache();

// Re-scan delayed (catches scene not yet ready on fresh import)
EditorApplication.delayCall += () =>
{
    EnsurePipelineMaterials();
    ScanSceneForExistingFences();
    foreach (var root in finalizedFences)
        RebuildChainIndexForRoot(root);
    RebuildProtectedPillarIdCache();
    LoadPreviewTextures();
    OnSelectionChanged();
    RefreshUi();
};

if (topPreviews == null) topPreviews = new Texture2D[4];
topPreviewIndex = 4; // start with "No Tops"
textureVariantIsWorn = false; // default = NEW
selectedVariant = ContinueVariant.V1; // hard default on window open
previewVariant  = ContinueVariant.V1;
}

void LoadPreviewTextures()
{
    variantV1Preview = FindAsset<Texture2D>("fence_V1_PREVIEW");
    variantV2Preview = FindAsset<Texture2D>("fence_V2_PREVIEW");
    variantV3Preview = FindAsset<Texture2D>("fence_V3_PREVIEW");
    variantV4Preview = FindAsset<Texture2D>("fence_V4_PREVIEW");
    variantV5Preview = FindAsset<Texture2D>("fence_V5_PREVIEW");

    topPreviews = new Texture2D[]
    {
        FindAsset<Texture2D>("top1_PREVIEW"),
        FindAsset<Texture2D>("top2_PREVIEW"),
        FindAsset<Texture2D>("top3_PREVIEW"),
        FindAsset<Texture2D>("top4_PREVIEW"),
    };
    topPreviewIndex = topPreviews.Length;
}

void OnSelectionChanged()
{
    if (suppressSelectionChanged) return;

    var sel = Selection.activeGameObject;

// Cache last rail/gate selection so we can react after user deletes it
bool selIsRailOrGate = sel && (IsRailInstance(sel) || GetGateType(sel) != 0);
lastSelectionWasRail   = selIsRailOrGate;
lastSelectionWasPillar = sel && IsPillarInstance(sel);
lastRailDeleteFenceRoot = selIsRailOrGate
    ? FindOwningFenceRoot(sel.transform)?.gameObject
    : null;

// Build instanceId → fenceRoot map for ALL selected rails/gates
foreach (var o in Selection.objects)
{
    var go = o as GameObject;
    if (!go || (!IsRailInstance(go) && GetGateType(go) == 0)) continue;
    var root = FindOwningFenceRoot(go.transform)?.gameObject;
    if (root) railIdToFenceRoot[go.StableId()] = root;

    // Capture snap positions before potential delete
    var s1 = FindSnap(go.transform, RailStartSnap);
    var s2 = FindSnap(go.transform, RailEndSnap);
    if (s1 && s2) pendingRailSnaps[go.StableId()] = (s1.position, s2.position);
}

// Rail selected → co-select connected pillars
// Single: orphan pillars only. Multi: pillars between selected rails.
bool isSingleSelect = Selection.objects.Length <= 1;
if (isSingleSelect) railCoSelectedPillarIds.Clear();
if (lastSelectionWasRail && sel)
{
    if (isSingleSelect)
    {
        var pillars = FindCoSelectPillarsForRail(sel, onlyOrphans: true);
        if (pillars.Count > 0)
        {
            foreach (var p in pillars)
                railCoSelectedPillarIds.Add(p.StableId());

            var capturedPillars = pillars.ToArray();
            var capturedSelection = Selection.objects;
            suppressSelectionChanged = true;
            EditorApplication.delayCall += () =>
            {
                var objs = new List<Object>(capturedSelection);
                foreach (var p in capturedPillars)
                    if (p && !objs.Contains(p)) objs.Add(p);
                if (objs.Count > capturedSelection.Length)
                    Selection.objects = objs.ToArray();
                EditorApplication.delayCall += () => suppressSelectionChanged = false;
            };
        }
    }
    else
    {
        // Multi-select: strip old co-selected pillars, find between-pillars
        var selectedRails = new List<GameObject>();
        var cleanSelection = new List<Object>();
        foreach (var o in Selection.objects)
        {
            var go = o as GameObject;
            if (go && railCoSelectedPillarIds.Contains(go.StableId())) continue;
            cleanSelection.Add(o);
            if (go && IsRailInstance(go)) selectedRails.Add(go);
        }
        railCoSelectedPillarIds.Clear();

        var pillars = FindPillarsBetweenRails(selectedRails.ToArray());
        foreach (var p in pillars)
            railCoSelectedPillarIds.Add(p.StableId());

        bool needsUpdate = pillars.Count > 0 || cleanSelection.Count < Selection.objects.Length;
        if (needsUpdate)
        {
            var capturedPillars = pillars.ToArray();
            var capturedClean = cleanSelection.ToArray();
            suppressSelectionChanged = true;
            EditorApplication.delayCall += () =>
            {
                var objs = new List<Object>(capturedClean);
                foreach (var p in capturedPillars)
                    if (p && !objs.Contains(p)) objs.Add(p);
                Selection.objects = objs.ToArray();
                EditorApplication.delayCall += () => suppressSelectionChanged = false;
            };
        }
    }
}

    // --- TRANSFORM GIZMO CONTROL ---
if (!sel)
{
    Tools.hidden = false;
    SetFenceGizmosVisible(true);
}
else if (finalizedFences.Contains(sel))
{
    Tools.hidden = false;          // Transform gizmos ON
    SetFenceGizmosVisible(false); // MeshCollider + LOD gizmos OFF
    SyncUiFromFenceRoot(sel);
    EnsureFencePivotCentered(sel); // full-version parity: center pivot on root selection
}
else
{
    // Child selected → hide transform + collider/LOD gizmos
    var root = FindOwningFenceRoot(sel.transform);
    bool isFenceChild = root != null;
    bool isRoot = isFenceChild && root.gameObject == sel;

    Tools.hidden = isFenceChild && !isRoot;
    SetFenceGizmosVisible(!isFenceChild);

    if (isRoot) SyncUiFromFenceRoot(sel);
}

    UpdateContinueBuildState();
    SceneView.RepaintAll();
    RefreshUi();
}

void OnDisable()
{
    Selection.selectionChanged -= OnSelectionChanged;
SceneView.duringSceneGui -= OnSceneGUI_Overlay;
if (onHierarchyChanged != null)
    EditorApplication.hierarchyChanged -= onHierarchyChanged;
ObjectChangeEvents.changesPublished -= OnObjectChanges_BlockPillarDelete;
Undo.undoRedoPerformed -= OnUndoRedoPerformed;
onHierarchyChanged = null;
    StopBuildMode();

SetFenceGizmosVisible(true);
Tools.hidden = false;
}

// ===================== UI (UI Toolkit) =====================
// Element lookups are resolved once in CreateGUI. RefreshUi runs on every
// selection change and on a timer, so walking the tree by name each time
// would be wasted work.
Button uiBuildBtn, uiUpsellBtn, uiFlipBtn, uiMirrorBtn;
Button uiTypePrev, uiTypeNext, uiNewPrev, uiNewNext, uiWornPrev, uiWornNext;
Toggle uiFullDetail;
VisualElement uiTypePreview, uiTypeLock, uiNewPreview, uiNewLock, uiWornPreview, uiWornLock;
Label uiTypeName, uiNewName, uiWornName, uiTextureDesc;

// Resolve THIS file's folder at runtime. The UXML/USS sit next to it, so the
// pack keeps working after a buyer renames or moves the folder.
string ToolFolder()
{
    var ms = MonoScript.FromScriptableObject(this);
    var p  = ms != null ? AssetDatabase.GetAssetPath(ms) : null;
    if (!string.IsNullOrEmpty(p))
        return System.IO.Path.GetDirectoryName(p).Replace('\\', '/');

    // Fallback: find the layout asset itself. In a precompiled build there is
    // no script asset to locate, but the .uxml always ships beside the assembly.
    foreach (var guid in AssetDatabase.FindAssets("FenceModularWindow t:VisualTreeAsset"))
    {
        var ap = AssetDatabase.GUIDToAssetPath(guid);
        if (ap.EndsWith("/FenceModularWindow.uxml"))
            return System.IO.Path.GetDirectoryName(ap).Replace('\\', '/');
    }
    return null;
}

public void CreateGUI()
{
    var root = rootVisualElement;
    root.Clear();

    var dir  = ToolFolder();
    var tree = dir != null
        ? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(dir + "/FenceModularWindow.uxml")
        : null;

    if (tree == null)
    {
        // Without the layout the window can do nothing - say so instead of staying empty.
        const string msg = "FenceModularWindow.uxml was not found next to FenceModularWindow.cs. "
                         + "Keep the Editor folder of Fence Modular System together (the .uxml and "
                         + ".uss must sit beside the script), then reopen the window.";
        root.Add(new HelpBox(msg, HelpBoxMessageType.Error));
        Debug.LogError("[Fence Modular System] " + msg);
        return;
    }

    tree.CloneTree(root);

    var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(dir + "/FenceModularWindow.uss");
    if (sheet != null) root.styleSheets.Add(sheet);

    uiBuildBtn    = root.Q<Button>("build-btn");
    uiUpsellBtn   = root.Q<Button>("upsell-btn");
    uiFlipBtn     = root.Q<Button>("flip-btn");
    uiMirrorBtn   = root.Q<Button>("mirror-gate-btn");
    uiTypePrev    = root.Q<Button>("type-prev");
    uiTypeNext    = root.Q<Button>("type-next");
    uiNewPrev     = root.Q<Button>("new-prev");
    uiNewNext     = root.Q<Button>("new-next");
    uiWornPrev    = root.Q<Button>("worn-prev");
    uiWornNext    = root.Q<Button>("worn-next");
    uiFullDetail  = root.Q<Toggle>("full-detail-toggle");
    uiTypePreview = root.Q<VisualElement>("type-preview");
    uiTypeLock    = root.Q<VisualElement>("type-lock");
    uiNewPreview  = root.Q<VisualElement>("new-preview");
    uiNewLock     = root.Q<VisualElement>("new-lock");
    uiWornPreview = root.Q<VisualElement>("worn-preview");
    uiWornLock    = root.Q<VisualElement>("worn-lock");
    uiTypeName    = root.Q<Label>("type-name");
    uiNewName     = root.Q<Label>("new-name");
    uiWornName    = root.Q<Label>("worn-name");
    uiTextureDesc = root.Q<Label>("texture-desc");

    uiBuildBtn.clicked  += () => { if (buildMode) StopBuildMode(); else StartBuildMode(); RefreshUi(); };
    uiUpsellBtn.clicked += () => Application.OpenURL(FullVersionUrl);
    uiFlipBtn.clicked   += () => { FlipSelectedFenceElements(); RefreshUi(); };
    uiMirrorBtn.clicked += () => { FlipSingleGateAroundSectionCenter(); RefreshUi(); };

    uiTypePrev.clicked += () => { CycleFenceType(-1);    RefreshUi(); };
    uiTypeNext.clicked += () => { CycleFenceType(+1);    RefreshUi(); };
    uiNewPrev.clicked  += () => { CycleFinish(false, -1); RefreshUi(); };
    uiNewNext.clicked  += () => { CycleFinish(false, +1); RefreshUi(); };
    uiWornPrev.clicked += () => { CycleFinish(true,  -1); RefreshUi(); };
    uiWornNext.clicked += () => { CycleFinish(true,  +1); RefreshUi(); };

    uiFullDetail.RegisterValueChangedCallback(evt =>
    {
        fullDetailMode = evt.newValue;
        ApplyFullDetailToAllFences(fullDetailMode);
    });

    // The finish columns are picked by clicking their image - no radio buttons.
    // Clicks that originate on an arrow are ignored here so the arrow only cycles.
    uiNewPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        SetTextureWorn(false);
    });
    uiWornPreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        SetTextureWorn(true);
    });

    // Clicking a locked type preview opens the paid listing.
    uiTypePreview.RegisterCallback<ClickEvent>(evt =>
    {
        if (evt.target is Button) return;
        if (!IsTypeAvailable(previewVariant)) Application.OpenURL(FullVersionUrl);
    });

    LoadPreviewTextures();
    RefreshUi();

    // IMGUI re-read the scene on every OnGUI frame. UI Toolkit does not redraw
    // by itself, so that polling moves onto a slow timer here.
    root.schedule.Execute(RefreshUi).Every(250);
}

void SetTextureWorn(bool worn)
{
    // Without a fence there is nothing to apply to, and RefreshUi would reset the
    // state on the next tick anyway - so the click would only flicker.
    if (FindFenceRootFromSelection(Selection.activeGameObject) == null) return;
    if (textureVariantIsWorn == worn) { RefreshUi(); return; }

    string from = worn ? "new"  + (newTextureIndex  + 1) : "worn" + (wornTextureIndex + 1);
    string to   = worn ? "worn" + (wornTextureIndex + 1) : "new"  + (newTextureIndex  + 1);
    ApplyTextureVariantToSelectedFence(from, to);
    textureVariantIsWorn = worn;
    RefreshUi();
}

// Everything the old OnGUI recomputed per frame, gathered in one place.
void RefreshUi()
{
    if (uiBuildBtn == null) return; // CreateGUI has not run yet

    CleanupFinalizedFences();
    UpdateContinueBuildState();

    var fenceRoot = FindFenceRootFromSelection(Selection.activeGameObject);
    bool hasFenceRoot = fenceRoot != null;

    if (fenceRoot)
    {
        ApplyUiStateFromFenceRoot(fenceRoot);
        UpdateVariantAvailabilityFromHierarchy();
        UpdateTextureVariantFromHierarchy();
    }
    else if (!buildMode && !continueAnchorActive)
    {
        // Defaults only when NOT building (Continue Build clears selection on purpose)
        topPreviewIndex = topPreviews.Length;
        textureVariantIsWorn = false;
        newTextureIndex  = FirstFinishIndex(VariantTag, false);
        wornTextureIndex = FirstFinishIndex(VariantTag, true);
    }

    SyncTypePreviewIndex();
    SyncFinishPreviewIndices();

    // ---- build ----
    uiBuildBtn.text = buildMode ? "Stop Build Mode" : "Start Build Mode";
    uiBuildBtn.EnableInClassList("btn-primary", !buildMode);
    uiBuildBtn.EnableInClassList("btn-stop", buildMode);
    uiFullDetail.SetValueWithoutNotify(fullDetailMode);

    // ---- fence type ----
    bool typeLocked = !IsTypeAvailable(previewVariant);
    SetPreviewImage(uiTypePreview, PreviewTextureForType(previewVariant));
    SetLockBadge(uiTypeLock, typeLocked);
    uiTypeName.text = typeLocked
        ? TypeNameOf(previewVariant) + "  (Full Version)"
        : TypeNameOf(previewVariant);
    uiTypeName.EnableInClassList("preview-caption-locked", typeLocked);
    uiUpsellBtn.text = typeLocked
        ? "Unlock " + TypeNameOf(previewVariant) + " - Get the Full Version"
        : "Get the Full Version  (all 5 types & finishes)";

    // ---- finishes ----
    // The active column is marked by its border, since the radios are gone.
    uiNewPreview.EnableInClassList("preview-active", !textureVariantIsWorn);
    uiWornPreview.EnableInClassList("preview-active", textureVariantIsWorn);

    bool newLocked = !IsFinishAvailable(VariantTag, false, newPreviewIndex);
    SetPreviewImage(uiNewPreview,
        FindAsset<Texture2D>("fence_" + VariantTag + "_new" + (newPreviewIndex + 1) + "_PREVIEW"));
    SetLockBadge(uiNewLock, newLocked);
    uiNewName.text = "New #" + (newPreviewIndex + 1) + (newLocked ? "  (Full)" : "");
    uiNewName.EnableInClassList("preview-caption-locked", newLocked);

    bool wornLocked = !IsFinishAvailable(VariantTag, true, wornPreviewIndex);
    SetPreviewImage(uiWornPreview,
        FindAsset<Texture2D>("fence_" + VariantTag + "_worn" + (wornPreviewIndex + 1) + "_PREVIEW"));
    SetLockBadge(uiWornLock, wornLocked);
    uiWornName.text = "Worn #" + (wornPreviewIndex + 1) + (wornLocked ? "  (Full)" : "");
    uiWornName.EnableInClassList("preview-caption-locked", wornLocked);

    // Only the controls are disabled without a fence, never the whole card - the
    // padlocks must stay at full opacity so they keep reading as a sales cue.
    uiNewPrev.SetEnabled(hasFenceRoot && !textureVariantIsWorn);
    uiNewNext.SetEnabled(hasFenceRoot && !textureVariantIsWorn);
    uiWornPrev.SetEnabled(hasFenceRoot && textureVariantIsWorn);
    uiWornNext.SetEnabled(hasFenceRoot && textureVariantIsWorn);
    uiTextureDesc.text = hasFenceRoot
        ? "Click New or Worn to apply it. The arrows cycle that column's finish."
        : "Select a fence in the Scene View to change its finish.";

    // ---- options ----
    uiFlipBtn.SetEnabled(hasFenceRoot);
    uiMirrorBtn.SetEnabled(IsSelectedSingleGate());
}

Texture2D PreviewTextureForType(ContinueVariant v) => v switch
{
    ContinueVariant.V2 => variantV2Preview,
    ContinueVariant.V3 => variantV3Preview,
    ContinueVariant.V4 => variantV4Preview,
    ContinueVariant.V5 => variantV5Preview,
    _ => variantV1Preview
};

static void SetPreviewImage(VisualElement ve, Texture2D tex)
{
    if (ve == null) return;
    ve.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
}

void SetLockBadge(VisualElement badge, bool locked)
{
    if (badge == null) return;
    badge.EnableInClassList("hidden", !locked);
    if (!locked) return;

    var tex = GetLockIcon();
    badge.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
    // The shipped padlock is drawn as authored; only the monochrome Unity
    // fallback gets tinted gold.
    badge.style.unityBackgroundImageTintColor = lockIconIsCustom ? Color.white : LockGoldCol;
}

static string VariantTagOf(ContinueVariant v) => v switch
{
    ContinueVariant.V2 => "V2",
    ContinueVariant.V3 => "V3",
    ContinueVariant.V4 => "V4",
    ContinueVariant.V5 => "V5",
    _ => "V1"
};

void SwitchFenceType(ContinueVariant from, ContinueVariant to)
{
    if (from == to) return;
    string fromTag = VariantTagOf(from);
    string toTag   = VariantTagOf(to);

    var root = GetUiTargetFenceRoot();
    if (root == null) return;

    // ReplaceFenceVariant destroys every element and rebuilds it, which would
    // drop whatever the user had selected. Parent and sibling index survive the
    // swap, so remember those and reselect the new elements afterwards.
    bool rootWasSelected = false;
    var picked = new List<(Transform parent, int sibling)>();
    foreach (var o in Selection.objects)
    {
        var go = o as GameObject;
        if (!go) continue;
        if (go == root) { rootWasSelected = true; continue; }
        if (FindFenceRootFromSelection(go) != root) continue;
        if (go.transform.parent) picked.Add((go.transform.parent, go.transform.GetSiblingIndex()));
    }

    ReplaceFenceVariant(fromTag, toTag);
    RemoveAllTopsFromSelectedFence();

    RestoreSelectionAfterVariantSwap(root, rootWasSelected, picked);
}

void RestoreSelectionAfterVariantSwap(GameObject root, bool rootWasSelected,
                                      List<(Transform parent, int sibling)> picked)
{
    if (!rootWasSelected && picked.Count == 0) return;

    var objs = new List<Object>();
    if (rootWasSelected && root) objs.Add(root);

    foreach (var (parent, sibling) in picked)
    {
        if (!parent || sibling < 0 || sibling >= parent.childCount) continue;
        var go = parent.GetChild(sibling).gameObject;
        if (go && !objs.Contains(go)) objs.Add(go);
    }

    if (objs.Count > 0) Selection.objects = objs.ToArray();
}

void FlipSelectedFenceElements()
{
    var toFlip = new List<Transform>();

    foreach (var obj in Selection.objects)
    {
        var go = obj as GameObject;
        if (!go) continue;

        if (finalizedFences.Contains(go))
        {
            foreach (Transform child in go.transform)
                if (IsFlippableElement(child.gameObject)) toFlip.Add(child);
        }
        else if (FindFenceRootFromSelection(go) != null && IsFlippableElement(go))
        {
            toFlip.Add(go.transform);
        }
    }

    if (toFlip.Count == 0) return;

    Undo.IncrementCurrentGroup();
    int undoGroup = Undo.GetCurrentGroup();

    foreach (var t in toFlip)
        FlipVisuals180(t.gameObject);

    Undo.CollapseUndoOperations(undoGroup);
}

// Only rails and gates are flippable
static bool IsFlippableElement(GameObject go)
{
    if (!go) return false;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
    if (!src) return false;
    string n = src.name;
    return (n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
            n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) &&
           n.EndsWith("_PREFAB");
}

// Mirror visuals around SnapPoint1 on local Z axis, leaving SnapPoints untouched
// Check if currently selected object is a single_gate_ prefab inside a fence
bool IsSelectedSingleGate()
{
    var sel = Selection.activeGameObject;
    if (!sel) return false;
    var src = PrefabUtility.GetCorrespondingObjectFromSource(sel);
    if (!src || !src.name.StartsWith("single_gate_")) return false;
    return FindFenceRootFromSelection(sel) != null;
}

// Flip a single-gate by mirroring its transform on local X around the midpoint
// of the two pillars. Swaps SnapPoint names afterwards so SnapPoint1 still
// references the left pillar (required for variant swap to work correctly).
void FlipSingleGateAroundSectionCenter()
{
    var gate = Selection.activeGameObject;
    if (!gate) return;

    var sp1 = FindSnap(gate.transform, "SnapPoint1");
    var sp2 = FindSnap(gate.transform, "SnapPoint2");
    if (!sp1 || !sp2) return;

    Vector3 pillarMid = (sp1.position + sp2.position) * 0.5f;

    Undo.RecordObject(gate.transform, "Flip Single-Gate");

    var s = gate.transform.localScale;
    s.x *= -1f;
    gate.transform.localScale = s;

    Vector3 midAfter = (sp1.position + sp2.position) * 0.5f;
    gate.transform.position += pillarMid - midAfter;

    // Swap snap names: after mirror, SnapPoint1 sits on the right pillar.
    // Rename so SnapPoint1 still references the left pillar.
    Undo.RecordObject(sp1, "Flip Single-Gate");
    Undo.RecordObject(sp2, "Flip Single-Gate");
    sp1.name = "__tmp_swap__";
    sp2.name = "SnapPoint1";
    sp1.name = "SnapPoint2";
}

static void FlipVisuals180(GameObject go)
{
    if (!go) return;
    var snap = FindSnap(go.transform, "SnapPoint1");
    float pivotZ = snap ? go.transform.InverseTransformPoint(snap.position).z : 0f;

    // Flip all direct children except SnapPoints
    foreach (Transform child in go.transform)
    {
        if (!child) continue;
        if (child.name.StartsWith("SnapPoint")) continue;

        if (!Undo.isProcessing)
            Undo.RecordObject(child, "Flip Visual");

        // Mirror position around pivot in root-local Z
        Vector3 localPos = child.localPosition;
        localPos.z = 2f * pivotZ - localPos.z;
        child.localPosition = localPos;

        // Flip on Z
        var s = child.localScale;
        s.z *= -1f;
        child.localScale = s;
    }
}

}
} // namespace WB3DAssets.FenceModularSystem
