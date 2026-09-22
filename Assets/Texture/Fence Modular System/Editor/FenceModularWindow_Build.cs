using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{
    void StartBuildMode()
    {
currentBuildObjects.Clear();
// FORCE texture variant to NEW when entering Build Mode
textureVariantIsWorn = false;
        ghostMat = FindAsset<Material>(GhostMatName);
var prefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
        if (!ghostMat || !prefab) return;

ghost = CreateGhost(prefab);

// IMPORTANT: Ghost starts completely neutral
ghost.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

// absolute reset
lastPillarSnap = null;
activeDir = Vector3.right;

        buildMode = true;
        state = State.FreeMove;
        SceneView.duringSceneGui += OnSceneGUI;
Selection.activeObject = null;
Repaint();
    }

    void StopBuildMode()
    {
// Abort behavior:
// - Continue build abort should finalize into the existing target fence
// - Normal build abort should create a new fence (existing behavior)
// ABORT = hard cancel, do NOT place V1E or finalize anything
// ABORT: parent already placed objects into a fence, but do NOT finalize
if (currentBuildObjects.Count > 0)
{
    // Replace last M-pillar with E-pillar (so chain ends correctly on ESC)
    if (lastPlacedPillarM)
    {
        // Find last rail in currentBuildObjects (scan backwards)
        GameObject lastRail = null;
        for (int i = currentBuildObjects.Count - 1; i >= 0; i--)
        {
            var go = currentBuildObjects[i];
            if (!go) continue;
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (!src) continue;
            string n = src.name;
            if ((n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_")) && n.EndsWith("_PREFAB"))
            { lastRail = go; break; }
        }

        var railEndSnap = lastRail ? FindSnap(lastRail.transform, RailEndSnap) : null;
        if (railEndSnap)
        {
            FinalizeChain(lastPlacedPillarM.transform, railEndSnap, null);
            currentBuildObjects.RemoveAll(o => !o); // strip null after M->E swap
        }
    }

    GameObject fenceRoot;

    // Continue Build → reuse existing fence
    if (continueTargetFence)
    {
        fenceRoot = continueTargetFence;
    }
    else
    {
        fenceRoot =
            new GameObject($"Fence_{GetNextFenceNumber()}");
        Undo.RegisterCreatedObjectUndo(fenceRoot, "Create Fence (Abort)");
        finalizedFences.Add(fenceRoot);

        // Mark start pillar for abort too
        if (currentBuildObjects.Count > 0 && currentBuildObjects[0])
        {
            fenceStartPillars[fenceRoot] = currentBuildObjects[0];
            EnsureStartMarker(fenceRoot, currentBuildObjects[0]);
            var sd = FindSnap(currentBuildObjects[0].transform, "SnapPoint1");
        }
    }

    foreach (var go in currentBuildObjects)
    {
        if (go)
            go.transform.SetParent(fenceRoot.transform, true);
    }

    // Only recenter pivot for NEW fences; skip for continue builds
    // to avoid moving the existing root (which causes undo artifacts).
    if (!continueTargetFence)
        CenterFencePivot(fenceRoot);

// --- CHAIN INDEX ASSIGNMENT (ABORT) ---
int startIdxForThisAbort = 0;

if (continueTargetFence)
{
    var cache = GetOrCreateIndexCache(fenceRoot);
    startIdxForThisAbort = cache != null ? cache.nextIndex : 0;
}

AssignIndices(fenceRoot, currentBuildObjects, startIdxForThisAbort);

// Apply Full Detail Mode to the entire finalized fence
if (fullDetailMode)
    ApplyFullDetailToFence(fenceRoot, true);
}

currentBuildObjects.Clear();

        buildMode = false;
        state = State.Idle;
        SceneView.duringSceneGui -= OnSceneGUI;
Selection.activeObject = null;

        if (ghost) DestroyImmediate(ghost);
        ghost = null;

ClearRailPreview();
ClearDirSelectHoverChain();
lastPillarSnap = null;
lastPlacedPillarE = null;
lastPlacedPillarM = null;

// RESET build direction for next build
activeDir = Vector3.right;

ClearCurvedGhost();
ClearHover90Preview();
curvedGhostActive = false;
curvedOutSnapName = null;

// If Continue Build was aborted, restore a valid snap from V1E
if (continueAnchorPillar)
{
    var snap = FindSnap(continueAnchorPillar.transform, "SnapPoint2");
    if (!snap)
        snap = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

    if (snap)
        lastPillarSnap = snap;

    ShowContinueAnchorPillar();
}

continueAnchorPillar = null;
continueAnchorActive = false;

if (continueGhostPillarM)
    DestroyImmediate(continueGhostPillarM);
continueGhostPillarM = null;

if (continueSnapProxy)
    DestroyImmediate(continueSnapProxy.gameObject);
continueSnapProxy = null;

// Invalidate cached positions and ghost snaps when elements were added to existing fence
if (continueTargetFence)
{
    deletedRailIdsByRoot.Remove(continueTargetFence);
}
continueTargetFence = null;
continueScale = Vector3.one;
continueTopIndex = -1;

isV1MContinueMode = false; // Reset V1M mode
continueFlipElements = false;

// Collapse all continue-build operations into a single undo step
if (continueUndoGroup >= 0)
{
    Undo.CollapseUndoOperations(continueUndoGroup);
    continueUndoGroup = -1;
}

Repaint();
    }

void OnSceneGUI_Overlay(SceneView sv)
{
// --- DELETE KEY: strip co-selected pillars from selection so only rails get deleted ---
if (railCoSelectedPillarIds.Count > 0 && Event.current.type == EventType.KeyDown &&
    (Event.current.keyCode == KeyCode.Delete || Event.current.keyCode == KeyCode.Backspace))
{
    var keep = new List<Object>();
    foreach (var obj in Selection.objects)
    {
        var go = obj as GameObject;
        if (go && railCoSelectedPillarIds.Contains(go.StableId()))
            continue;
        keep.Add(obj);
    }
    if (keep.Count < Selection.objects.Length)
    {
        suppressSelectionChanged = true;
        Selection.objects = keep.ToArray();
        suppressSelectionChanged = false;
    }
}

if (Event.current.type == EventType.MouseMove)
    sv.Repaint();

    if (buildMode || continueAnchorActive)
        return;

// V1E only: show Continue Build arrow
if (canContinueBuild && selectedContinuePillar)
    DrawContinueDoubleArrowGizmo(selectedContinuePillar.transform);

// Show gate replace gizmo when a rail is selected
DrawGateReplaceGizmo(sv);
}

    void OnSceneGUI(SceneView sv)
    {
// --- CONTINUE BUILD ARROW GIZMO (V1E only) ---
if (canContinueBuild && selectedContinuePillar)
    DrawContinueDoubleArrowGizmo(selectedContinuePillar.transform);

if (!buildMode || (!ghost && !continueAnchorActive)) return;
        var e = Event.current;

if (e.type == EventType.MouseMove)
    sv.Repaint();

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            StopBuildMode();
            e.Use();
            return;
        }

        if (!e.alt)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        if (state == State.FreeMove)
        {
            ghost.transform.position = MouseOnSurface(e.mousePosition, ghost.transform.position);

if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
{
frozenPos = ghost.transform.position;

// HARD LOCK of start basis
activeDir = Vector3.right;

    state = State.DirectionSelect;
    e.Use();
}
        }
        else if (state == State.DirectionSelect)
        {
            var mw = MouseOnPlane(e.mousePosition, frozenPos);
            activeDir = PickDir(mw - frozenPos, activeDir);
            
            // Ghost stays at frozenPos and only rotates around its own axis
            ghost.transform.position = frozenPos;
            
            // Calculate rotation ABSOLUTE: SnapPoint1.right (local X+) should point in activeDir
            Quaternion targetRot = Quaternion.LookRotation(
                Vector3.Cross(Vector3.up, activeDir).normalized,
                Vector3.up
            ) * Quaternion.Euler(0, 0, 0);
            
            ghost.transform.rotation = targetRot;
            
            DrawDirGizmos(frozenPos, activeDir);
            
            // Show ghost chain preview in hover direction
            UpdateDirectionSelectHoverChain(activeDir);

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                CommitDirectionSelectWithChain();
                e.Use();
            }
        }
else if (state == State.RailPreview)
{
    var axis = activeDir.normalized;
    var mw = MouseOnPlane(e.mousePosition, railAnchorPos);

    float dist = Vector3.Dot(mw - railAnchorPos, axis);
    dist = Mathf.Max(0.01f, dist);

    int segCount = Mathf.Max(1, Mathf.FloorToInt(dist / segLen));
    EnsureRailSegs(segCount);
    LayoutRailSegs(axis);

if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
{
    // 1) FINALIZE: Convert ghost chain to REAL rails + pillars
    CommitRailSegs(null);
    ClearRailPreview();  // Clear OLD ghost system completely

// 2) Set up Continue Build Mode internally (manual setup, no root needed yet)
if (lastPlacedPillarM)
{
    continueAnchorPillar = lastPlacedPillarM;
    continueAnchorActive = true;

    // Hide the anchor pillar
    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;

    // Ensure ghost material exists
    if (!ghostMat)
        ghostMat = FindAsset<Material>(GhostMatName);

// Find the connected snap (the one WITH a rail attached)
    // After RailPreview, snap1 is connected to the rail, snap2 is FREE
    var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
    var snap2 = FindSnap(continueAnchorPillar.transform, "SnapPoint2");

    Transform connectedSnap = snap1; // snap1 is connected to the previous rail

    // Setup snap proxy
    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(connectedSnap.position, connectedSnap.rotation);

lastPillarSnap = continueSnapProxy;
// snap2.right points INTO the V1M (opposite to build direction)
// For the next build direction we need to flip
activeDir = -continueSnapProxy.right; // flip 180° - same as EnterContinueAnchorMode
activeDir.y = 0f;
if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

    // Create Ghost V1M
    if (continueGhostPillarM)
        DestroyImmediate(continueGhostPillarM);

var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (pillarMPrefab)
    {
        continueGhostPillarM = CreateGhost(pillarMPrefab);
        continueGhostPillarM.name = "ContinueGhostV1M";

        var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
        if (ghostSnap && lastPillarSnap)
        {
// Ghost SnapPoint1.right must point INTO the snap (opposite to activeDir)
continueGhostPillarM.transform.rotation =
                YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

            continueGhostPillarM.transform.position +=
                lastPillarSnap.position - ghostSnap.position;
        }

        // Add ghost top if fence has tops
        AddGhostTopToPillar(continueGhostPillarM);
    }
}

state = State.CornerSelect;
e.Use();
}
}
else if (state == State.CornerSelect)
{
if (!(hasCurvedRails && curvedGhostActive) &&
    railSegs.Count == 0 &&
    ghostPillarsM.Count == 0 &&
    lastPlacedPillarM == null &&
    !continueAnchorActive)
{
    // allow back arrow in continue mode even without any commits
    return;
}

Transform railEndSnap;
Transform lastPillarM;

if (!(hasCurvedRails && curvedGhostActive))
{
    // Normal build: use ghost chain
    if (railSegs.Count > 0 && ghostPillarsM.Count > 0)
    {
        lastPillarM = ghostPillarsM[^1].transform;
        railEndSnap = FindSnap(railSegs[^1].transform, RailEndSnap);
    }
    // Continue build: no ghost chain yet, use existing anchor
else if (continueAnchorActive && lastPillarSnap != null)
{
    // Continue Build: use hidden V1E as virtual last pillar
    lastPillarM = continueAnchorPillar.transform;
    railEndSnap = lastPillarSnap;
}
else if (lastPlacedPillarM != null && lastPillarSnap != null)
{
    lastPillarM = lastPlacedPillarM.transform;
    railEndSnap = lastPillarSnap;
}
else
{
    return;
}
}
else
{
    lastPillarM = ghostCurvedPillar.transform;
    railEndSnap = curvedGhostEndSnap;
}

if (!railEndSnap || !lastPillarM)
    return;

// Base for STRAIGHT / INNER ARC
Transform basePillarSnap =
    (continueAnchorActive && continueGhostPillarM)
        ? FindSnap(continueGhostPillarM.transform, "SnapPoint2")
        : (continueAnchorActive
            ? lastPillarSnap
            : FindSnap(lastPillarM, "SnapPoint2"));

if (!basePillarSnap) return;

// Base for 90° / 45° / OUTER ARC
Transform baseRailSnap = railEndSnap;

// Stable origin for angle picking (do NOT depend on hover flags here)
Vector3 mouseBasePos =
    (hasCurvedRails && curvedGhostActive)
        ? ghostCurvedPillar.transform.position
        : (continueAnchorActive
            ? lastPillarSnap.position
            : lastPillarM.position);

    Vector3 backDir = -activeDir.normalized;

    // base directions
    Vector3 side    = Vector3.Cross(Vector3.up, activeDir);
    Vector3 left45  = (activeDir - side).normalized;
    Vector3 right45 = (activeDir + side).normalized;

    // mouse direction (hover)
Vector3 mouseDir = MouseOnPlane(Event.current.mousePosition, mouseBasePos) - mouseBasePos;
    mouseDir.y = 0f;
    mouseDir.Normalize();

// Signed angle around Y axis (0° = forward, +left, -right)
float sideAngle = Vector3.SignedAngle(side, mouseDir, Vector3.up);
float absSideAngle = Mathf.Abs(sideAngle);

float forwardAngle = Vector3.SignedAngle(activeDir, mouseDir, Vector3.up);
float absForwardAngle = Mathf.Abs(forwardAngle);

Vector3 hoverDir = Vector3.zero;
bool hoverLeft = false;
bool hoverRight = false;
bool hoverLeftOuter = false;
bool hoverRightOuter = false;

// Absolute angle zones (NO gaps, NO overlap)
// 0° ........ straight
// 22.5° ..... diagonal
// 45° ....... arc start
// 67.5° ..... arc center
// 90° ....... side
// 180° ...... back

// Signed angle relative to activeDir
// 0° = forward, +left, -right
float angle = Vector3.SignedAngle(activeDir, mouseDir, Vector3.up);

float handleScale = HandleUtility.GetHandleSize(mouseBasePos);
float r = ArrowLength * 1.6f * handleScale;
float visualR = r;

// ----- V1M CONTINUE MODE: CUSTOM ANGLE ZONES -----
if (isV1MContinueMode)
{
    // STRAIGHT (Forward)
    if (Mathf.Abs(angle) <= 30f)
    {
        hoverDir = activeDir;
    }
    // INNER ARC LEFT
    else if (hasCurvedRails && angle > 30f && angle <= 90f)
    {
        hoverLeft = true;
    }
    // INNER ARC RIGHT
    else if (hasCurvedRails && angle < -30f && angle >= -90f)
    {
        hoverRight = true;
    }
    // BACK (Finish)
    else if (Mathf.Abs(angle) >= 90f)
    {
        hoverDir = backDir;
    }
}
// ----- NORMAL MODE: FULL ANGLE ZONES -----
else
{
    // ----- UNIQUE ANGLE ZONES PER ARROW -----
    // FORWARD
    if (Mathf.Abs(angle) <= 12f)
    {
        hoverDir = activeDir;
    }
// 45° RIGHT
else if (hasCurvedRails && angle > 36f && angle <= 60f)
{
    hoverDir = right45;
}
// RIGHT ARC (INNER)
else if (hasCurvedRails && angle > 12f && angle <= 36f)
{
    hoverLeft = true;
}
// RIGHT OUTER ARC
else if (hasCurvedRails && angle > 60f && angle <= 84f)
{
    hoverRightOuter = true;
}
// RIGHT SIDE (90°)
else if (angle > (hasCurvedRails ? 84f : 12f) && angle <= 108f)
{
    hoverDir = side;
}
// LEFT SIDE (90°)
else if (angle < -(hasCurvedRails ? 84f : 12f) && angle >= -108f)
{
    hoverDir = -side;
}
// LEFT OUTER ARC
else if (hasCurvedRails && angle < -60f && angle >= -84f)
{
    hoverLeftOuter = true;
}
// LEFT ARC (INNER)
else if (hasCurvedRails && angle < -12f && angle >= -36f)
{
    hoverRight = true;
}
// 45° LEFT
else if (hasCurvedRails && angle < -36f && angle >= -60f)
{
    hoverDir = left45;
}
// BACK (FINISH - allowed to overlap)
    else if (Mathf.Abs(angle) >= 108f)
    {
        hoverDir = backDir;
    }
} // End of else block for Normal Mode

// Arcs override direction arrows (inner + outer)
if (hoverLeft || hoverRight)
{
    hoverDir = Vector3.zero;
}

// --- Continue Ghost + Anchor visibility control ---
// Ghost V1M visible ONLY for straight + inner arc
// Hidden V1E becomes visible ONLY when hovering back arrow
if (continueAnchorActive)
{
    if (continueGhostPillarM)
    {
        bool showContinueGhost =
            hoverDir == activeDir ||   // straight
            hoverLeft || hoverRight;   // inner arc

        continueGhostPillarM.SetActive(showContinueGhost);
    }

    if (hoverDir == backDir)
        ShowContinueAnchorPillar();
    else
        HideContinueAnchorPillar();
}

// Preview origin FIX:
// As soon as a rail exists, ALL direction gizmos must snap to the rail end.
Vector3 pos =
    (hasCurvedRails && curvedGhostActive)
        ? ghostCurvedPillar.transform.position
        : (railSegs.Count > 0
            ? baseRailSnap.position
            : basePillarSnap.position);

// If we leave inner arc hover, immediately clear curved hover preview
if (hover90Root && !(hoverLeft || hoverRight))
{
    ClearHover90Preview();
}

// Clear corner previews when not hovering any turn direction
if (hover90Root &&
    hoverDir != side &&
    hoverDir != -side &&
    hoverDir != left45 &&
    hoverDir != right45 &&
    hoverDir != activeDir)
{
    ClearHover90Preview();
}

// ---------- HOVER PREVIEW: STRAIGHT (VISUAL ONLY) ----------
if (hoverDir == activeDir)
{
    // IMPORTANT: do NOT hide last V1M for straight preview

    // Initialize hover root and calculate segment length once
    if (!hover90Root)
    {
        ClearHover90Preview();

        hover90Root = new GameObject("HoverStraightPreview");
        hover90Root.hideFlags = HideFlags.HideAndDontSave;

        // Store anchor position and direction for chain layout
        hoverChainAnchorPos = basePillarSnap.position;
        hoverChainDir = activeDir;
        hoverChainStartSnap = basePillarSnap; // Cache the start snap
        
        // Calculate segment length from a temporary rail
        var railPrefab = FindAsset<GameObject>(
            $"sectionB_{VariantTag}_PREFAB"
        );
        if (!railPrefab) return;
        
        var tempRail = CreateGhost(railPrefab, hover90Root.transform);
        var rs = FindSnap(tempRail.transform, RailStartSnap);
        var re = FindSnap(tempRail.transform, RailEndSnap);
        if (!rs || !re) { DestroyImmediate(tempRail); return; }
        
        hoverChainSegLen = Vector3.Distance(rs.position, re.position);
        DestroyImmediate(tempRail);
        
        // Include pillar width so chain length matches actual layout
        hoverChainSegLen += MeasurePillarWidth();
    }

    // Calculate segment count based on mouse position (every frame)
    var mw = MouseOnPlane(Event.current.mousePosition, hoverChainAnchorPos);
    float dist = Vector3.Dot(mw - hoverChainAnchorPos, hoverChainDir.normalized);
    dist = Mathf.Max(0.01f, dist);
    int segCount = Mathf.Max(1, Mathf.FloorToInt(dist / hoverChainSegLen));
    
    // Update chain using the cached start snap
    EnsureHoverChainSegs(segCount);
    LayoutHoverChain(hoverChainDir, hoverChainStartSnap);
}

// ---------- HOVER PREVIEW: INNER ARC (CURVED RAIL, VISUAL ONLY) ----------
if (hasCurvedRails && (hoverLeft || hoverRight))
{

    HideLastGhostPillarM(); // inner arc replaces the last V1M visually as well

    if (!hover90Root)
    {
        ClearHover90Preview();
//HideLastGhostPillarM();

        hover90Root = new GameObject("HoverCurvedPreview");
        hover90Root.hideFlags = HideFlags.HideAndDontSave;

        bool turnRight = hoverLeft;

        // Base snap = FREE snap of last ghost V1M
Transform startSnap = basePillarSnap;

        Vector3 desiredDir =
            turnRight
                ? Vector3.Cross(Vector3.up, activeDir).normalized
                : Vector3.Cross(activeDir, Vector3.up).normalized;

        // --- Ghost Curved Rail ---
        var curvedPrefab = FindAsset<GameObject>(
    $"sectionBCrvd_{VariantTag}_PREFAB"
);

        // Create two candidates, place each, pick the best
        var candA = CreateGhost(curvedPrefab);
        ApplyCurvedRailVisualVariation(candA);
        var candB = CreateGhost(curvedPrefab);
        ApplyCurvedRailVisualVariation(candB);

        var a1 = FindSnap(candA.transform, RailStartSnap);
        var a2 = FindSnap(candA.transform, RailEndSnap);
        var b1 = FindSnap(candB.transform, RailStartSnap);
        var b2 = FindSnap(candB.transform, RailEndSnap);
        if (!a1 || !a2 || !b1 || !b2) return;

        // Place A: SnapPoint1 = input (same as ApplyCornerPlacement)
        candA.transform.rotation = YawDelta(a1.right, -activeDir) * candA.transform.rotation;
        candA.transform.position += startSnap.position - a1.position;
        Vector3 outDirA = a2.right.normalized;

        // Place B: SnapPoint2 = input
        candB.transform.rotation = YawDelta(b2.right, -activeDir) * candB.transform.rotation;
        candB.transform.position += startSnap.position - b2.position;
        Vector3 outDirB = b1.right.normalized;

        bool useA = Vector3.Dot(outDirA, desiredDir) >= Vector3.Dot(outDirB, desiredDir);

        if (useA)
        {
            hover90Rail = candA;
            DestroyImmediate(candB);
        }
        else
        {
            hover90Rail = candB;
            DestroyImmediate(candA);
        }
        hover90Rail.transform.SetParent(hover90Root.transform, true);

        Transform outSnap = useA ? a2 : b1;
        Vector3 newDir = useA ? outDirA : outDirB;

// --- Ghost Pillar V1M ---
var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
        hover90PillarM = CreateGhost(pillarMPrefab);
        var ps = FindSnap(hover90PillarM.transform, PillarSnapName);
        if (!ps) return;

        hover90PillarM.transform.rotation =
            YawDelta(ps.right, -newDir) * hover90PillarM.transform.rotation;
        hover90PillarM.transform.position += outSnap.position - ps.position;
        hover90PillarM.transform.SetParent(hover90Root.transform, true);

        // Add ghost top if fence has tops
        AddGhostTopToPillar(hover90PillarM);

        // Flip ghosts when continuing from reverse direction
        if (continueFlipElements)
        {
            FlipVisuals180(hover90Rail);
        }
    }
}

// ---------- HOVER PREVIEW: OUTER ARC (CURVED + V1C, VISUAL ONLY) ----------
if (hasCurvedRails && (hoverLeftOuter || hoverRightOuter) && baseRailSnap != null && !isV1MContinueMode)
{
    if (!hover90Root)
    {
        ClearHover90Preview();

        HideLastGhostPillarM();
        HideCurvedGhostEndPillar();

        hover90Root = new GameObject("HoverOuterCurvedPreview");
        hover90Root.hideFlags = HideFlags.HideAndDontSave;

        bool isLeftOuter = hoverLeftOuter;

        // Base snap = last straight rail end
Transform baseRailEndSnap = baseRailSnap;

        // --- 1) Ghost V1C ---
var pillarCPrefab = FindAsset<GameObject>(GetPillarPrefabName('C'));

        var ocA = CreateGhost(pillarCPrefab);
        var ocB = CreateGhost(pillarCPrefab);

        var oca1 = FindSnap(ocA.transform, "SnapPoint1");
        var oca2 = FindSnap(ocA.transform, "SnapPoint2");
        var ocb1 = FindSnap(ocB.transform, "SnapPoint1");
        var ocb2 = FindSnap(ocB.transform, "SnapPoint2");
        if (!oca1 || !oca2 || !ocb1 || !ocb2) return;

        Vector3 cornerDir =
            isLeftOuter
                ? Vector3.Cross(activeDir, Vector3.up).normalized
                : Vector3.Cross(Vector3.up, activeDir).normalized;

        // Place A: SnapPoint1 = input
        ocA.transform.rotation = YawDelta(oca1.right, -activeDir) * ocA.transform.rotation;
        ocA.transform.position += baseRailEndSnap.position - oca1.position;
        float scoreA = Vector3.Dot(oca2.right.normalized, cornerDir.normalized);

        // Place B: SnapPoint2 = input
        ocB.transform.rotation = YawDelta(ocb2.right, -activeDir) * ocB.transform.rotation;
        ocB.transform.position += baseRailEndSnap.position - ocb2.position;
        float scoreB = Vector3.Dot(ocb1.right.normalized, cornerDir.normalized);

        Transform cOutSnap;
        if (scoreA >= scoreB)
        {
            hover90EndPillarE = ocA; DestroyImmediate(ocB);
            cOutSnap = oca2;
        }
        else
        {
            hover90EndPillarE = ocB; DestroyImmediate(ocA);
            cOutSnap = ocb1;
        }
        hover90EndPillarE.transform.SetParent(hover90Root.transform, true);

        // --- 2) Ghost Curved Rail ---
        var curvedPrefab = FindAsset<GameObject>(
    $"sectionBCrvd_{VariantTag}_PREFAB"
);
        hover90Rail = CreateGhost(curvedPrefab); // No parent — place standalone
ApplyCurvedRailVisualVariation(hover90Rail);

        // FIXED SNAP RULE
        Transform railInputSnap = isLeftOuter
            ? FindSnap(hover90Rail.transform, RailStartSnap) // LEFT OUTER → SnapPoint1
            : FindSnap(hover90Rail.transform, RailEndSnap);  // RIGHT OUTER → SnapPoint2

        Transform railOutputSnap = isLeftOuter
            ? FindSnap(hover90Rail.transform, RailEndSnap)
            : FindSnap(hover90Rail.transform, RailStartSnap);

        if (!railInputSnap || !railOutputSnap) return;

        hover90Rail.transform.rotation =
            YawDelta(railInputSnap.right, -cornerDir) * hover90Rail.transform.rotation;
        hover90Rail.transform.position +=
            cOutSnap.position - railInputSnap.position;
        hover90Rail.transform.SetParent(hover90Root.transform, true);

// OUTER arc does NOT change build direction
Vector3 newDir = activeDir;

        // --- 3) Ghost Pillar V1M ---
var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
        hover90PillarM = CreateGhost(pillarMPrefab); // No parent

        var ps = FindSnap(hover90PillarM.transform, PillarSnapName);
        if (!ps) return;

        hover90PillarM.transform.rotation =
            YawDelta(ps.right, -newDir) * hover90PillarM.transform.rotation;
        hover90PillarM.transform.position +=
            railOutputSnap.position - ps.position;
        hover90PillarM.transform.SetParent(hover90Root.transform, true);

        // Add ghost tops if fence has tops
        AddGhostTopToPillar(hover90EndPillarE);
        AddGhostTopToPillar(hover90PillarM);

        // Flip ghosts when continuing from reverse direction
        if (continueFlipElements)
        {
            FlipVisuals180(hover90Rail);
        }
    }
}

// ---------- HOVER PREVIEW: RIGHT 90° (VISUAL ONLY) ----------
if ((hoverDir == side || hoverDir == -side) && !isV1MContinueMode)
{
    bool turnRight = hoverDir == side;
    Vector3 newDir =
        turnRight
            ? Vector3.Cross(Vector3.up, activeDir).normalized
            : Vector3.Cross(activeDir, Vector3.up).normalized;

    // Initialize hover root and corner pillar once
    if (!hover90Root)
    {
        ClearHover90Preview();

        HideLastGhostPillarM();
        HideCurvedGhostEndPillar();

        hover90Root = new GameObject("Hover90Preview");
        hover90Root.hideFlags = HideFlags.HideAndDontSave;

        // Starting point: last STRAIGHT rail end
        Transform baseRailEndSnap = baseRailSnap;

// --- 1) Ghost V1C (ersetzt V1M visuell) ---
        var pillarCPrefab = FindAsset<GameObject>(GetPillarPrefabName('C'));

        // Create two candidates
        var cCandA = CreateGhost(pillarCPrefab);
        AddGhostTopToPillar(cCandA);
        var cCandB = CreateGhost(pillarCPrefab);
        AddGhostTopToPillar(cCandB);

        var ca1 = FindSnap(cCandA.transform, "SnapPoint1");
        var ca2 = FindSnap(cCandA.transform, "SnapPoint2");
        var cb1 = FindSnap(cCandB.transform, "SnapPoint1");
        var cb2 = FindSnap(cCandB.transform, "SnapPoint2");
        if (!ca1 || !ca2 || !cb1 || !cb2) return;

        // Place A: SnapPoint1 = input
        cCandA.transform.rotation = YawDelta(ca1.right, -activeDir) * cCandA.transform.rotation;
        cCandA.transform.position += baseRailEndSnap.position - ca1.position;
        Vector3 outDirA = ca2.right.normalized;

        // Place B: SnapPoint2 = input
        cCandB.transform.rotation = YawDelta(cb2.right, -activeDir) * cCandB.transform.rotation;
        cCandB.transform.position += baseRailEndSnap.position - cb2.position;
        Vector3 outDirB = cb1.right.normalized;

        bool useA = Vector3.Dot(outDirA, newDir) >= Vector3.Dot(outDirB, newDir);

        if (useA)
        {
            hover90EndPillarE = cCandA;
            DestroyImmediate(cCandB);
        }
        else
        {
            hover90EndPillarE = cCandB;
            DestroyImmediate(cCandA);
        }
        hover90EndPillarE.transform.SetParent(hover90Root.transform, true);

        Transform cOutSnap = useA ? ca2 : cb1;

        // Store anchor position and direction for chain layout
        hoverChainAnchorPos = cOutSnap.position;
        hoverChainDir = newDir;
        hoverChainStartSnap = cOutSnap; // Cache the correct output snap
        
        // Calculate segment length from a temporary rail
        var railPrefab = FindAsset<GameObject>(
            $"sectionB_{VariantTag}_PREFAB"
        );
        if (!railPrefab) return;
        
        var tempRail = CreateGhost(railPrefab, hover90Root.transform);
        var rs = FindSnap(tempRail.transform, RailStartSnap);
        var re = FindSnap(tempRail.transform, RailEndSnap);
        if (!rs || !re) { DestroyImmediate(tempRail); return; }
        
        hoverChainSegLen = Vector3.Distance(rs.position, re.position);
        DestroyImmediate(tempRail);
        
        // Include pillar width so chain length matches actual layout
        hoverChainSegLen += MeasurePillarWidth();

    }

    // Calculate segment count based on mouse position (every frame)
    var mw = MouseOnPlane(Event.current.mousePosition, hoverChainAnchorPos);
    float dist = Vector3.Dot(mw - hoverChainAnchorPos, hoverChainDir.normalized);
    dist = Mathf.Max(0.01f, dist);
    int segCount = Mathf.Max(1, Mathf.FloorToInt(dist / hoverChainSegLen));
    
    // Update chain using the cached start snap
    EnsureHoverChainSegs(segCount);
    LayoutHoverChain(hoverChainDir, hoverChainStartSnap);
}

// ---------- HOVER PREVIEW: 45° (VISUAL ONLY) ----------
if ((hoverDir == right45 || hoverDir == left45) && !isV1MContinueMode)
{
    Vector3 newDir = hoverDir.normalized;

    // Initialize hover root and corner pillar once
    if (!hover90Root)
    {
        ClearHover90Preview();
        HideLastGhostPillarM();
        HideCurvedGhostEndPillar();

        hover90Root = new GameObject("Hover45Preview");
        hover90Root.hideFlags = HideFlags.HideAndDontSave;

        Transform baseRailEndSnap = baseRailSnap;

// --- Ghost V1C45 ---
        var corner45Prefab = FindAsset<GameObject>(GetPillarPrefabName('4'));
        hover90EndPillarE = CreateGhost(corner45Prefab, hover90Root.transform);
        AddGhostTopToPillar(hover90EndPillarE);  // Add ghost top if fence has tops

        var s1 = FindSnap(hover90EndPillarE.transform, "SnapPoint1");
        var s2 = FindSnap(hover90EndPillarE.transform, "SnapPoint2");
        if (!s1 || !s2) return;

        // reuse scoring logic
        float scoreA = ScoreCornerCandidate(
            hover90EndPillarE.transform, s1, s2, baseRailEndSnap.position, newDir
        );
        float scoreB = ScoreCornerCandidate(
            hover90EndPillarE.transform, s2, s1, baseRailEndSnap.position, newDir
        );

        Transform inSnap = scoreA >= scoreB ? s1 : s2;
        Transform outSnap = scoreA >= scoreB ? s2 : s1;

        hover90EndPillarE.transform.rotation =
            YawDelta(inSnap.right, -activeDir) * hover90EndPillarE.transform.rotation;
        hover90EndPillarE.transform.position +=
            baseRailEndSnap.position - inSnap.position;

        // Store anchor position and direction for chain layout
        hoverChainAnchorPos = outSnap.position;
        hoverChainDir = newDir;
        hoverChainStartSnap = outSnap; // Cache the correct output snap
        
        // Calculate segment length from a temporary rail
        var railPrefab = FindAsset<GameObject>(
            $"sectionB_{VariantTag}_PREFAB"
        );
        if (!railPrefab) return;
        
        var tempRail = CreateGhost(railPrefab, hover90Root.transform);
        var rs = FindSnap(tempRail.transform, RailStartSnap);
        var re = FindSnap(tempRail.transform, RailEndSnap);
        if (!rs || !re) { DestroyImmediate(tempRail); return; }
        
        hoverChainSegLen = Vector3.Distance(rs.position, re.position);
        DestroyImmediate(tempRail);
        
        // Include pillar width so chain length matches actual layout
        hoverChainSegLen += MeasurePillarWidth();

    }

    // Calculate segment count based on mouse position (every frame)
    var mw = MouseOnPlane(Event.current.mousePosition, hoverChainAnchorPos);
    float dist = Vector3.Dot(mw - hoverChainAnchorPos, hoverChainDir.normalized);
    dist = Mathf.Max(0.01f, dist);
    int segCount = Mathf.Max(1, Mathf.FloorToInt(dist / hoverChainSegLen));
    
    // Update chain using the cached start snap
    EnsureHoverChainSegs(segCount);
    LayoutHoverChain(hoverChainDir, hoverChainStartSnap);
}

// --- Close Loop Detection ---
UpdateCloseLoopDetection();
AdjustCloseLoopBridge();

// --- fixed 90° curved arrows (blue + hover) ---
if (hasCurvedRails)
{
    Vector3 up = Vector3.up;
    Vector3 arcSide = side.normalized;

    // LEFT 90°
    DrawArcArrow90(
        pos + arcSide * r,
        -arcSide,
        up,
        r,
        false,
        hoverLeft ? ActiveCol : BaseCol
    );

    // RIGHT 90°
    DrawArcArrow90(
        pos - arcSide * r,
        arcSide,
        up,
        r,
        true,
        hoverRight ? ActiveCol : BaseCol
    );

    if (!isV1MContinueMode)
    {
        DrawVisualTurnArc90(
            pos,
            activeDir,
            false,
            visualR,
            hoverLeftOuter ? ActiveCol : BaseCol
        );

        DrawVisualTurnArc90(
            pos,
            activeDir,
            true,
            visualR,
            hoverRightOuter ? ActiveCol : BaseCol
        );
    }
}

    // draw arrows
    DrawArrow(pos, activeDir, hoverDir == activeDir);
    if (!isV1MContinueMode)
    {
        DrawArrow(pos, -side,     hoverDir == -side);
        DrawArrow(pos, side,      hoverDir == side);
        if (hasCurvedRails)
        {
            DrawArrow(pos, left45,    hoverDir == left45,  null, 1.25f);
            DrawArrow(pos, right45,   hoverDir == right45, null, 1.25f);
        }
    }

    // finalize arrow (same geometry, green)
DrawArrow(
    pos,
    backDir,
    hoverDir == backDir,
    hoverDir == backDir
        ? ActiveCol
        : new Color(0.05f, 0.6f, 0.15f, 1f)
);

    // click handling
    if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
    {
// --- CLOSE LOOP FINALIZE (any direction click while green) ---
if (closeLoopDetected && hoverDir != Vector3.zero)
{
    Undo.IncrementCurrentGroup();
    int undoGroupCL = Undo.GetCurrentGroup();

    bool cornerReplacesAnchor = hover90EndPillarE != null;

    CommitCloseLoopChain();
    CleanupContinueAnchorForCloseLoop(cornerReplacesAnchor);
    CommitRailSegs(null);

    FinalizeCloseLoopFence(undoGroupCL);
    e.Use();
    return;
}

// --- CONTINUE ANCHOR: FIRST COMMIT ---
if (continueAnchorActive && lastPlacedPillarM == null)
{
    // Forward (you said this is already fixed on your side; keep your working forward code here)
// Forward – FIRST CONTINUE COMMIT
if (hoverDir == activeDir)
{
    // 1) Create REAL pillar from continue ghost
    if (!continueGhostPillarM) { e.Use(); return; }

    Undo.IncrementCurrentGroup();
    int undoGroup = Undo.GetCurrentGroup();

var pillarPrefab = FindAsset<GameObject>(
    isV1MContinueMode ? GetPillarPrefabName('T') : GetPillarPrefabName('M')
);
    if (!pillarPrefab) { e.Use(); return; }

    var realPillarM = (GameObject)PrefabUtility.InstantiatePrefab(pillarPrefab);
ApplyCurrentTextureVariantToObject(realPillarM);
ApplyContinueTopToPillar(realPillarM);
    Undo.RegisterCreatedObjectUndo(realPillarM, "Place Pillar V1M (Continue)");
    currentBuildObjects.Add(realPillarM);

realPillarM.transform.position = continueGhostPillarM.transform.position;
realPillarM.transform.localScale = continueScale;
    
    // V1T: fit rotation to match surrounding rail snaps
    if (isV1MContinueMode)
    {
        realPillarM.transform.rotation = continueGhostPillarM.transform.rotation;
        FitV1TToRails(realPillarM, realPillarM.transform.position);
    }
    else
    {
        realPillarM.transform.rotation = continueGhostPillarM.transform.rotation;
    }

    // 2) Cleanup old continue objects
    DestroyImmediate(continueGhostPillarM);
    continueGhostPillarM = null;

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);
    continueSnapProxy = null;

    if (continueAnchorPillar)
    {
        protectedPillarIds.Remove(continueAnchorPillar.StableId());
        Undo.DestroyObjectImmediate(continueAnchorPillar);
    }
    continueAnchorPillar = null;
    continueAnchorActive = false;

    // 3) COMMIT THE GHOST CHAIN
    var chainLastPillar = CommitHoverChainOnly();
    if (chainLastPillar)
    {
        lastPlacedPillarM = chainLastPillar;
    }
    else
    {
        lastPlacedPillarM = realPillarM;
    }

ClearHover90Preview();

    // 4) Exit V1M mode after first commit
    isV1MContinueMode = false;

    // 5) NOW setup Continue Mode EXACTLY like normal BuildMode after RailPreview click (lines 2122-2181)
    continueAnchorPillar = lastPlacedPillarM;
    continueAnchorActive = true;

// Hide the anchor pillar
    foreach (var rend in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        rend.enabled = false;

    // Find the connected snap
    var snapA = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
    Transform connectedSnap = snapA;

    // Setup snap proxy
    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(connectedSnap.position, connectedSnap.rotation);

    lastPillarSnap = continueSnapProxy;
    activeDir = -continueSnapProxy.right;
    activeDir.y = 0f;
    if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

// Create Ghost V1M
    var pillarMPrefab2 = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (pillarMPrefab2)
    {
        continueGhostPillarM = CreateGhost(pillarMPrefab2);
        continueGhostPillarM.name = "ContinueGhostV1M";

        var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
        if (ghostSnap && lastPillarSnap)
        {
            continueGhostPillarM.transform.rotation =
                YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

            continueGhostPillarM.transform.position +=
                lastPillarSnap.position - ghostSnap.position;
        }

        // Add ghost top if fence has tops
        AddGhostTopToPillar(continueGhostPillarM);
    }

    Undo.CollapseUndoOperations(undoGroup);

    state = State.CornerSelect;
    e.Use();
    return;
}
    // 90°
    else if (hoverDir == side || hoverDir == -side)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        // 1) Cleanup old continue objects
        if (continueGhostPillarM) DestroyImmediate(continueGhostPillarM);
        continueGhostPillarM = null;

        if (continueSnapProxy) DestroyImmediate(continueSnapProxy.gameObject);
        continueSnapProxy = null;

        if (continueAnchorPillar) { protectedPillarIds.Remove(continueAnchorPillar.StableId()); Undo.DestroyObjectImmediate(continueAnchorPillar); }
        continueAnchorPillar = null;
        continueAnchorActive = false;

        // 2) COMMIT THE GHOST CHAIN (includes Corner + Rails + PillarsM)
        var chainLastPillar = CommitHoverChainOnly();
        if (chainLastPillar)
        {
            lastPlacedPillarM = chainLastPillar;
        }

        // 3) Update activeDir for 90° turn
        activeDir = hoverDir == side
            ? Vector3.Cross(Vector3.up, activeDir).normalized
            : Vector3.Cross(activeDir, Vector3.up).normalized;

        ClearHover90Preview();

        // 4) NOW setup Continue Mode EXACTLY like normal BuildMode after RailPreview click
        continueAnchorPillar = lastPlacedPillarM;
        continueAnchorActive = true;

// Hide the anchor pillar
        foreach (var rend in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            rend.enabled = false;

        // Find the connected snap
        var snapA = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
        Transform connectedSnap = snapA;

        // Setup snap proxy
        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);

        var proxyGO = new GameObject("ContinueSnapProxy");
        proxyGO.hideFlags = HideFlags.HideAndDontSave;
        continueSnapProxy = proxyGO.transform;
        continueSnapProxy.SetPositionAndRotation(connectedSnap.position, connectedSnap.rotation);

lastPillarSnap = continueSnapProxy;
        // Keep the NEW activeDir (already set for 90° turn)
        
        // Create Ghost V1M
        var pillarMPrefab2 = FindAsset<GameObject>(GetPillarPrefabName('M'));
        if (pillarMPrefab2)
        {
            continueGhostPillarM = CreateGhost(pillarMPrefab2);
            continueGhostPillarM.name = "ContinueGhostV1M";

            var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
            if (ghostSnap && lastPillarSnap)
            {
                continueGhostPillarM.transform.rotation =
                    YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

                continueGhostPillarM.transform.position +=
                    lastPillarSnap.position - ghostSnap.position;
            }

            // Add ghost top if fence has tops
            AddGhostTopToPillar(continueGhostPillarM);
        }

        Undo.CollapseUndoOperations(undoGroup);

        state = State.CornerSelect;
        e.Use();
        return;
    }
    // 45°
    else if (hoverDir == left45 || hoverDir == right45)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        // 1) Cleanup old continue objects
        if (continueGhostPillarM) DestroyImmediate(continueGhostPillarM);
        continueGhostPillarM = null;

        if (continueSnapProxy) DestroyImmediate(continueSnapProxy.gameObject);
        continueSnapProxy = null;

        if (continueAnchorPillar) { protectedPillarIds.Remove(continueAnchorPillar.StableId()); Undo.DestroyObjectImmediate(continueAnchorPillar); }
        continueAnchorPillar = null;
        continueAnchorActive = false;

        // 2) COMMIT THE GHOST CHAIN (includes Corner + Rails + PillarsM)
        var chainLastPillar = CommitHoverChainOnly();
        if (chainLastPillar)
        {
            lastPlacedPillarM = chainLastPillar;
        }

        // 3) Update activeDir for 45° turn
        activeDir = hoverDir.normalized;

        ClearHover90Preview();

        // 4) NOW setup Continue Mode EXACTLY like normal BuildMode after RailPreview click
        continueAnchorPillar = lastPlacedPillarM;
        continueAnchorActive = true;

// Hide the anchor pillar
        foreach (var rend in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            rend.enabled = false;

        // Find the connected snap
        var snapA = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
        Transform connectedSnap = snapA;

        // Setup snap proxy
        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);

        var proxyGO = new GameObject("ContinueSnapProxy");
        proxyGO.hideFlags = HideFlags.HideAndDontSave;
        continueSnapProxy = proxyGO.transform;
        continueSnapProxy.SetPositionAndRotation(connectedSnap.position, connectedSnap.rotation);

lastPillarSnap = continueSnapProxy;
        // Keep the NEW activeDir (already set for 45° turn)
        
        // Create Ghost V1M
        var pillarMPrefab2 = FindAsset<GameObject>(GetPillarPrefabName('M'));
        if (pillarMPrefab2)
        {
            continueGhostPillarM = CreateGhost(pillarMPrefab2);
            continueGhostPillarM.name = "ContinueGhostV1M";

            var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
            if (ghostSnap && lastPillarSnap)
            {
                continueGhostPillarM.transform.rotation =
                    YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

                continueGhostPillarM.transform.position +=
                    lastPillarSnap.position - ghostSnap.position;
            }

            // Add ghost top if fence has tops
            AddGhostTopToPillar(continueGhostPillarM);
        }

        Undo.CollapseUndoOperations(undoGroup);

        state = State.CornerSelect;
        e.Use();
        return;
    }
// Inner/Outer arc FIRST COMMIT
    else if (hoverLeft || hoverRight)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

var pillarPrefab = FindAsset<GameObject>(
    isV1MContinueMode ? GetPillarPrefabName('T') : GetPillarPrefabName('M')
);
        if (!pillarPrefab || !continueGhostPillarM) { e.Use(); return; }

        // REAL base V1T/V1M at continue ghost transform
        var realBaseM = (GameObject)PrefabUtility.InstantiatePrefab(pillarPrefab);
ApplyCurrentTextureVariantToObject(realBaseM);
ApplyContinueTopToPillar(realBaseM);
        Undo.RegisterCreatedObjectUndo(realBaseM, "Place Pillar V1M (Continue Base)");
        currentBuildObjects.Add(realBaseM);

realBaseM.transform.position = continueGhostPillarM.transform.position;
realBaseM.transform.localScale = continueScale;
        
        // V1T: fit rotation to match surrounding rail snaps
        if (isV1MContinueMode)
        {
            realBaseM.transform.rotation = continueGhostPillarM.transform.rotation;
            FitV1TToRails(realBaseM, realBaseM.transform.position);
        }
        else
        {
            realBaseM.transform.rotation = continueGhostPillarM.transform.rotation;
        }

        lastPlacedPillarM = realBaseM;
        lastPillarSnap = FindSnap(realBaseM.transform, "SnapPoint2");

        // cleanup continue objects
        DestroyImmediate(continueGhostPillarM);
        continueGhostPillarM = null;

        if (continueSnapProxy) DestroyImmediate(continueSnapProxy.gameObject);
        continueSnapProxy = null;

        if (continueAnchorPillar) { protectedPillarIds.Remove(continueAnchorPillar.StableId()); Undo.DestroyObjectImmediate(continueAnchorPillar); }
        continueAnchorPillar = null;
        continueAnchorActive = false;

// DO NOT clear hover preview here.
// Outer/Inner arc click commit needs hover90Root/hover90Rail/hover90PillarM/hover90EndPillarE in the same event.

// REBIND bases for THIS click (otherwise outer/inner arc uses stale proxy bases)
lastPillarM   = realBaseM.transform;
railEndSnap   = lastPillarSnap;     // now points to realBaseM SnapPoint2
baseRailSnap  = lastPillarSnap;
basePillarSnap = FindSnap(realBaseM.transform, "SnapPoint2");
mouseBasePos  = realBaseM.transform.position;

        // Exit V1M mode after first commit (fall-through to normal arc handler)
        isV1MContinueMode = false;

        Undo.CollapseUndoOperations(undoGroup);

        // IMPORTANT: no return here -> let the normal arc blocks handle the click below
    }
else if (hoverDir == backDir)
{
    Undo.IncrementCurrentGroup();
    int undoGroup = Undo.GetCurrentGroup();

    // V1M/V1C BuildMode abort: just restore the original pillar
    if (isV1MContinueMode)
    {
        ShowContinueAnchorPillar();
    }
    // CONTINUE BUILD + IMMEDIATE FINISH
    // If anchor pillar is NOT V1E, replace it with V1E
    else
    {
    var src = PrefabUtility.GetCorrespondingObjectFromSource(continueAnchorPillar);
bool isV1E = src && (src.name == GetPillarPrefabName('E'));

    if (!isV1E)
    {
        // --- REPLACE ANCHOR WITH V1E ---

        Transform oldPillar = continueAnchorPillar.transform;

var pillarEPrefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
        if (pillarEPrefab)
        {
            var endPillar =
                (GameObject)PrefabUtility.InstantiatePrefab(pillarEPrefab);
ApplyCurrentTextureVariantToObject(endPillar);
            Undo.RegisterCreatedObjectUndo(endPillar, "Replace Pillar with V1E");

// SnapPoint1 (X+) of V1E must point EXACTLY into the back arrow direction
var snap = FindSnap(endPillar.transform, PillarSnapName);
if (snap && lastPillarSnap)
{
    // 1) Rotate V1E so that SnapPoint1 (X+) points into the back arrow direction
    endPillar.transform.rotation =
        YawDelta(snap.right, backDir) * endPillar.transform.rotation;

    // 2) Move V1E so that SnapPoint1 sits EXACTLY on the previous rail snap
    endPillar.transform.position +=
        lastPillarSnap.position - snap.position;
}
endPillar.transform.localScale = continueScale;

            // parent into same fence
            if (continueTargetFence)
                endPillar.transform.SetParent(
                    continueTargetFence.transform,
                    true
                );

            // Transfer start marker if anchor was start pillar
            if (continueFlipElements && continueTargetFence)
            {
                EnsureStartMarker(continueTargetFence, endPillar);
                fenceStartPillars[continueTargetFence] = endPillar;
            }

            // remove old pillar
            protectedPillarIds.Remove(continueAnchorPillar.StableId());
            Undo.DestroyObjectImmediate(continueAnchorPillar);
        }
    }
    else
    {
        // original behavior for V1E
        ShowContinueAnchorPillar();
    }
    }

    // cleanup continue state
    if (continueGhostPillarM)
        DestroyImmediate(continueGhostPillarM);
    continueGhostPillarM = null;

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);
    continueSnapProxy = null;

    continueAnchorPillar = null;
    continueAnchorActive = false;

    Undo.CollapseUndoOperations(undoGroup);

    // Apply Full Detail Mode to the target fence
    if (fullDetailMode && continueTargetFence)
        ApplyFullDetailToFence(continueTargetFence, true);

    StopBuildMode();
    e.Use();
    return;
}
}

// --- STEP 2: commit straight chain, then place curved rail + V1M ---
if (hasCurvedRails && (hoverLeft || hoverRight))
{
    if (hover90Root && hover90Rail && hover90PillarM)
    {
        // Close-loop via inner arc
        if (closeLoopDetected)
        {
            Undo.IncrementCurrentGroup();
            int undoGroupCL = Undo.GetCurrentGroup();
            CommitArcAndCloseLoop(false);
            FinalizeCloseLoopFence(undoGroupCL);
            e.Use();
            return;
        }

        bool turnRight = hoverLeft;
        Vector3 newDir = turnRight
            ? Vector3.Cross(Vector3.up, activeDir).normalized
            : Vector3.Cross(activeDir, Vector3.up).normalized;

        CommitHover90CurvedPreviewAndEnterContinueMode(newDir);
        state = State.CornerSelect;
        e.Use();
        return;
    }

    e.Use();
    return;
}

// --- OUTER ARC: COMMIT AND ENTER CONTINUE MODE ---
if (hasCurvedRails && (hoverLeftOuter || hoverRightOuter) && baseRailSnap != null)
{
    if (hover90Root && hover90Rail && hover90PillarM && hover90EndPillarE)
    {
        // Close-loop via outer arc
        if (closeLoopDetected)
        {
            Undo.IncrementCurrentGroup();
            int undoGroupCL = Undo.GetCurrentGroup();
            CommitArcAndCloseLoop(true);
            FinalizeCloseLoopFence(undoGroupCL);
            e.Use();
            return;
        }

        CommitHover90OuterArcPreviewAndEnterContinueMode(activeDir);
        state = State.CornerSelect;
        e.Use();
        return;
    }

    e.Use();
    return;
}

        // finalize
if (hoverDir == backDir)
{
Undo.IncrementCurrentGroup();
int undoGroupFinalize = Undo.GetCurrentGroup();

bool wasCurved = hasCurvedRails && curvedGhostActive;

Transform finalRailEndSnap = null;
Transform finalPillarM = lastPillarM;

if (hasCurvedRails && curvedGhostActive)
{
    var realCurvedPrefab = FindAsset<GameObject>(
    $"sectionBCrvd_{VariantTag}_PREFAB"
);
var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (!realCurvedPrefab || !pillarMPrefab) { e.Use(); return; }

    // 1) Instantiate curved rail
    var realCurved = (GameObject)PrefabUtility.InstantiatePrefab(realCurvedPrefab);
ApplyCurrentTextureVariantToObject(realCurved);
    Undo.RegisterCreatedObjectUndo(realCurved, "Place Curved Rail");
    currentBuildObjects.Add(realCurved);

ApplyRailVisualVariation(realCurved);
ApplyCurvedRailVisualVariation(realCurved);

    realCurved.transform.SetPositionAndRotation(
        ghostCurvedRail.transform.position,
        ghostCurvedRail.transform.rotation
    );
    realCurved.transform.localScale = continueScale;

    // 2) Instantiate curved end V1M (corresponds to ghostCurvedPillar)
    var realCurvedPillarM = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
ApplyCurrentTextureVariantToObject(realCurvedPillarM);
ApplyContinueTopToPillar(realCurvedPillarM);
    Undo.RegisterCreatedObjectUndo(realCurvedPillarM, "Place Pillar V1M");
    currentBuildObjects.Add(realCurvedPillarM);

ApplyPillarMVisualVariation(realCurvedPillarM);

    realCurvedPillarM.transform.SetPositionAndRotation(
        ghostCurvedPillar.transform.position,
        ghostCurvedPillar.transform.rotation
    );
    realCurvedPillarM.transform.localScale = continueScale;

    finalPillarM = realCurvedPillarM.transform;

    // 3) Get FREE snap of the real curved rail (SnapPoint1 OR SnapPoint2)
    finalRailEndSnap = FindSnap(realCurved.transform, curvedOutSnapName);

    ClearCurvedGhost();
    curvedGhostActive = false;
}
else
{
finalRailEndSnap = railSegs.Count > 0 ? FindSnap(railSegs[^1].transform, RailEndSnap) : lastPillarSnap;
}

GameObject fenceRoot;

// CONTINUE BUILD → reuse remembered fence
if (continueTargetFence)
{
    fenceRoot = continueTargetFence;
}
else
{
    // NEW BUILD → create new fence
    fenceRoot =
        new GameObject($"Fence_{GetNextFenceNumber()}");
    Undo.RegisterCreatedObjectUndo(fenceRoot, "Create Fence");
    finalizedFences.Add(fenceRoot);

// Store start pillar (the very first V1E placed)
if (currentBuildObjects.Count > 0)
{
    var first = currentBuildObjects[0];
    if (first)
    {
        fenceStartPillars[fenceRoot] = first;
        EnsureStartMarker(fenceRoot, first);
        var startSnap = FindSnap(first.transform, "SnapPoint1");
    }
}
}

// Only in NON-curved case do RailSegs need to be committed
if (!wasCurved)
{
    CommitRailSegs(fenceRoot.transform);

    // IMPORTANT: last real V1M from CommitRailSegs
    finalPillarM = lastPlacedPillarM.transform;
}

// Finalize using the ACTUAL end pillar (ghost-based)
FinalizeChain(
    finalPillarM,
    finalRailEndSnap,
    fenceRoot.transform
);

    // Parent everything created during this build session
    foreach (var go in currentBuildObjects)
    {
        if (go)
            go.transform.SetParent(fenceRoot.transform, true);
    }

// center fence root pivot (only for NEW fences, not continue builds)
if (!continueTargetFence)
    CenterFencePivot(fenceRoot);

// --- CHAIN INDEX ASSIGNMENT ---
// New build: start at 0 (new root has no cache yet)
// Continue build: continue from existing cache.nextIndex
int startIdxForThisCommit = 0;

if (continueTargetFence)
{
    var cache = GetOrCreateIndexCache(fenceRoot);
    startIdxForThisCommit = cache != null ? cache.nextIndex : 0;
}

AssignIndices(fenceRoot, currentBuildObjects, startIdxForThisCommit);

// Apply Full Detail Mode to the entire finalized fence
if (fullDetailMode)
    ApplyFullDetailToFence(fenceRoot, true);

currentBuildObjects.Clear();

Undo.CollapseUndoOperations(undoGroupFinalize);

StopBuildMode();
Repaint();

e.Use();
return;
}

// continue building
if (!ENABLE_CONTINUE_BUILD)
{
    e.Use();
    return;
}

// --- Curved Rail + End-Pillar finalisieren ---
if (hasCurvedRails && curvedGhostActive)
{
    var realCurvedPrefab = FindAsset<GameObject>(
    $"sectionBCrvd_{VariantTag}_PREFAB"
);
var pillarMPrefab    = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (!realCurvedPrefab || !pillarMPrefab) return;

    // 1) Curved Rail
    var realCurved = (GameObject)PrefabUtility.InstantiatePrefab(realCurvedPrefab);
ApplyCurrentTextureVariantToObject(realCurved);
    Undo.RegisterCreatedObjectUndo(realCurved, "Place Curved Rail");
    currentBuildObjects.Add(realCurved);

ApplyRailVisualVariation(realCurved);
ApplyCurvedRailVisualVariation(realCurved);

    realCurved.transform.SetPositionAndRotation(
        ghostCurvedRail.transform.position,
        ghostCurvedRail.transform.rotation
    );
    realCurved.transform.localScale = continueScale;

    // 2) End pillar (corresponds to ghost pillar!)
    var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
ApplyCurrentTextureVariantToObject(realPillar);
ApplyContinueTopToPillar(realPillar);
    Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
    currentBuildObjects.Add(realPillar);

ApplyPillarMVisualVariation(realPillar);

    realPillar.transform.SetPositionAndRotation(
        ghostCurvedPillar.transform.position,
        ghostCurvedPillar.transform.rotation
    );
    realPillar.transform.localScale = continueScale;

// 3) New starting point is the FREE snap of the curved rail
lastPlacedPillarM = realPillar;

// IMPORTANT: Corner must snap to the curved rail, not to the pillar
lastPillarSnap = FindSnap(realCurved.transform, curvedOutSnapName);

    ClearCurvedGhost();
    curvedGhostActive = false;
}
else
{
    if (railSegs.Count > 0)
        CommitRailSegs(null);
}

// If hover90 preview exists, commit it and enter continue mode
        if (hover90Root)
        {
            // Determine new direction based on what was clicked
            Vector3 newDir = activeDir; // default: straight
            if (hoverDir == left45 || hoverDir == right45)
                newDir = hoverDir;
            else if (hoverDir == side || hoverDir == -side)
                newDir = hoverDir;
            
            CommitHover90PreviewAndEnterContinueMode(newDir);
            state = State.CornerSelect;
            e.Use();
            return;
        }

        if (hoverDir == left45 || hoverDir == right45)
        {
            ReplaceLastPillarWithCorner45(hoverDir);
        }
        else if (hoverDir != activeDir)
        {
            ReplaceLastPillarWithCorner(hoverDir);
        }
        else
        {
            lastPillarSnap = FindSnap(lastPlacedPillarM.transform, "SnapPoint2");
        }

ClearHover90Preview(); // destroy last direction preview

        StartRailPreview(FindAsset<GameObject>(
    $"sectionB_{VariantTag}_PREFAB"
));
        state = State.RailPreview;

        e.Use();
    }
}

        sv.Repaint();
    }

    void CommitPillarAndStartRailPreview()
    {
var pillarPrefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
        var railPrefab   = FindAsset<GameObject>(
    $"sectionB_{VariantTag}_PREFAB"
);
        if (!pillarPrefab || !railPrefab) return;

        var placed = (GameObject)PrefabUtility.InstantiatePrefab(pillarPrefab);
ApplyCurrentTextureVariantToObject(placed);
currentBuildObjects.Add(placed);
        Undo.RegisterCreatedObjectUndo(placed, "Place Pillar");
Selection.activeObject = null;
lastPlacedPillarE = placed;
        placed.transform.position = frozenPos;

        var snap = FindSnap(placed.transform, PillarSnapName);
        if (!snap) { DestroyImmediate(placed); return; }

        placed.transform.rotation = YawDelta(snap.right, activeDir) * placed.transform.rotation;
        lastPillarSnap = snap;

        DestroyImmediate(ghost);
        ghost = null;

        StartRailPreview(railPrefab);
        state = State.RailPreview;
    }

void UpdateDirectionSelectHoverChain(Vector3 dir)
{
    if (!ghost) return;
    
    // Get ghost pillar snap after rotation
    var pillarSnap = FindSnap(ghost.transform, PillarSnapName);
    if (!pillarSnap) return;
    
    Vector3 anchorPos = pillarSnap.position;
    
    // Create root if needed
    if (!dirSelectHoverRoot)
    {
        dirSelectHoverRoot = new GameObject("DirSelectHoverChain");
        dirSelectHoverRoot.hideFlags = HideFlags.HideAndDontSave;
        
        // Calculate segment length once
        var railPrefab = FindAsset<GameObject>(
            $"sectionB_{VariantTag}_PREFAB"
        );
        if (railPrefab)
        {
            var tempRail = CreateGhost(railPrefab);
            var rs = FindSnap(tempRail.transform, RailStartSnap);
            var re = FindSnap(tempRail.transform, RailEndSnap);
            dirSelectSegLen = (rs && re) ? Vector3.Distance(rs.position, re.position) : 1f;
            DestroyImmediate(tempRail);
            
            // Include pillar width so chain length matches actual layout
            dirSelectSegLen += MeasurePillarWidth();
        }
    }
    
    // Calculate segment count based on mouse distance
    var mw = MouseOnPlane(Event.current.mousePosition, anchorPos);
    float dist = Vector3.Dot(mw - anchorPos, dir.normalized);
    dist = Mathf.Max(0.01f, dist);
    int segCount = Mathf.Max(1, Mathf.FloorToInt(dist / dirSelectSegLen));
    
    // IMPORTANT: Destroy ALL old objects and create completely new
    // This prevents any accumulation
    foreach (var go in dirSelectHoverRails)
        if (go) DestroyImmediate(go);
    dirSelectHoverRails.Clear();
    
    foreach (var go in dirSelectHoverPillars)
        if (go) DestroyImmediate(go);
    dirSelectHoverPillars.Clear();
    
    // Create new objects
    var newRailPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (!newRailPrefab || !pillarMPrefab) return;
    
    for (int i = 0; i < segCount; i++)
    {
        var newRail = CreateGhost(newRailPrefab, dirSelectHoverRoot.transform);
        if (continueFlipElements) FlipVisuals180(newRail);
        dirSelectHoverRails.Add(newRail);
        var newPillar = CreateGhost(pillarMPrefab, dirSelectHoverRoot.transform);
        dirSelectHoverPillars.Add(newPillar);
    }
    
    // Layout the chain (now with fresh objects)
    LayoutDirSelectHoverChain(dir, pillarSnap);

    // Check close loop
    UpdateCloseLoopDetection();
}

void EnsureDirSelectHoverSegs(int count)
{
    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    
    if (!railPrefab || !pillarMPrefab || !dirSelectHoverRoot) return;
    
    while (dirSelectHoverRails.Count < count)
    {
        var newRail = CreateGhost(railPrefab, dirSelectHoverRoot.transform);
        if (continueFlipElements) FlipVisuals180(newRail);
        dirSelectHoverRails.Add(newRail);
        var newPillar = CreateGhost(pillarMPrefab, dirSelectHoverRoot.transform);
        dirSelectHoverPillars.Add(newPillar);
    }
    
    while (dirSelectHoverRails.Count > count)
    {
        DestroyImmediate(dirSelectHoverRails[^1]);
        dirSelectHoverRails.RemoveAt(dirSelectHoverRails.Count - 1);
        
        DestroyImmediate(dirSelectHoverPillars[^1]);
        dirSelectHoverPillars.RemoveAt(dirSelectHoverPillars.Count - 1);
    }
}

void LayoutDirSelectHoverChain(Vector3 dir, Transform startSnap)
{
    if (dirSelectHoverRails.Count == 0 || !startSnap) return;
    
    Transform target = startSnap;

    // Baseline Y: take from last pillar snap (covers both initial and continue build).
    // lastPillarSnap is the snap of the previously placed pillar (start E, or last M from continue).
    Vector3 prevPos;
    if (lastPillarSnap)
        prevPos = lastPillarSnap.position;
    else if (continueAnchorActive && continueAnchorPillar)
        prevPos = continueAnchorPillar.transform.position;
    else
        prevPos = frozenPos;
    float prevY = prevPos.y;
    float maxTan = Mathf.Tan(3f * Mathf.Deg2Rad);

    for (int i = 0; i < dirSelectHoverRails.Count; i++)
    {
        var rail = dirSelectHoverRails[i];
        var pillar = dirSelectHoverPillars[i];

        var rs = FindSnap(rail.transform, RailStartSnap);
        var re = FindSnap(rail.transform, RailEndSnap);
        var ps = FindSnap(pillar.transform, PillarSnapName);
        var psNext = FindSnap(pillar.transform, "SnapPoint2");
        if (!rs || !re || !ps || !psNext) return;

        // Rail to previous snap (exactly like LayoutRailSegs)
        AlignRailToTarget(rail.transform, rs, -dir, target);

        // PillarM to rail end (exactly like LayoutRailSegs)
        pillar.transform.rotation =
            YawDelta(ps.right, -dir) * pillar.transform.rotation;
        pillar.transform.position += re.position - ps.position;

        // Terrain Y-snap, clamped to ±3° relative to previous pillar.
        // Skip entirely if ground is flat between prev and current (no spurious Y shift).
        // Going down: pillar AND rail snap together (smooth slope)
        // Going up: pillar snaps up, rail stays at previous pillar height (stair)
        Vector3 pp = pillar.transform.position;
        float currGround = GroundYAt(pp);
        float prevGround = GroundYAt(prevPos);
        if (!float.IsNaN(currGround) && !float.IsNaN(prevGround))
        {
            float dist = Vector2.Distance(new Vector2(pp.x, pp.z),
                                          new Vector2(prevPos.x, prevPos.z));
            float groundDelta = currGround - prevGround;
            if (dist > 1e-4f && Mathf.Abs(groundDelta) > 1e-3f)
            {
                float tanA = Mathf.Clamp(groundDelta / dist, -maxTan, maxTan);
                float clampedY = prevY + tanA * dist;
                float dy = clampedY - pp.y;

                pillar.transform.position = new Vector3(pp.x, clampedY, pp.z);

                // Rail: only follow pillar when going down. When going up, rail stays at prev height.
                if (clampedY < pp.y)
                    rail.transform.position += new Vector3(0f, dy, 0f);
            }
        }

        prevPos = pillar.transform.position;
        prevY = prevPos.y;
        target = psNext;
    }
}

void ClearDirSelectHoverChain()
{
    ClearCloseLoopFull();

    foreach (var go in dirSelectHoverRails)
        if (go) DestroyImmediate(go);
    dirSelectHoverRails.Clear();
    
    foreach (var go in dirSelectHoverPillars)
        if (go) DestroyImmediate(go);
    dirSelectHoverPillars.Clear();
    
    if (dirSelectHoverRoot)
    {
        DestroyImmediate(dirSelectHoverRoot);
        dirSelectHoverRoot = null;
    }
}

void CommitDirectionSelectWithChain()
{
    // 1) Commit start pillar (V1E)
    var pillarPrefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    
    if (!pillarPrefab || !railPrefab || !pillarMPrefab) return;
    
    var startPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarPrefab);
    ApplyCurrentTextureVariantToObject(startPillar);
    currentBuildObjects.Add(startPillar);
    Undo.RegisterCreatedObjectUndo(startPillar, "Place Start Pillar");
    Selection.activeObject = null;
    lastPlacedPillarE = startPillar;
    startPillar.transform.position = frozenPos;
    startPillar.transform.rotation = ghost.transform.rotation; // match ghost exactly so snap positions align with hover chain
    
    var snap = FindSnap(startPillar.transform, PillarSnapName);
    if (!snap) { DestroyImmediate(startPillar); return; }
    lastPillarSnap = snap;
    
    // 2) Commit all hover chain rails + pillars
    for (int i = 0; i < dirSelectHoverRails.Count; i++)
    {
        var ghostRail = dirSelectHoverRails[i];
        var ghostPillar = dirSelectHoverPillars[i];
        
        if (!ghostRail || !ghostPillar) continue;
        
        // Real rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);
        
        realRail.transform.SetPositionAndRotation(
            ghostRail.transform.position,
            ghostRail.transform.rotation
        );
        if (continueFlipElements) FlipVisuals180(realRail);
        
        // Real pillar V1M
        var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);
        
        realPillar.transform.SetPositionAndRotation(
            ghostPillar.transform.position,
            ghostPillar.transform.rotation
        );
        
        lastPlacedPillarM = realPillar;
        
        // Update lastPillarSnap to THIS pillar's SnapPoint2 for next iteration / CornerSelect
        lastPillarSnap = FindSnap(realPillar.transform, "SnapPoint2");
    }
    
    // 3) Cleanup hover chain
    ClearDirSelectHoverChain();
    
    if (ghost) DestroyImmediate(ghost);
    ghost = null;
    
    // 4) Setup Continue Build Mode (required for CornerSelect to work!)
    if (lastPlacedPillarM)
    {
        continueAnchorPillar = lastPlacedPillarM;
        continueAnchorActive = true;
        
        // Find the connected snap (SnapPoint1 is connected to previous rail)
        var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
        var snap2 = FindSnap(continueAnchorPillar.transform, "SnapPoint2");
        Transform connectedSnap = snap1;
        
        // Setup snap proxy
        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);
        
        var proxyGO = new GameObject("ContinueSnapProxy");
        proxyGO.hideFlags = HideFlags.HideAndDontSave;
        continueSnapProxy = proxyGO.transform;
        continueSnapProxy.SetPositionAndRotation(connectedSnap.position, connectedSnap.rotation);
        
        lastPillarSnap = continueSnapProxy;
        
        // Create Ghost V1M for visual preview
        if (continueGhostPillarM)
            DestroyImmediate(continueGhostPillarM);
        
        continueGhostPillarM = CreateGhost(pillarMPrefab);
        continueGhostPillarM.name = "ContinueGhostV1M";
        
        var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
        if (ghostSnap && lastPillarSnap)
        {
            continueGhostPillarM.transform.rotation =
                YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;
            continueGhostPillarM.transform.position +=
                lastPillarSnap.position - ghostSnap.position;
        }
        
        AddGhostTopToPillar(continueGhostPillarM);
    }
    
    // 5) Apply Full Detail Mode to placed objects
    ApplyFullDetailToCurrentBuild();

    // 6) Enter CornerSelect state
    state = State.CornerSelect;
}

void StartRailPreview(GameObject railPrefab)
{
    ClearRailPreview();

    railAnchorPos = lastPillarSnap.position;

    railPreviewRoot = new GameObject("RailPreviewRoot");
    railPreviewRoot.hideFlags = HideFlags.HideAndDontSave;
    ghost = railPreviewRoot;

    var firstRail = CreateGhost(railPrefab, railPreviewRoot.transform);
    if (continueFlipElements) FlipVisuals180(firstRail);
    railSegs.Add(firstRail);

var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
var firstPillar = CreateGhost(pillarMPrefab, railPreviewRoot.transform);
AddGhostTopToPillar(firstPillar);
ghostPillarsM.Add(firstPillar);

    var railStart = FindSnap(firstRail.transform, RailStartSnap);
    var railEnd   = FindSnap(firstRail.transform, RailEndSnap);
    if (!railStart || !railEnd) return;

    AlignRailToTarget(firstRail.transform, railStart, -activeDir, lastPillarSnap);
    segLen = Vector3.Distance(railStart.position, railEnd.position);
}

void EnsureRailSegs(int count)
{
    var railPrefab   = FindAsset<GameObject>(
    $"sectionB_{VariantTag}_PREFAB"
);
var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

while (railSegs.Count < count)
    {
        var newRail = CreateGhost(railPrefab, railPreviewRoot.transform);
        if (continueFlipElements) FlipVisuals180(newRail);
        railSegs.Add(newRail);
        var newPillar = CreateGhost(pillarMPrefab, railPreviewRoot.transform);
        AddGhostTopToPillar(newPillar);
        ghostPillarsM.Add(newPillar);
    }

    while (railSegs.Count > count)
    {
        DestroyImmediate(railSegs[^1]);
        railSegs.RemoveAt(railSegs.Count - 1);

        DestroyImmediate(ghostPillarsM[^1]);
        ghostPillarsM.RemoveAt(ghostPillarsM.Count - 1);
    }
}

void LayoutRailSegs(Vector3 axis)
{
    Transform target = lastPillarSnap;

    for (int i = 0; i < railSegs.Count; i++)
    {
        var rail = railSegs[i];
        var pillar = ghostPillarsM[i];

        var rs = FindSnap(rail.transform, RailStartSnap);
        var re = FindSnap(rail.transform, RailEndSnap);
        var ps = FindSnap(pillar.transform, PillarSnapName);
	var psNext = FindSnap(pillar.transform, "SnapPoint2");
        if (!rs || !re || !ps) return;

        // Rail to previous snap
        AlignRailToTarget(rail.transform, rs, -activeDir, target);

        // PillarM to rail end
        pillar.transform.rotation =
            YawDelta(ps.right, -activeDir) * pillar.transform.rotation;
        pillar.transform.position += re.position - ps.position;

        // Terrain Y-snap (raycast from above, set pillar to ground)
        Vector3 pp = pillar.transform.position;
        Vector3 rayStart = new Vector3(pp.x, pp.y + 100f, pp.z);
        bool didHit = Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f);
        if (didHit)
        {
            pillar.transform.position = new Vector3(pp.x, hit.point.y, pp.z);
        }

        target = psNext; // next rail snaps to this pillar
    }
}

// Shared finalization for close-loop: parent objects, assign indices, apply detail, stop build.
void FinalizeCloseLoopFence(int undoGroup)
{
    GameObject clRoot;
    if (continueTargetFence)
    {
        clRoot = continueTargetFence;
    }
    else
    {
        clRoot = new GameObject($"Fence_{GetNextFenceNumber()}");
        Undo.RegisterCreatedObjectUndo(clRoot, "Create Fence");
        finalizedFences.Add(clRoot);
        if (currentBuildObjects.Count > 0)
        {
            var first = currentBuildObjects[0];
            if (first) {
                fenceStartPillars[clRoot] = first; EnsureStartMarker(clRoot, first);
                var sd = FindSnap(first.transform, "SnapPoint1");
            }
        }
    }
    foreach (var go in currentBuildObjects)
        if (go) go.transform.SetParent(clRoot.transform, true);
    if (!continueTargetFence)
        CenterFencePivot(clRoot);
    int si = 0;
    if (continueTargetFence)
    { var c = GetOrCreateIndexCache(clRoot); si = c != null ? c.nextIndex : 0; }
    AssignIndices(clRoot, currentBuildObjects, si);
    if (fullDetailMode) ApplyFullDetailToFence(clRoot, true);
    currentBuildObjects.Clear();
    Undo.CollapseUndoOperations(undoGroup);
    StopBuildMode();
    Repaint();
}

// Cleanup continue-mode anchor + ghost for close-loop finalize.
// cornerReplacesAnchor: true when a corner/V1C was placed at the anchor position.
void CleanupContinueAnchorForCloseLoop(bool cornerReplacesAnchor)
{
    if (continueGhostPillarM) { DestroyImmediate(continueGhostPillarM); continueGhostPillarM = null; }
    if (continueSnapProxy) { DestroyImmediate(continueSnapProxy.gameObject); continueSnapProxy = null; }
    if (continueAnchorPillar)
    {
        if (isV1MContinueMode)
        {
            // External V1M/V1C continue: anchor must become V1T (3 snaps occupied)
            var pf = FindAsset<GameObject>(GetPillarPrefabName('T'));
            if (pf)
            {
                var v = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                ApplyCurrentTextureVariantToObject(v); ApplyContinueTopToPillar(v);
                v.transform.localScale = continueScale;
                Undo.RegisterCreatedObjectUndo(v, "Place V1T (Close Loop Anchor)");
                v.transform.SetPositionAndRotation(continueAnchorPillar.transform.position, continueAnchorPillar.transform.rotation);
                FitV1TToRails(v, v.transform.position);
                currentBuildObjects.Add(v);
            }
        }
        else if (!cornerReplacesAnchor)
        {
            var srcObj = PrefabUtility.GetCorrespondingObjectFromSource(continueAnchorPillar);
            bool anchorIsV1E = srcObj && (srcObj.name.Contains("V1E") || srcObj.name.Contains("V2E") || srcObj.name.Contains("V3E") || srcObj.name.Contains("V4E") || srcObj.name.Contains("V5E"));

            if (!anchorIsV1E)
            {
                // Anchor is already V1M (e.g. inner arc) → just unhide
                foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
                    r.enabled = true;
                continueAnchorPillar = null;
            }
            else
            {
                // Anchor is V1E → replace with V1M (now has 2 snaps occupied)
                var pf = FindAsset<GameObject>(GetPillarPrefabName('M'));
                if (pf)
                {
                    var v = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    ApplyCurrentTextureVariantToObject(v); ApplyContinueTopToPillar(v);
                    v.transform.localScale = continueScale;
                    Undo.RegisterCreatedObjectUndo(v, "Place V1M (Close Loop Anchor)");
                    v.transform.SetPositionAndRotation(continueAnchorPillar.transform.position, continueAnchorPillar.transform.rotation);
                    FitV1TToRails(v, v.transform.position);
                    ApplyPillarMVisualVariation(v);
                    currentBuildObjects.Add(v);
                }
                // V1E anchor destroyed below
            }
        }
        // else: corner already placed at anchor position → destroy anchor below

        if (continueAnchorPillar)
        {
            currentBuildObjects.Remove(continueAnchorPillar);
            protectedPillarIds.Remove(continueAnchorPillar.StableId());
            bool prevSuppress = suppressDeleteUndo;
            suppressDeleteUndo = true;
            Undo.DestroyObjectImmediate(continueAnchorPillar);
            suppressDeleteUndo = prevSuppress;
            continueAnchorPillar = null;
        }
    }
    isV1MContinueMode = false;
}

void CommitRailSegs(Transform parent, bool skipLastPillar = false)
{
    var railPrefab    = FindAsset<GameObject>(
    $"sectionB_{VariantTag}_PREFAB"
);
var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (!railPrefab || !pillarMPrefab) return;

    for (int i = 0; i < railSegs.Count; i++)
    {
        // Rail
        var railPlaced = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
ApplyCurrentTextureVariantToObject(railPlaced);
        Undo.RegisterCreatedObjectUndo(railPlaced, "Place Rail");
        currentBuildObjects.Add(railPlaced);

ApplyRailVisualVariation(railPlaced);

        railPlaced.transform.SetPositionAndRotation(
            railSegs[i].transform.position,
            railSegs[i].transform.rotation
        );
        railPlaced.transform.localScale = continueScale;
        if (continueFlipElements) FlipVisuals180(railPlaced);

        if (parent)
            railPlaced.transform.SetParent(parent, true);

        // Skip last pillar when close-loop handles the junction
        if (skipLastPillar && i == railSegs.Count - 1)
            break;

        // Pillar V1M
        var pillarPlaced = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
ApplyCurrentTextureVariantToObject(pillarPlaced);
ApplyContinueTopToPillar(pillarPlaced);
        Undo.RegisterCreatedObjectUndo(pillarPlaced, "Place Pillar V1M");
        currentBuildObjects.Add(pillarPlaced);

ApplyPillarMVisualVariation(pillarPlaced);

        pillarPlaced.transform.SetPositionAndRotation(
            ghostPillarsM[i].transform.position,
            ghostPillarsM[i].transform.rotation
        );
        pillarPlaced.transform.localScale = continueScale;

        if (parent)
            pillarPlaced.transform.SetParent(parent, true);

        if (i == railSegs.Count - 1)
            lastPlacedPillarM = pillarPlaced;
    }

    ApplyFullDetailToCurrentBuild();
    state = State.CornerSelect;
}

void CommitRailOnly(Transform parent)
{
    var railPrefab = FindAsset<GameObject>(
    $"sectionB_{VariantTag}_PREFAB"
);
    if (!railPrefab) return;

    for (int i = 0; i < railSegs.Count; i++)
    {
        var railPlaced = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
ApplyCurrentTextureVariantToObject(railPlaced);
        Undo.RegisterCreatedObjectUndo(railPlaced, "Place Rail");
        currentBuildObjects.Add(railPlaced);

        railPlaced.transform.SetPositionAndRotation(
            railSegs[i].transform.position,
            railSegs[i].transform.rotation
        );
        railPlaced.transform.localScale = continueScale;
        if (continueFlipElements) FlipVisuals180(railPlaced);

        if (parent)
            railPlaced.transform.SetParent(parent, true);
    }

// Rail-only commit is only used during finalization
// State is then ended by StopBuildMode
state = State.CornerSelect;
}

void ClearRailPreview()
{
    if (railPreviewRoot) DestroyImmediate(railPreviewRoot);
    railPreviewRoot = null;
    railSegs.Clear();
    ghostPillarsM.Clear();
}

void FinalizeChain(
    Transform lastPillarM,
    Transform railEndSnap,
    Transform parent
)
{
if (!lastPillarM || !railEndSnap)
    return;

var pillarEPrefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
    if (!pillarEPrefab) return;

// The V1M to remove is the one at the rail end,
// NOT lastPlacedPillarM
var v1mToRemove = lastPillarM.gameObject;
DestroyImmediate(v1mToRemove);

// Only reset lastPlacedPillarM if it was exactly this one
if (lastPlacedPillarM == v1mToRemove)
    lastPlacedPillarM = null;

    // create end pillar (V1E)
    var endPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarEPrefab);
ApplyCurrentTextureVariantToObject(endPillar);
ApplyContinueTopToPillar(endPillar);
endPillar.transform.localScale = continueScale;
currentBuildObjects.Add(endPillar);
    Undo.RegisterCreatedObjectUndo(endPillar, "Place End Pillar");

if (parent) endPillar.transform.SetParent(parent, true);

    var snap = FindSnap(endPillar.transform, PillarSnapName);
    if (!snap)
    {
        DestroyImmediate(endPillar);
        return;
    }

    // align SnapPoint1 to last rail SnapPoint2
    endPillar.transform.rotation =
        YawDelta(snap.right, -activeDir) * endPillar.transform.rotation;

// Set V1E SnapPoint1 exactly at the rail end
endPillar.transform.position +=
    railEndSnap.position - snap.position;

    // StopBuildMode is called by the caller after parenting.
}

void ReplaceLastPillarWithCorner(Vector3 newDir)
{
var cornerPrefab = FindAsset<GameObject>(GetPillarPrefabName('C'));
    if (!cornerPrefab || !lastPlacedPillarM) return;

    // find last rail end snap
Transform railEndSnap =
    railSegs.Count > 0
        ? FindSnap(railSegs[^1].transform, RailEndSnap)
        : lastPillarSnap;
    if (!railEndSnap) return;
Vector3 railEndPos = railEndSnap.position;

    // destroy old V1M
    DestroyImmediate(lastPlacedPillarM);
    lastPlacedPillarM = null;

    // create corner pillar
    var corner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
ApplyCurrentTextureVariantToObject(corner);
corner.transform.localScale = continueScale;
currentBuildObjects.Add(corner);
    Undo.RegisterCreatedObjectUndo(corner, "Place Corner Pillar");
ApplyContinueTopToPillar(corner);

    var sp1 = FindSnap(corner.transform, "SnapPoint1");
    var sp2 = FindSnap(corner.transform, "SnapPoint2");
    if (!sp1 || !sp2)
    {
        DestroyImmediate(corner);
        return;
    }

    // test candidate A: SnapPoint1 = input
float scoreA = ScoreCornerCandidate(
    corner.transform, sp1, sp2, railEndPos, newDir
);

    // test candidate B: SnapPoint2 = input
float scoreB = ScoreCornerCandidate(
    corner.transform, sp2, sp1, railEndPos, newDir
);

    // apply best candidate
    if (scoreA >= scoreB)
ApplyCornerPlacement(corner.transform, sp1, sp2, railEndPos, newDir);
    else
ApplyCornerPlacement(corner.transform, sp2, sp1, railEndPos, newDir);
}

void ReplacePillarWithV1E_AtFreeSnap(GameObject oldPillar, Transform freeSnap)
{
    if (!oldPillar || !freeSnap)
        return;

var prefab = FindAsset<GameObject>(GetPillarPrefabName('E'));
    if (!prefab)
        return;

    Transform parent = oldPillar.transform.parent;
    int sibling = oldPillar.transform.GetSiblingIndex();

    // Instantiate new first (never delete old before success)
    var v1e = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    ApplyCurrentTextureVariantToObject(v1e);
    ApplyContinueTopToPillar(v1e);
    v1e.transform.localScale = continueScale;

    var snap = FindSnap(v1e.transform, PillarSnapName);
    if (!snap)
    {
        DestroyImmediate(v1e);
        return;
    }

    // SnapPoint1 must face INTO the free snap direction
    Vector3 snapDir = freeSnap.right;
    snapDir.y = 0f;
    snapDir.Normalize();

    v1e.transform.rotation =
        YawDelta(snap.right, snapDir) * v1e.transform.rotation;

    // Snap AFTER rotation
    v1e.transform.position += freeSnap.position - snap.position;

    if (parent)
        v1e.transform.SetParent(parent, true);

    v1e.transform.SetSiblingIndex(sibling);

    Undo.RegisterCreatedObjectUndo(v1e, "Replace Pillar with V1E");

    Undo.DestroyObjectImmediate(oldPillar);
}

void ReplaceLastPillarWithCorner45(Vector3 newDir)
{
var cornerPrefab = FindAsset<GameObject>(GetPillarPrefabName('4'));
    if (!cornerPrefab || !lastPlacedPillarM) return;

Transform railEndSnap =
    railSegs.Count > 0
        ? FindSnap(railSegs[^1].transform, RailEndSnap)
        : lastPillarSnap;
    if (!railEndSnap) return;
Vector3 railEndPos = railEndSnap.position;

    DestroyImmediate(lastPlacedPillarM);

    var corner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
ApplyCurrentTextureVariantToObject(corner);
corner.transform.localScale = continueScale;
currentBuildObjects.Add(corner);
    Undo.RegisterCreatedObjectUndo(corner, "Place 45 Corner Pillar");
ApplyContinueTopToPillar(corner);

    var sp1 = FindSnap(corner.transform, "SnapPoint1");
    var sp2 = FindSnap(corner.transform, "SnapPoint2");
    if (!sp1 || !sp2)
    {
        DestroyImmediate(corner);
        return;
    }

    // score both snap configurations (same logic as V1C)
float scoreA = ScoreCornerCandidate(
    corner.transform, sp1, sp2, railEndPos, newDir
);
float scoreB = ScoreCornerCandidate(
    corner.transform, sp2, sp1, railEndPos, newDir
);

    if (scoreA >= scoreB)
ApplyCornerPlacement(corner.transform, sp1, sp2, railEndPos, newDir);
    else
ApplyCornerPlacement(corner.transform, sp2, sp1, railEndPos, newDir);
}

float ScoreCornerCandidate(
    Transform root,
    Transform inputSnap,
    Transform outputSnap,
    Vector3 railEndPos,
    Vector3 newDir
)
{
    Quaternion origRot = root.rotation;
    Vector3 origPos = root.position;

    root.rotation = YawDelta(inputSnap.right, -activeDir) * origRot;
    root.position += railEndPos - inputSnap.position;
    Vector3 outDir = outputSnap.right;

    // Restore original transform
    root.SetPositionAndRotation(origPos, origRot);

    return Vector3.Dot(outDir.normalized, newDir.normalized);
}

void ApplyCornerPlacement(
    Transform root,
    Transform inputSnap,
    Transform outputSnap,
    Vector3 railEndPos,
    Vector3 newDir
)
{
    root.rotation =
        YawDelta(inputSnap.right, -activeDir) * root.rotation;

    root.position += railEndPos - inputSnap.position;

    activeDir = newDir;
    lastPillarSnap = outputSnap;
}

void PlaceCornerFromContinueSnap(GameObject cornerPrefab, Vector3 newDir)
{
    if (!cornerPrefab || !lastPillarSnap) return;

    Vector3 railEndPos = lastPillarSnap.position;

    var corner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
ApplyCurrentTextureVariantToObject(corner);
corner.transform.localScale = continueScale;
    currentBuildObjects.Add(corner);
    Undo.RegisterCreatedObjectUndo(corner, "Place Corner Pillar (Continue)");
ApplyContinueTopToPillar(corner);

    var sp1 = FindSnap(corner.transform, "SnapPoint1");
    var sp2 = FindSnap(corner.transform, "SnapPoint2");
    if (!sp1 || !sp2)
    {
        DestroyImmediate(corner);
        return;
    }

    float scoreA = ScoreCornerCandidate(corner.transform, sp1, sp2, railEndPos, newDir);
    float scoreB = ScoreCornerCandidate(corner.transform, sp2, sp1, railEndPos, newDir);

    if (scoreA >= scoreB) ApplyCornerPlacement(corner.transform, sp1, sp2, railEndPos, newDir);
    else                 ApplyCornerPlacement(corner.transform, sp2, sp1, railEndPos, newDir);

    lastPlacedPillarM = corner;
}

// Draws single/double gate replace buttons above the selected rail/gate in the scene view.
void DrawGateReplaceGizmo(SceneView sv)
{
    if (buildMode) return;

    var sel = Selection.activeGameObject;
    if (!sel) return;

    bool isRail   = IsRailInstance(sel);
    int  gateType = GetGateType(sel);
    if (!isRail && gateType == 0) return;

    // Determine variant tag from fence root
    string variantTag = "V1";
    var root = FindOwningFenceRoot(sel.transform);
    if (root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (!src) continue;
            if (src.name.StartsWith("post_V5")) { variantTag = "V5"; break; }
            if (src.name.StartsWith("post_V4")) { variantTag = "V4"; break; }
            if (src.name.StartsWith("post_V3")) { variantTag = "V3"; break; }
            if (src.name.StartsWith("post_V2")) { variantTag = "V2"; break; }
        }
    }

    // Dynamic labels: the current type shows "Section", the other two show their names
    string labelS = gateType == 1 ? "Section"     : "Single-Gate";
    string labelD = gateType == 2 ? "Section"     : "Double-Gate";

    // Click actions
    string actionS = gateType == 1 ? $"sectionB_{variantTag}_PREFAB"     : $"single_gate_{variantTag}_PREFAB";
    string actionD = gateType == 2 ? $"sectionB_{variantTag}_PREFAB"     : $"double_gate_{variantTag}_PREFAB";

    Vector3 fwd = sel.transform.right;
    fwd.y = 0f;
    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
    fwd.Normalize();

    // Measure actual height from renderer bounds
    float railHeight = 0f;
    var renderers = sel.GetComponentsInChildren<Renderer>(true);
    if (renderers.Length > 0)
    {
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        railHeight = b.max.y - sel.transform.position.y;
    }
    float yBase     = sel.transform.position.y + railHeight + 0.3f;
    Vector3 basePos   = new Vector3(sel.transform.position.x, yBase, sel.transform.position.z);
    Vector3 posSingle = basePos;
    Vector3 posDouble = basePos - Vector3.up * 0.25f;

    // Hit detection using screen-space rect matching the text labels
    int idS = GUIUtility.GetControlID(GateSingleGizmoIdHint, FocusType.Passive);
    int idD = GUIUtility.GetControlID(GateDoubleGizmoIdHint, FocusType.Passive);

    const float lw2 = 160f;
    const float lh  = 22f;

    Vector2 screenS = HandleUtility.WorldToGUIPoint(posSingle);
    Vector2 screenD = HandleUtility.WorldToGUIPoint(posDouble);

    Rect rectS = new Rect(screenS.x - lw2 * 0.5f, screenS.y - lh, lw2, lh);
    Rect rectD = new Rect(screenD.x - lw2 * 0.5f, screenD.y - lh, lw2, lh);

    if (Event.current.type == EventType.Layout)
    {
        Vector2 mp = Event.current.mousePosition;
        HandleUtility.AddControl(idS, DistToRect(mp, rectS));
        HandleUtility.AddControl(idD, DistToRect(mp, rectD));
    }
    bool hoverS = HandleUtility.nearestControl == idS;
    bool hoverD = HandleUtility.nearestControl == idD;
    if (hoverS && Event.current.type == EventType.MouseDown && Event.current.button == 0)
    { Event.current.Use(); ReplaceRailWithGate(sel, actionS); return; }
    if (hoverD && Event.current.type == EventType.MouseDown && Event.current.button == 0)
    { Event.current.Use(); ReplaceRailWithGate(sel, actionD); return; }

    if (Event.current.type != EventType.Repaint) return;

    GUIStyle style = new GUIStyle
    {
        fontSize  = 13,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperCenter,
    };

    Handles.BeginGUI();

    style.normal.textColor = hoverS ? ActiveCol : GateTextCol;
    GUI.Label(rectS, labelS, style);

    style.normal.textColor = hoverD ? ActiveCol : GateTextCol;
    GUI.Label(rectD, labelD, style);

    Handles.EndGUI();
}

// Returns pixel distance from point p to rect r (0 if inside).
static float DistToRect(Vector2 p, Rect r)
{
    float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
    float dy = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
    return Mathf.Sqrt(dx * dx + dy * dy);
}

// Replaces a rail or gate with any fence prefab, aligned via SnapPoint1.
void ReplaceRailWithGate(GameObject rail, string gatePrefabName)
{
    var gatePrefab = FindAsset<GameObject>(gatePrefabName);
    if (!gatePrefab)
    {
        Debug.LogWarning($"[FMS] Gate prefab not found: {gatePrefabName}");
        return;
    }

    var parent  = rail.transform.parent;
    var rot     = rail.transform.rotation;
    var sibling = rail.transform.GetSiblingIndex();

    // Cache rail SnapPoint1 world position before destroying
    Transform railSnap1 = FindSnap(rail.transform, RailStartSnap);
    Vector3 snapWorldPos = railSnap1 ? railSnap1.position : rail.transform.position;

    // Detect if source element was flipped (any renderer with negative Z scale)
    bool wasFlipped = false;
    foreach (var r in rail.GetComponentsInChildren<Renderer>())
    {
        if (r.transform.localScale.z < 0f) { wasFlipped = true; break; }
    }

    // Clear rail selection state BEFORE destroy so repair logic has nothing to act on
    lastSelectionWasRail = false;
    lastRailDeleteFenceRoot = null;
    railIdToFenceRoot.Remove(rail.StableId());

    // Suppress pillar repair - posts adjacent to gate must not be replaced
    suppressDeleteUndo = true;
    Undo.DestroyObjectImmediate(rail);
    suppressDeleteUndo = false;

    var gate = (GameObject)PrefabUtility.InstantiatePrefab(gatePrefab);
    Undo.RegisterCreatedObjectUndo(gate, "Place Gate");
    gate.transform.SetParent(parent, false);
    gate.transform.rotation = rot;

    // Align gate SnapPoint1 to the cached rail SnapPoint1 world position
    Transform gateSnap1 = FindSnap(gate.transform, RailStartSnap);
    if (gateSnap1)
        gate.transform.position += snapWorldPos - gateSnap1.position;
    else
        gate.transform.position = snapWorldPos;

    gate.transform.SetSiblingIndex(sibling);

    // Apply current fence texture variant
    ApplyCurrentTextureVariantToObject(gate);

    // Transfer flip state from source element
    if (wasFlipped)
        FlipVisuals180(gate);

    // Apply full detail mode to gate if active
    if (fullDetailMode)
    {
        foreach (var t in gate.GetComponentsInChildren<Transform>(true))
            SetFullDetailOnObject(t.gameObject, true);
    }

    Selection.activeGameObject = gate;
}

}
} // namespace WB3DAssets.FenceModularSystem
