using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{

void EnterContinueAnchorMode()
{
    // Ensure we use the latest selection state (button click happens on non-Layout events)
    UpdateContinueBuildState();

    if (!selectedContinuePillar)
        return;

    // Capture undo group so all continue-build operations can be collapsed on finalize
    Undo.IncrementCurrentGroup();
    continueUndoGroup = Undo.GetCurrentGroup();

    // --- START-PILLAR CONTINUE CHECK (INDEX 0 ONLY) - BEFORE ANY STATE CHANGES ---
    // --- NOW proceed with normal continue mode setup ---
    continueAnchorPillar = selectedContinuePillar;
    continueAnchorActive = true;

    // remember target fence for finalization
    if (selectedFenceIndex >= 0 &&
        selectedFenceIndex < finalizedFences.Count)
    {
        continueTargetFence =
            finalizedFences[selectedFenceIndex];
    }

    // Flip detection moved below, after activeDir is computed
    continueFlipElements = false;

// --- CONTINUE TOP SYNC (FROM SELECTED PILLAR ONLY) ---
continueTopIndex = GetTopIndexFromPillar(selectedContinuePillar);

    // Force UI (and thus ghosts/commit) to match the target fence
    if (continueTargetFence)
    {
        SyncUiFromFenceRoot(continueTargetFence);
        continueScale = continueTargetFence.transform.localScale;
    }
else
{
    continueTargetFence = null;
    continueScale = Vector3.one;
}

    // hide V1E visually
foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
    r.enabled = false;

// Ensure ghost material exists for continue mode (StartBuildMode is not used here)
if (!ghostMat)
    ghostMat = FindAsset<Material>(GhostMatName);

// Ghost V1M is created AFTER continueSnapProxy/lastPillarSnap is initialized (avoid NRE)

// initialize build context from the CONNECTED snap (the one with an attached rail)
// Continue direction is derived via: activeDir = -snap.right
var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
var snap2 = FindSnap(continueAnchorPillar.transform, "SnapPoint2");

bool snap1Free = snap1 && IsSnapFree(continueAnchorPillar.transform, "SnapPoint1");
bool snap2Free = snap2 && IsSnapFree(continueAnchorPillar.transform, "SnapPoint2");

// Prefer the connected snap (NOT free). If both are free (rare), fallback to SnapPoint2 then SnapPoint1.
Transform snap = null;
if (snap1 && !snap1Free) snap = snap1;
if (snap2 && !snap2Free) snap = snap2;

if (!snap)
    snap = snap2 ? snap2 : snap1;

// If neither snap exists, rollback renderer state and abort cleanly
if (!snap)
{
    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = true;

    continueAnchorPillar = null;
    continueAnchorActive = false;
    return;
}

// proxy keeps snap stable even after deleting the anchor pillar
if (continueSnapProxy)
    DestroyImmediate(continueSnapProxy.gameObject);

var proxyGO = new GameObject("ContinueSnapProxy");
proxyGO.hideFlags = HideFlags.HideAndDontSave;
continueSnapProxy = proxyGO.transform;
continueSnapProxy.SetPositionAndRotation(snap.position, snap.rotation);

lastPlacedPillarM = null;
lastPillarSnap = continueSnapProxy;

// --- V1M/V2M T-SNAP OVERRIDE (from side arrows) ---
if (!string.IsNullOrEmpty(continueTSnapOverride))
{
    var tSnap = FindSnap(
        continueAnchorPillar.transform,
        continueTSnapOverride
    );

    if (tSnap)
        lastPillarSnap = tSnap;

    continueTSnapOverride = null;
}

// --- V1C SNAP OVERRIDE (compute free V1T snap via snap-to-snap alignment) ---
if (isV1MContinueMode && continueAnchorPillar)
{
    var anchorSrc = PrefabUtility.GetCorrespondingObjectFromSource(continueAnchorPillar);
    bool isV1C = anchorSrc &&
        (anchorSrc.name == "post_V1C_PREFAB" || anchorSrc.name == "post_V2C_PREFAB" || anchorSrc.name == "post_V3C_PREFAB" || anchorSrc.name == "post_V4C_PREFAB" || anchorSrc.name == "post_V5C_PREFAB");

    if (isV1C)
    {
        var v1tPrefab = FindAsset<GameObject>(GetPillarPrefabName('T'));
        if (v1tPrefab)
        {
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(v1tPrefab);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.localScale = continueScale;

            var v1cS1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");
            var v1cS2 = FindSnap(continueAnchorPillar.transform, "SnapPoint2");
            Transform[] v1cSnaps = { v1cS1, v1cS2 };

            Vector3 wantDir = continueDirOverride;
            wantDir.y = 0f;
            if (wantDir.sqrMagnitude > 1e-6f) wantDir.Normalize();

            bool found = false;
            string[] snapNames = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };

            // Try each V1T snap pair as "occupied" (matching V1C snaps)
            for (int free = 0; free < 3 && !found; free++)
            {
                int idxA = (free + 1) % 3;
                int idxB = (free + 2) % 3;

                // Try both permutations: matchA→v1cS1 + matchB→v1cS2, and reversed
                for (int perm = 0; perm < 2 && !found; perm++)
                {
                    Transform targetA = v1cSnaps[perm];
                    Transform targetB = v1cSnaps[1 - perm];

                    // Reset V1T to anchor position/rotation
                    temp.transform.SetPositionAndRotation(
                        continueAnchorPillar.transform.position,
                        continueAnchorPillar.transform.rotation);

                    var matchA = FindSnap(temp.transform, snapNames[idxA]);
                    var matchB = FindSnap(temp.transform, snapNames[idxB]);
                    var freeSnap = FindSnap(temp.transform, snapNames[free]);
                    if (!matchA || !matchB || !freeSnap) continue;

                    // 1) Rotate V1T so matchA.right aligns with targetA.right
                    temp.transform.rotation =
                        YawDelta(matchA.right, targetA.right) * temp.transform.rotation;

                    // 2) Translate so matchA.position == targetA.position
                    Vector3 delta = targetA.position - matchA.position;
                    delta.y = 0f;
                    temp.transform.position += delta;

                    // 3) Check if matchB.position ≈ targetB.position (scale-aware tolerance)
                    float dist = Vector3.Distance(matchB.position, targetB.position);
                    float tol = 0.3f * Mathf.Max(continueScale.x, continueScale.y, continueScale.z, 1f);
                    if (dist > tol) continue;

                    // 4) Check free snap direction matches wanted build direction
                    Vector3 fDir = -freeSnap.right; fDir.y = 0f; fDir.Normalize();
                    float dirMatch = Vector3.Dot(fDir, wantDir);
                    if (dirMatch < 0.7f) continue;

                    // Found valid fit
                    continueSnapProxy.SetPositionAndRotation(
                        freeSnap.position, freeSnap.rotation);
                    lastPillarSnap = continueSnapProxy;
                    found = true;
                }
            }

            DestroyImmediate(temp);
        }
    }
}

activeDir = -continueSnapProxy.right; // flip 180° for continue build
activeDir.y = 0f;
if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

// OPTIONAL: override continue direction (used by V1M side arrows)
if (continueDirOverrideActive)
{
    Vector3 od = continueDirOverride;
    od.y = 0f;
    if (od.sqrMagnitude > 1e-6f)
    {
        od.Normalize();

        // Rotate proxy so that -proxy.right becomes the override direction
        Quaternion q = Quaternion.FromToRotation(-continueSnapProxy.right, od);
        continueSnapProxy.rotation = q * continueSnapProxy.rotation;

        activeDir = od;
    }

    continueDirOverrideActive = false;
}

// Flip detection: check connected rail's flip state and which end we're at
if (continueTargetFence && continueAnchorPillar)
{
    Vector3 connPos = snap.position;
    long connKey = PosKey(connPos);
    foreach (Transform t in continueTargetFence.GetComponentsInChildren<Transform>(true))
    {
        if (t == continueTargetFence.transform) continue;
        if (!IsRailInstance(t.gameObject) && GetGateType(t.gameObject) == 0) continue;

        var re = FindSnap(t, RailEndSnap);
        var rs = FindSnap(t, RailStartSnap);
        bool atEnd   = re && PosKey(re.position) == connKey;
        bool atStart = rs && PosKey(rs.position) == connKey;

        // Distance fallback
        if (!atEnd && !atStart)
        {
            if (re && Vector3.Distance(re.position, connPos) < 0.02f) atEnd = true;
            else if (rs && Vector3.Distance(rs.position, connPos) < 0.02f) atStart = true;
        }
        if (!atEnd && !atStart) continue;

        // Check if this rail's visuals are flipped
        bool railFlipped = false;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (r.transform.localScale.z < 0f) { railFlipped = true; break; }
        }

        // atEnd = rail ends here → building forward → match rail flip
        // atStart = rail starts here → building backward → invert rail flip
        continueFlipElements = atEnd ? railFlipped : !railFlipped;
        break;
    }
}

// Create Ghost V1M as visual replacement for Continue Build (NOW lastPillarSnap is valid)
if (continueGhostPillarM)
    DestroyImmediate(continueGhostPillarM);

var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
if (pillarMPrefab)
{
    continueGhostPillarM = CreateGhost(pillarMPrefab);
    continueGhostPillarM.name = "ContinueGhostV1M";

    // Snap Ghost V1M SnapPoint1 to the continue snap (final rail end)
    var ghostSnap = FindSnap(continueGhostPillarM.transform, "SnapPoint1");
    if (ghostSnap && lastPillarSnap)
    {
        // Match the same rule as your V1M placement: SnapPoint1.right -> -activeDir
        continueGhostPillarM.transform.rotation =
            YawDelta(ghostSnap.right, -activeDir) * continueGhostPillarM.transform.rotation;

        continueGhostPillarM.transform.position +=
            lastPillarSnap.position - ghostSnap.position;
    }

    // Add ghost top if fence has tops
    AddGhostTopToPillar(continueGhostPillarM);
}

    ClearRailPreview();
    ClearCurvedGhost();
    ClearHover90Preview();

    buildMode = true;
    state = State.CornerSelect;

    SceneView.duringSceneGui += OnSceneGUI;

Selection.activeObject = null;
Selection.activeGameObject = null;
SceneView.RepaintAll();
    selectedContinuePillar = null;
    canContinueBuild = false;

    Repaint();
}

void UpdateContinueBuildState()
{
    selectedContinuePillar = null;
    canContinueBuild = false;

    var sel = Selection.activeGameObject;
    if (!sel)
        return;

    var sourcePrefab =
        PrefabUtility.GetCorrespondingObjectFromSource(sel);
    if (!sourcePrefab)
        return;

string prefabName = sourcePrefab.name;

// Allowed pillar types
bool isPillar =
    prefabName.StartsWith("post_") &&
    prefabName.EndsWith("_PREFAB");

if (!isPillar)
    return;

// Only V1E is allowed for Continue Build - fences have no V1M/V1C continue
bool isContinueAllowedPillar =
    prefabName == $"post_V1E_PREFAB" ||
    prefabName == $"post_V2E_PREFAB" ||
    prefabName == $"post_V3E_PREFAB" ||
    prefabName == $"post_V4E_PREFAB" ||
    prefabName == $"post_V5E_PREFAB";

if (!isContinueAllowedPillar)
    return;

// Find owning fence root
Transform fenceRoot = sel.transform;
while (fenceRoot != null)
{
    if (finalizedFences.Contains(fenceRoot.gameObject))
        break;

    fenceRoot = fenceRoot.parent;
}

if (!fenceRoot)
    return;

    // Sync UI selection to this fence
    selectedFenceIndex =
        finalizedFences.IndexOf(fenceRoot.gameObject);

    canContinueBuild = true;
    selectedContinuePillar = sel;
}

void DrawContinueDoubleArrowGizmo(Transform pillar)
{
    if (!pillar) return;

    // --- direction from connected rail ---
    Transform s1 = FindSnap(pillar, "SnapPoint1");
    Transform s2 = FindSnap(pillar, "SnapPoint2");

    Vector3 dir;
    if (s1 && !IsSnapFree(pillar, "SnapPoint1"))
        dir = -s1.right;
    else if (s2 && !IsSnapFree(pillar, "SnapPoint2"))
        dir = -s2.right;
    else
        dir = activeDir;

    dir.y = 0f;
    if (dir.sqrMagnitude < 1e-6f) return;
    dir.Normalize();

    float s = HandleUtility.GetHandleSize(pillar.position);
    Vector3 pos = pillar.position + dir * ArrowLength * 0.9f * s;

    // --- HOVER LOGIC (EXACTLY LIKE BUILDMODE) ---
int id = GUIUtility.GetControlID(ContinueGizmoIdHint, FocusType.Passive);

// Measure hover against BOTH arrow heads (and keep it stable)
float spacing = ArrowLength * 0.35f * s;

float dA = HandleUtility.DistanceToCircle(
    pos,
    ArrowHeadSize * 1.25f * s
);

float dB = HandleUtility.DistanceToCircle(
    pos + dir * spacing,
    ArrowHeadSize * 1.25f * s
);

float hoverDist = Mathf.Min(dA, dB);

if (Event.current.type == EventType.Layout)
    HandleUtility.AddControl(id, hoverDist);

bool isHover = HandleUtility.nearestControl == id;

// CLICK = START CONTINUE BUILD MODE
if (isHover &&
    Event.current.type == EventType.MouseDown &&
    Event.current.button == 0)
{
    Event.current.Use();
    EnterContinueAnchorMode();
    return;
}

Handles.color = isHover ? ActiveCol : BaseCol;

    // Arrow 1
    Handles.ConeHandleCap(
        id,
        pos,
        Quaternion.LookRotation(dir),
        ArrowHeadSize * (isHover ? 1.15f : 1f) * s,
        EventType.Repaint
    );

    // Arrow 2
    Handles.ConeHandleCap(
        id,
        pos + dir * spacing,
        Quaternion.LookRotation(dir),
        ArrowHeadSize * (isHover ? 1.15f : 1f) * s,
        EventType.Repaint
    );
}

void EnterCornerSelectFromV1M(Transform pillar, Vector3 dir)
{
    if (!pillar)
        return;

    dir.y = 0f;
    if (dir.sqrMagnitude < 1e-6f)
        return;
    dir.Normalize();

    // Ensure CornerSelect prerequisites
    activeDir = dir;
    buildMode = true;
    state = State.CornerSelect;

    lastPlacedPillarM = pillar.gameObject;

    // Prefer outgoing snap
    lastPillarSnap = FindSnap(pillar, "SnapPoint2");
    if (!lastPillarSnap)
        lastPillarSnap = FindSnap(pillar, "SnapPoint1");

    // Force Scene GUI to stay active
    SceneView.duringSceneGui -= OnSceneGUI;
    SceneView.duringSceneGui += OnSceneGUI;

    Selection.activeObject = null;
    Selection.activeGameObject = null;

    SceneView.RepaintAll();
    Repaint();
}

void DrawV1MSideArrows(Transform pillar)
{
    if (!pillar) return;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(pillar.gameObject);
    if (!src || !IsPillarMPrefab(src.name))
        return;

    Vector3 up = Vector3.up;

    Vector3 fwd = pillar.forward;
    fwd.y = 0f;
    if (fwd.sqrMagnitude < 1e-6f)
        fwd = Vector3.forward;
    fwd.Normalize();

// 90° rotated arrows: use forward/back instead of left/right
Vector3 dir = fwd; // pillar forward defines arrow axis

float s = HandleUtility.GetHandleSize(pillar.position);
float offset = ArrowLength * 0.9f * s;
Vector3 center = pillar.position;

Vector3 leftPos  = center - dir * offset;
Vector3 rightPos = center + dir * offset;

    // UNIQUE control ids (stable!)
    int leftId  = GUIUtility.GetControlID("V1M_LeftArrow".GetHashCode(),  FocusType.Passive);
    int rightId = GUIUtility.GetControlID("V1M_RightArrow".GetHashCode(), FocusType.Passive);

    float hoverRadius = ArrowHeadSize * 1.25f * s;

    // --- HOVER DISTANCE ---
    float dLeft  = HandleUtility.DistanceToCircle(leftPos,  hoverRadius);
    float dRight = HandleUtility.DistanceToCircle(rightPos, hoverRadius);

    if (Event.current.type == EventType.Layout)
    {
        HandleUtility.AddControl(leftId,  dLeft);
        HandleUtility.AddControl(rightId, dRight);
    }

bool hoverLeft  = HandleUtility.nearestControl == leftId;
bool hoverRight = HandleUtility.nearestControl == rightId;

// CLICK: start EXACT same flow as double arrow (EnterContinueAnchorMode)
if ((hoverLeft || hoverRight) &&
    Event.current.type == EventType.MouseDown &&
    Event.current.button == 0 &&
    !Event.current.alt)
{
    Vector3 clickDir = hoverLeft ? -dir : dir;
    clickDir.y = 0f;
    if (clickDir.sqrMagnitude > 1e-6f)
        clickDir.Normalize();

continueDirOverride = clickDir;
    continueDirOverrideActive = true;

    // LEFT = SnapPointT2, RIGHT = SnapPointT1
continueTSnapOverride = hoverLeft ? "SnapPointT1" : "SnapPointT2";

    // V1M variant lock dialog (guarded by feature flag)
    if (ENABLE_V1M_VARIANT_LOCK)
    {
        GameObject targetRoot = null;
        if (selectedFenceIndex >= 0 &&
            selectedFenceIndex < finalizedFences.Count)
            targetRoot = finalizedFences[selectedFenceIndex];

        if (targetRoot && !HasVariantLockMarker(targetRoot))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "V1M Build Mode",
                "You are about to start V1M Build Mode.\n\n" +
                "This will lock variant switching (V1 / V2) for this fence " +
                "to prevent inconsistent geometry.\n\n" +
                "Do you want to continue?",
                "Continue",
                "No"
            );

            if (!confirmed)
            {
                isV1MContinueMode = false;
                continueDirOverrideActive = false;
                continueTSnapOverride = null;
                Event.current.Use();
                return;
            }

            EnsureVariantLockMarker(targetRoot);
        }
    }

    isV1MContinueMode = true; // Activate V1M mode

    Event.current.Use();
    EnterContinueAnchorMode();
    return;
}

    // LEFT arrow
    Handles.color = hoverLeft ? ActiveCol : BaseCol;
    Handles.ConeHandleCap(
        leftId,
        leftPos,
Quaternion.LookRotation(-dir, up),
        ArrowHeadSize * (hoverLeft ? 1.15f : 1f) * s,
        EventType.Repaint
    );

    // RIGHT arrow
    Handles.color = hoverRight ? ActiveCol : BaseCol;
    Handles.ConeHandleCap(
        rightId,
        rightPos,
Quaternion.LookRotation(dir, up),
        ArrowHeadSize * (hoverRight ? 1.15f : 1f) * s,
        EventType.Repaint
    );
}

void DrawV1CSideArrows(Transform pillar)
{
    if (!pillar) return;

    var src = PrefabUtility.GetCorrespondingObjectFromSource(pillar.gameObject);
    if (!src) return;
    bool isV1C = src.name == "post_V1C_PREFAB" || src.name == "post_V2C_PREFAB" || src.name == "post_V3C_PREFAB" || src.name == "post_V4C_PREFAB" || src.name == "post_V5C_PREFAB";
    if (!isV1C) return;

    var s1 = FindSnap(pillar, "SnapPoint1");
    var s2 = FindSnap(pillar, "SnapPoint2");
    if (!s1 || !s2) return;

    // Arrow directions: opposite each snap's rail direction
    Vector3 dir1 = -s1.right; dir1.y = 0f; if (dir1.sqrMagnitude < 1e-6f) return; dir1.Normalize();
    Vector3 dir2 = -s2.right; dir2.y = 0f; if (dir2.sqrMagnitude < 1e-6f) return; dir2.Normalize();

    float hs = HandleUtility.GetHandleSize(pillar.position);
    float offset = ArrowLength * 0.9f * hs;
    Vector3 center = pillar.position;
    Vector3 pos1 = center + dir1 * offset;
    Vector3 pos2 = center + dir2 * offset;

    int id1 = GUIUtility.GetControlID("V1C_Arrow1".GetHashCode(), FocusType.Passive);
    int id2 = GUIUtility.GetControlID("V1C_Arrow2".GetHashCode(), FocusType.Passive);

    float hr = ArrowHeadSize * 1.25f * hs;
    float d1 = HandleUtility.DistanceToCircle(pos1, hr);
    float d2 = HandleUtility.DistanceToCircle(pos2, hr);

    if (Event.current.type == EventType.Layout)
    {
        HandleUtility.AddControl(id1, d1);
        HandleUtility.AddControl(id2, d2);
    }

    bool hover1 = HandleUtility.nearestControl == id1;
    bool hover2 = HandleUtility.nearestControl == id2;

    if ((hover1 || hover2) &&
        Event.current.type == EventType.MouseDown &&
        Event.current.button == 0 &&
        !Event.current.alt)
    {
        continueDirOverride = hover1 ? dir1 : dir2;
        continueDirOverrideActive = true;
        isV1MContinueMode = true; // Reuse V1M mode (same CornerSelect behavior)

        Event.current.Use();
        EnterContinueAnchorMode();
        return;
    }

    Vector3 up = Vector3.up;
    Handles.color = hover1 ? ActiveCol : BaseCol;
    Handles.ConeHandleCap(id1, pos1, Quaternion.LookRotation(dir1, up),
        ArrowHeadSize * (hover1 ? 1.15f : 1f) * hs, EventType.Repaint);

    Handles.color = hover2 ? ActiveCol : BaseCol;
    Handles.ConeHandleCap(id2, pos2, Quaternion.LookRotation(dir2, up),
        ArrowHeadSize * (hover2 ? 1.15f : 1f) * hs, EventType.Repaint);
}

void ApplyContinueTopToPillar(GameObject pillar)
{
    if (!pillar)
        return;

    if (continueTopIndex < 0 || continueTopIndex >= TopPrefabNames.Length)
        return;

    var snapTop = FindSnap(pillar.transform, TopSnapName);
    if (!snapTop)
        return;

    var topPrefab = FindAsset<GameObject>(TopPrefabNames[continueTopIndex]);
    if (!topPrefab)
        return;

    // safety: remove existing
    RemoveTopFromPillar(pillar.transform);

    var top = (GameObject)PrefabUtility.InstantiatePrefab(topPrefab);
    ApplyCurrentTextureVariantToObject(top);

    Undo.RegisterCreatedObjectUndo(top, "Place Top (Continue)");

    top.transform.SetParent(pillar.transform, false);
    top.transform.position = snapTop.position;
    top.transform.rotation = snapTop.rotation;

    ApplyTopVisualRotation(top);
}

int GetTopIndexFromPillar(GameObject pillar)
{
    if (!pillar)
        return -1;

    foreach (Transform t in pillar.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src)
            continue;

        for (int i = 0; i < TopPrefabNames.Length; i++)
        {
            if (src.name == TopPrefabNames[i])
                return i;
        }
    }

    return -1;
}

void ShowContinueAnchorPillar()
{
    if (!continueAnchorPillar) return;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = true;
}

void HideContinueAnchorPillar()
{
    if (!continueAnchorPillar) return;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;
}

void FinalizeActiveCurvedGhostForContinue()
{
    if (!hasCurvedRails || !curvedGhostActive) return;

    var realCurvedPrefab = FindAsset<GameObject>(
    $"sectionBCrvd_{VariantTag}_PREFAB"
);
var pillarMPrefab    = FindAsset<GameObject>(GetPillarPrefabName('M'));
    if (!realCurvedPrefab || !pillarMPrefab) return;

    // Curved Rail
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

    // End Pillar V1M
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

    // update anchors
    lastPlacedPillarM = realPillar;
    lastPillarSnap = FindSnap(realCurved.transform, curvedOutSnapName);

    ClearCurvedGhost();
    curvedGhostActive = false;
}

// Fit a V1T by testing 4 rotations and picking the one where
// the most SnapPoints align with nearby rail SnapPoints.
bool FitV1TToRails(GameObject v1t, Vector3 center)
{
    if (!v1t) return false;

    string[] v1tSnaps = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };
    float scaleFactor = Mathf.Max(continueScale.x, 1f);
    float tol = 0.15f * scaleFactor;
    float searchRadius = 2f * scaleFactor;

    // Collect all rail snap positions in scene near center
    var railSnapPositions = new List<Vector3>();
    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!IsRailInstance(t.gameObject)) continue;
            foreach (var rsn in new[] { RailStartSnap, RailEndSnap })
            {
                var rs = FindSnap(t, rsn);
                if (rs && Vector3.Distance(rs.position, center) < searchRadius)
                    railSnapPositions.Add(rs.position);
            }
        }
    }
    // Also check currentBuildObjects (rails just committed in this session)
    foreach (var go in currentBuildObjects)
    {
        if (!go || !IsRailInstance(go)) continue;
        foreach (var rsn in new[] { RailStartSnap, RailEndSnap })
        {
            var rs = FindSnap(go.transform, rsn);
            if (rs && Vector3.Distance(rs.position, center) < searchRadius)
                railSnapPositions.Add(rs.position);
        }
    }
    // Also check active ghost chain rails and rail segments (not yet committed)
    foreach (var go in hoverChainRails)
    {
        if (!go) continue;
        foreach (var rsn in new[] { RailStartSnap, RailEndSnap })
        {
            var rs = FindSnap(go.transform, rsn);
            if (rs && Vector3.Distance(rs.position, center) < searchRadius)
                railSnapPositions.Add(rs.position);
        }
    }
    foreach (var go in railSegs)
    {
        if (!go) continue;
        foreach (var rsn in new[] { RailStartSnap, RailEndSnap })
        {
            var rs = FindSnap(go.transform, rsn);
            if (rs && Vector3.Distance(rs.position, center) < searchRadius)
                railSnapPositions.Add(rs.position);
        }
    }

    if (railSnapPositions.Count == 0) return false;

    Quaternion baseRot = v1t.transform.rotation;
    int bestMatches = -1;
    Quaternion bestRot = baseRot;

    for (int r = 0; r < 4; r++)
    {
        v1t.transform.rotation = baseRot * Quaternion.Euler(0f, r * 90f, 0f);

        int matches = 0;
        foreach (var sn in v1tSnaps)
        {
            var snap = FindSnap(v1t.transform, sn);
            if (!snap) continue;
            foreach (var rp in railSnapPositions)
            {
                if (Vector3.Distance(snap.position, rp) < tol)
                { matches++; break; }
            }
        }

        if (matches > bestMatches)
        {
            bestMatches = matches;
            bestRot = v1t.transform.rotation;
        }
    }

    v1t.transform.rotation = bestRot;
    return bestMatches >= 2;
}

}
} // namespace WB3DAssets.FenceModularSystem
