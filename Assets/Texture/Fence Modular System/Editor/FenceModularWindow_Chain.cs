using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{
sealed class ChainIndexCache
{
    public readonly Dictionary<FenceId, int> idToIndex = new();
    public int nextIndex;
}

ChainIndexCache GetOrCreateIndexCache(GameObject root)
{
    if (!root) return null;

    if (!chainIndexByRoot.TryGetValue(root, out var cache) || cache == null)
    {
        cache = new ChainIndexCache();
        chainIndexByRoot[root] = cache;
    }
    return cache;
}

void AssignIndices(GameObject root, List<GameObject> objects, int startIndex)
{
    if (!root || objects == null || objects.Count == 0) return;

    var cache = GetOrCreateIndexCache(root);
    if (cache == null) return;

    int idx = startIndex;

    for (int i = 0; i < objects.Count; i++)
    {
        var go = objects[i];
        if (!go) continue;

        cache.idToIndex[go.StableId()] = idx;
        idx++;
    }

    cache.nextIndex = Mathf.Max(cache.nextIndex, idx);
}

void TransferIndex(GameObject root, GameObject oldGO, GameObject newGO)
{
    if (!root || !oldGO || !newGO) return;

    if (!chainIndexByRoot.TryGetValue(root, out var cache) || cache == null)
        return;

    FenceId oldId = oldGO.StableId();
    if (!cache.idToIndex.TryGetValue(oldId, out int idx))
        return;

    cache.idToIndex.Remove(oldId);
    cache.idToIndex[newGO.StableId()] = idx;
}

void RemoveIndexCache(GameObject root)
{
    if (!root) return;
    chainIndexByRoot.Remove(root);
}

bool HasVariantLockMarker(GameObject root)
{
    if (!root) return false;
    // Marker lives at scene root level, named with the root's instance ID
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    foreach (var go in root.scene.GetRootGameObjects())
    {
        if (go.name == markerName)
            return true;
    }
    return false;
}

void EnsureVariantLockMarker(GameObject root)
{
    if (!root || HasVariantLockMarker(root)) return;
    // Create marker at scene root level (not as child of fence)
    // to avoid "dangling child" errors during undo operations.
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    var marker = new GameObject(markerName);
    marker.hideFlags = HideFlags.HideInHierarchy;
    EditorUtility.SetDirty(marker);
}

void RemoveVariantLockMarker(GameObject root)
{
    if (!root) return;
    string markerName = VariantLockMarkerName + "_" + root.StableId();
    foreach (var go in root.scene.GetRootGameObjects())
    {
        if (go.name == markerName)
        {
            DestroyImmediate(go);
        }
    }
}

void EnsureStartMarker(GameObject root, GameObject pillar)
{
    if (!root || !pillar) return;

    Transform existing = null;
    foreach (Transform c in root.transform)
    {
        if (c.name == StartMarkerName) { existing = c; break; }
    }

    if (existing)
    {
        existing.position = pillar.transform.position;
        return;
    }

    var marker = new GameObject(StartMarkerName);
    marker.transform.SetParent(root.transform, false);
    marker.transform.position = pillar.transform.position;
    marker.hideFlags = HideFlags.HideInHierarchy;
    if (!Undo.isProcessing)
        Undo.RegisterCreatedObjectUndo(marker, "Mark Start Pillar");
}

void TransferStartMarker(GameObject root, GameObject oldPillar, GameObject newPillar)
{
    if (!root || !newPillar) return;

    foreach (Transform c in root.transform)
    {
        if (c.name == StartMarkerName)
        {
            // Update position only, keep stored build direction
            c.position = newPillar.transform.position;
            return;
        }
    }

    EnsureStartMarker(root, newPillar);
}

Transform FindStartPillarByMarker(GameObject root)
{
    if (!root) return null;

    // Find marker as direct child of root
    Transform marker = null;
    foreach (Transform c in root.transform)
    {
        if (c.name == StartMarkerName) { marker = c; break; }
    }
    if (!marker) return null;

    // Find nearest pillar to marker position
    Vector3 markerPos = marker.position;
    Transform best = null;
    float bestDist = float.MaxValue;

    foreach (Transform t in root.transform)
    {
        if (t == marker) continue;
        if (!IsPillarInstance(t.gameObject)) continue;
        float d = Vector3.Distance(t.position, markerPos);
        if (d < bestDist) { bestDist = d; best = t; }
    }

    return best;
}

void ScanSceneForExistingFences()
{
    // Clear and rebuild from scene
    finalizedFences.Clear();
    fenceStartPillars.Clear();
    chainIndexByRoot.Clear();

    // Find all root-level GameObjects named "Fence_*"
    var allTransforms = FenceIds.FindAll<Transform>();
    
    foreach (var t in allTransforms)
    {
        if (!t) continue;
        if (t.parent != null) continue; // only root objects
        if (!t.name.StartsWith("Fence_")) continue;
        
        // Verify it contains fence content (pillars or rails)
        bool hasContent = false;
        
        foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
        {
            if (child == t) continue;
            
            var src = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
            if (!src) continue;
            
            string n = src.name;
            
            if (n.StartsWith("post_") && n.EndsWith("_PREFAB"))
                hasContent = true;
            
            if ((n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
                 n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) && n.EndsWith("_PREFAB"))
                hasContent = true;
            
            if (hasContent) break;
        }
        
        if (hasContent)
        {
            finalizedFences.Add(t.gameObject);
            // NOTE: fenceStartPillars is set by RebuildChainIndexForRoot
            // which is called right after this scan in OnUndoRedoPerformed.
        }
    }

    // Apply Full Detail Mode to all discovered fences
    if (fullDetailMode)
        ApplyFullDetailToAllFences(true);
}

int GetNextFenceNumber()
{
    var usedNumbers = new HashSet<int>();

    // Scan scene directly (robust even if finalizedFences is stale)
    foreach (var t in FenceIds.FindAll<Transform>())
    {
        if (!t || t.parent != null) continue;
        if (!t.name.StartsWith("Fence_")) continue;
        string numStr = t.name.Substring("Fence_".Length);
        if (int.TryParse(numStr, out int num))
            usedNumbers.Add(num);
    }

    int next = 1;
    while (usedNumbers.Contains(next))
        next++;
    return next;
}

void RebuildProtectedPillarIdCache()
{
    protectedPillarIds.Clear();

    // finalized fences
    foreach (var root in finalizedFences)
    {
        if (!root) continue;

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t) continue;

            if (IsPillarInstance(t.gameObject))
                protectedPillarIds.Add(t.gameObject.StableId());
        }
    }

// active build session
foreach (var go in currentBuildObjects)
{
    if (!go) continue;

    if (IsPillarInstance(go))
        protectedPillarIds.Add(go.StableId());
}
}

void HandleRailDeletedAndCleanupPillars(GameObject deletedRail)
{
    if (!deletedRail)
        return;

    // Find owning fence root
    var root = FindOwningFenceRoot(deletedRail.transform);
    if (!root)
        return;

    // Collect candidate pillars FIRST
    var pillars = new List<Transform>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (IsPillarInstance(t.gameObject))
            pillars.Add(t);
    }

    suppressDeleteUndo = true;

    foreach (var pillar in pillars)
    {
        // Check free snaps
        bool snap1Free = IsSnapFree(pillar, "SnapPoint1");
        bool snap2Free = IsSnapFree(pillar, "SnapPoint2");

        if (!snap1Free && !snap2Free)
            continue;

        // Prefer SnapPoint1, fallback SnapPoint2
        string freeSnapName = snap1Free ? "SnapPoint1" : "SnapPoint2";
        var freeSnap = FindSnap(pillar, freeSnapName);
        if (!freeSnap)
            continue;

        ReplacePillarWithV1E_AtFreeSnap(pillar.gameObject, freeSnap);
    }

    suppressDeleteUndo = false;

    if (fullDetailMode && root)
        ApplyFullDetailToFence(root.gameObject, true);

    RebuildProtectedPillarIdCache();
}

void OnObjectChanges_BlockPillarDelete(ref ObjectChangeEventStream stream)
{
    if (suppressDeleteUndo)
        return;

    bool railRepairDone = false;

    for (int i = 0; i < stream.length; i++)
    {
        if (stream.GetEventType(i) != ObjectChangeKind.DestroyGameObjectHierarchy)
            continue;

        stream.GetDestroyGameObjectHierarchyEvent(i, out var evt);
#if UNITY_6000_5_OR_NEWER
        FenceId id = FenceId.FromEvent(evt.entityId);
#else
        FenceId id = FenceId.FromEvent(evt.instanceId);
#endif


        // --- RAIL DELETE REPAIR ---
        GameObject repairRoot = null;
        if (railIdToFenceRoot.TryGetValue(id, out var mappedRoot) && mappedRoot)
        {
            repairRoot = mappedRoot;
            railIdToFenceRoot.Remove(id);
        }
        else if (!railRepairDone && lastSelectionWasRail && lastRailDeleteFenceRoot)
        {
            repairRoot = lastRailDeleteFenceRoot;
        }


        if (repairRoot && !railRepairDone)
        {
            railRepairDone = true;

            suppressDeleteUndo = true;
            RepairEndPillarsAfterRailDelete(repairRoot);
            suppressDeleteUndo = false;

            if (!deletedRailIdsByRoot.TryGetValue(repairRoot, out var ids))
            {
                ids = new List<FenceId>();
                deletedRailIdsByRoot[repairRoot] = ids;
            }
            ids.Add(id);

            lastSelectionWasRail = false;
            lastRailDeleteFenceRoot = null;
            railCoSelectedPillarIds.Clear();

            RebuildProtectedPillarIdCache();
            continue;
        }

        // --- BLOCK PILLAR DELETE ---
        // Only block if user manually selected a pillar and pressed Delete.
        // During undo/redo, pillars are destroyed programmatically and must NOT be blocked.
        // Also allow deletion if pillar was auto-co-selected with a rail.
        if (lastSelectionWasPillar && protectedPillarIds.Contains(id))
        {
            suppressDeleteUndo = true;
            Undo.PerformUndo();
            suppressDeleteUndo = false;

            // NOTE: do NOT reset lastSelectionWasPillar here,
            // the pillar is still selected after undo
            RebuildProtectedPillarIdCache();
            return;
        }
    }
}

void OnUndoRedoPerformed()
{
    // 1) Rescan scene for fence roots (handles destroyed/restored roots)
    ScanSceneForExistingFences();

    // 2) Rebuild chain indices from actual hierarchy
    RebuildAllChainIndices();

    // 3) Rebuild pillar protection cache
    RebuildProtectedPillarIdCache();

    // 4) Per-root variant lock: only unlock if the FIRST deleted rail has been restored
    var rootsToUnlock = new List<GameObject>();
    foreach (var kvp in deletedRailIdsByRoot)
    {
        var root = kvp.Key;
        var ids  = kvp.Value;
        if (!root || ids.Count == 0) { rootsToUnlock.Add(root); continue; }

        // Remove restored rails from the end of the list (last undo restores last delete)
        while (ids.Count > 0)
        {
            FenceId lastId = ids[ids.Count - 1];
            var obj = FenceIds.ObjectFromId(lastId);
            if (obj == null) break; // still deleted — stop checking
            ids.RemoveAt(ids.Count - 1);
        }

        // All deleted rails restored → unlock
        if (ids.Count == 0)
            rootsToUnlock.Add(root);
    }

    foreach (var root in rootsToUnlock)
    {
        deletedRailIdsByRoot.Remove(root);
    }

    // 5) Reset cached rail selection state (stale after undo)
    //    Skip if we're inside a programmatic undo (pillar delete protection)
    if (!suppressDeleteUndo)
    {
        lastSelectionWasRail = false;
        lastSelectionWasPillar = false;
        lastRailDeleteFenceRoot = null;
        railCoSelectedPillarIds.Clear();
    }

    // 6) Re-enable renderers on pillars restored by undo.
    //    When a pillar is hidden during Continue Anchor mode (renderers disabled)
    //    and then destroyed with Undo.DestroyObjectImmediate, undo restores it
    //    with disabled renderers. Fix: re-show any pillar that is NOT the current
    //    continue anchor but has all renderers disabled.
    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t || t == root.transform) continue;
            if (!IsPillarInstance(t.gameObject)) continue;

            // Skip the currently active continue anchor (it's supposed to be hidden)
            if (continueAnchorActive && continueAnchorPillar == t.gameObject)
                continue;

            var renderers = t.gameObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            // Check if ALL renderers are disabled (sign of an undo-restored hidden pillar)
            bool allDisabled = true;
            foreach (var r in renderers)
            {
                if (r.enabled) { allDisabled = false; break; }
            }

            if (allDisabled)
            {
                foreach (var r in renderers)
                    r.enabled = true;
            }
        }
    }

    // 7) Remove variant lock marker if no lock reason remains after undo.
    //    Lock reasons: V1T/V1C/V1C45 pillars present, or tracked deleted rails.
    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        if (!HasVariantLockMarker(root)) continue;

        // Check if any lock-requiring pillar types still exist
        bool hasLockReason = false;

        // Check for tracked deleted rails
        if (deletedRailIdsByRoot.TryGetValue(root, out var delIds) && delIds.Count > 0)
            hasLockReason = true;

        if (!hasLockReason)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (!src) continue;
                string n = src.name;
                // Only V1T/V2T (T-pieces) require lock; V1C corners do NOT
                if (n.Contains("V1T_") || n.Contains("V2T_") || n.Contains("V3T_") || n.Contains("V4T_") || n.Contains("V5T_"))
                {
                    hasLockReason = true;
                    break;
                }
            }
        }

        if (!hasLockReason)
            RemoveVariantLockMarker(root);
    }

    // 8) Reapply Full Detail Mode (undo may restore objects with active LODGroups)
    if (fullDetailMode)
        ApplyFullDetailToAllFences(true);

    // 9) Refresh UI
    Repaint();
    SceneView.RepaintAll();
}

void RebuildAllChainIndices()
{
    chainIndexByRoot.Clear();

    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        RebuildChainIndexForRoot(root);
    }
}

void RebuildChainIndexForRoot(GameObject root)
{
    if (!root) return;

    var cache = GetOrCreateIndexCache(root);
    cache.idToIndex.Clear();
    cache.nextIndex = 0;

    // --- Collect all pillars and rails in this root ---
    var pillars = new List<Transform>();
    var rails   = new List<Transform>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t == root.transform) continue;

        var go = t.gameObject;
        if (!go) continue;

        if (IsPillarInstance(go))
            pillars.Add(t);
        else if (IsRailInstance(go))
            rails.Add(t);
    }

    if (pillars.Count == 0) return;

    // --- Build snap-position lookup for all rails ---
    // Key = quantized snap position, Value = (rail Transform, snapName)
    var snapToRail = new Dictionary<long, (Transform rail, string snapName)>();

    foreach (var rail in rails)
    {
        var s1 = FindSnap(rail, RailStartSnap);
        var s2 = FindSnap(rail, RailEndSnap);

        if (s1) snapToRail[PosKey(s1.position)] = (rail, RailStartSnap);
        if (s2) snapToRail[PosKey(s2.position)] = (rail, RailEndSnap);
    }

    // --- Find start pillar ---
    // PRIMARY: look for the persistent __BMS_START__ marker
    Transform startPillar = FindStartPillarByMarker(root);

    // Build pillar snap lookup (needed for chain walk below)
    var snapToPillar = new Dictionary<long, (Transform pillar, string snapName)>();

    foreach (var pillar in pillars)
    {
        var s1 = FindSnap(pillar, "SnapPoint1");
        var s2 = FindSnap(pillar, "SnapPoint2");

        if (s1) snapToPillar[PosKey(s1.position)] = (pillar, "SnapPoint1");
        if (s2) snapToPillar[PosKey(s2.position)] = (pillar, "SnapPoint2");
    }

    // FALLBACK: no marker found (legacy fence or marker lost)
    // During undo processing, prefer the previously known start pillar if it still exists,
    // because undo may restore it in a later step and the sibling-index heuristic can pick wrong.
    if (!startPillar)
    {
        // Check if we already know the start pillar from a previous rebuild
        if (fenceStartPillars.TryGetValue(root, out var knownStart) && knownStart)
        {
            // Verify it's still a child of this root
            if (knownStart.transform.parent == root.transform)
                startPillar = knownStart.transform;
        }
    }

    if (!startPillar)
    {
        // Use V1E/V2E with lowest sibling index among direct root children.
        Transform bestCandidate = null;
        int bestSibling = int.MaxValue;

        foreach (var p in pillars)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(p.gameObject);
            if (!src) continue;
            if (!src.name.Contains("E_PREFAB")) continue;

            // Only consider DIRECT children of root
            if (p.parent != root.transform) continue;

            int si = p.GetSiblingIndex();
            if (si < bestSibling)
            {
                bestSibling = si;
                bestCandidate = p;
            }
        }

        startPillar = bestCandidate;

        // Only add marker outside of undo processing to avoid marking the wrong pillar
        if (startPillar && !Undo.isProcessing)
            EnsureStartMarker(root, startPillar.gameObject);
    }

    if (!startPillar) return;

    // Update fenceStartPillars so other systems stay consistent
    fenceStartPillars[root] = startPillar.gameObject;

    // --- Walk the chain starting from startPillar ---
    var visited = new HashSet<FenceId>();
    int index = 0;
    const float ChainSnapTol = 0.01f;

    Transform current = startPillar;

    while (current != null)
    {
        FenceId currentId = current.gameObject.StableId();
        if (visited.Contains(currentId))
            break;

        visited.Add(currentId);
        cache.idToIndex[currentId] = index++;

        // Find outgoing snap of current pillar
        // Try SnapPoint1 first, then SnapPoint2
        Transform outSnap = null;
        string[] snapNames = { "SnapPoint1", "SnapPoint2" };

        foreach (var sn in snapNames)
        {
            var snap = FindSnap(current, sn);
            if (!snap) continue;

            long key = PosKey(snap.position);

            // Look for a rail connected at this snap
            if (snapToRail.TryGetValue(key, out var railInfo))
            {
                FenceId railId = railInfo.rail.gameObject.StableId();
                if (!visited.Contains(railId))
                {
                    outSnap = snap;
                    break;
                }
            }
        }

        // Distance fallback for outSnap
        if (outSnap == null)
        {
            foreach (var sn in snapNames)
            {
                var snap = FindSnap(current, sn);
                if (!snap) continue;

                float bestDist = ChainSnapTol;
                foreach (var kvp in snapToRail)
                {
                    var railSnap = FindSnap(kvp.Value.rail, kvp.Value.snapName);
                    if (!railSnap) continue;
                    float d = Vector3.Distance(snap.position, railSnap.position);
                    if (d < bestDist && !visited.Contains(kvp.Value.rail.gameObject.StableId()))
                    {
                        bestDist = d;
                        outSnap = snap;
                    }
                }
                if (outSnap != null) break;
            }
        }

        if (outSnap == null)
            break;

        // Find the rail connected to this outgoing snap
        long outKey = PosKey(outSnap.position);
        (Transform rail, string snapName) connectedRail = default;
        bool foundRail = snapToRail.TryGetValue(outKey, out connectedRail);

        // Distance fallback for rail lookup
        if (!foundRail)
        {
            float bestDist = ChainSnapTol;
            foreach (var kvp in snapToRail)
            {
                var railSnap = FindSnap(kvp.Value.rail, kvp.Value.snapName);
                if (!railSnap) continue;
                float d = Vector3.Distance(outSnap.position, railSnap.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    connectedRail = kvp.Value;
                    foundRail = true;
                }
            }
        }

        if (!foundRail)
            break;

        FenceId connRailId = connectedRail.rail.gameObject.StableId();
        if (visited.Contains(connRailId))
            break;

        visited.Add(connRailId);
        cache.idToIndex[connRailId] = index++;

        // Find the OTHER snap of this rail (the end that connects to the next pillar)
        string otherSnapName = connectedRail.snapName == RailStartSnap
            ? RailEndSnap
            : RailStartSnap;

        var railOtherSnap = FindSnap(connectedRail.rail, otherSnapName);
        if (!railOtherSnap)
            break;

        // Find the next pillar at the rail's other end
        long nextKey = PosKey(railOtherSnap.position);
        (Transform pillar, string snapName) nextPillarInfo = default;
        bool foundPillar = snapToPillar.TryGetValue(nextKey, out nextPillarInfo);

        // Distance fallback for pillar lookup
        if (!foundPillar)
        {
            float bestDist = ChainSnapTol;
            foreach (var kvp in snapToPillar)
            {
                var pillarSnap = FindSnap(kvp.Value.pillar, kvp.Value.snapName);
                if (!pillarSnap) continue;
                float d = Vector3.Distance(railOtherSnap.position, pillarSnap.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    nextPillarInfo = kvp.Value;
                    foundPillar = true;
                }
            }
        }

        if (!foundPillar)
            break;

        FenceId nextPillarId = nextPillarInfo.pillar.gameObject.StableId();
        if (visited.Contains(nextPillarId))
            break;

        current = nextPillarInfo.pillar;
    }

    cache.nextIndex = index;
}

void CleanupFinalizedFences()
{
    for (int i = finalizedFences.Count - 1; i >= 0; i--)
    {
if (finalizedFences[i] == null)
{
    var deadRoot = finalizedFences[i];
    finalizedFences.RemoveAt(i);
fenceStartPillars.Remove(deadRoot);
deletedRailIdsByRoot.Remove(deadRoot);
RemoveIndexCache(deadRoot);
// Evict cached positions for all variant tags of this root
}
    }

    // Clamp selection index
    if (selectedFenceIndex >= finalizedFences.Count)
        selectedFenceIndex = finalizedFences.Count - 1;

    if (finalizedFences.Count == 0)
        selectedFenceIndex = -1;
}

// Find pillars connected to a rail for visual co-selection.
// onlyOrphans: true = only co-select pillars with exactly one occupied snap point
List<GameObject> FindCoSelectPillarsForRail(GameObject rail, bool onlyOrphans = false)
{
    var result = new List<GameObject>();
    if (!rail) return result;
    var root = FindOwningFenceRoot(rail.transform);
    if (!root) return result;

    var rs1 = FindSnap(rail.transform, RailStartSnap);
    var rs2 = FindSnap(rail.transform, RailEndSnap);
    if (!rs1 && !rs2) return result;

    // Collect ALL rail AND gate snap positions in this fence
    var allRailSnapPositions = new List<Vector3>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        if (!IsRailInstance(t.gameObject) && GetGateType(t.gameObject) == 0) continue;
        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);
        if (s1) allRailSnapPositions.Add(s1.position);
        if (s2) allRailSnapPositions.Add(s2.position);
    }

    // Scale-aware tolerance (root may be scaled)
    float scaleFactor = root ? Mathf.Max(root.lossyScale.x, root.lossyScale.y, root.lossyScale.z, 1f) : 1f;
    float tol = 0.15f * scaleFactor;
    var connected = new List<(GameObject go, bool bothOccupied)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;

        // Never co-select corner or T-pillars as orphans — repair logic handles them
        var psrc = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (psrc && (psrc.name.Contains("C_PREFAB") || psrc.name.Contains("C45_PREFAB") || psrc.name.Contains("T_PREFAB"))) continue;

        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        bool match =
            (p1 && rs1 && Vector3.Distance(p1.position, rs1.position) < tol) ||
            (p1 && rs2 && Vector3.Distance(p1.position, rs2.position) < tol) ||
            (p2 && rs1 && Vector3.Distance(p2.position, rs1.position) < tol) ||
            (p2 && rs2 && Vector3.Distance(p2.position, rs2.position) < tol);
        if (!match) continue;

        // Count how many snap points are occupied by any rail
        bool p1Occupied = false, p2Occupied = false;
        if (p1) foreach (var rsp in allRailSnapPositions)
            if (Vector3.Distance(p1.position, rsp) < tol) { p1Occupied = true; break; }
        if (p2) foreach (var rsp in allRailSnapPositions)
            if (Vector3.Distance(p2.position, rsp) < tol) { p2Occupied = true; break; }

        // E-pillar (no SnapPoint2): fully occupied if SnapPoint1 is occupied
        bool bothOccupied = p2 ? (p1Occupied && p2Occupied) : p1Occupied;

        connected.Add((t.gameObject, bothOccupied));
    }

    // onlyOrphans: skip pillars where both snap points are occupied
    foreach (var c in connected)
        if (!onlyOrphans || !c.bothOccupied) result.Add(c.go);

    return result;
}

// Find pillars that sit between two or more selected rails (multi-select co-selection).
// A pillar qualifies if its snap points connect to at least 2 different selected rails.
List<GameObject> FindPillarsBetweenRails(GameObject[] selectedRails)
{
    var result = new List<GameObject>();
    if (selectedRails == null || selectedRails.Length < 2) return result;

    // Collect snap positions per rail: (railInstanceId, position)
    var railSnaps = new List<(FenceId railId, Vector3 pos)>();
    foreach (var rail in selectedRails)
    {
        if (!rail) continue;
        FenceId id = rail.StableId();
        var s1 = FindSnap(rail.transform, RailStartSnap);
        var s2 = FindSnap(rail.transform, RailEndSnap);
        if (s1) railSnaps.Add((id, s1.position));
        if (s2) railSnaps.Add((id, s2.position));
    }

    // Gather all pillars from relevant fence roots
    var roots = new HashSet<Transform>();
    foreach (var rail in selectedRails)
    {
        if (!rail) continue;
        var r = FindOwningFenceRoot(rail.transform);
        if (r) roots.Add(r);
    }

    // Scale-aware tolerance (roots may be scaled)
    float maxScale = 1f;
    foreach (var root in roots)
        if (root) maxScale = Mathf.Max(maxScale, root.lossyScale.x, root.lossyScale.y, root.lossyScale.z);
    float tol = 0.15f * maxScale;
    var seen = new HashSet<FenceId>();

    foreach (var root in roots)
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (!t || !IsPillarInstance(t.gameObject)) continue;
        FenceId pid = t.gameObject.StableId();
        if (seen.Contains(pid)) continue;

        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        // Find which selected rails each snap point touches
        FenceId p1Rail = FenceId.None, p2Rail = FenceId.None;
        foreach (var (railId, pos) in railSnaps)
        {
            if (p1 && p1Rail == FenceId.None && Vector3.Distance(p1.position, pos) < tol) p1Rail = railId;
            if (p2 && p2Rail == FenceId.None && Vector3.Distance(p2.position, pos) < tol) p2Rail = railId;
        }

        // Both snaps touch different selected rails → pillar is between them
        if (p1Rail != FenceId.None && p2Rail != FenceId.None && p1Rail != p2Rail)
        {
            seen.Add(pid);
            result.Add(t.gameObject);
        }
    }

    return result;
}

void RepairEndPillarsAfterRailDelete(GameObject fenceRoot)
{
    if (!fenceRoot)
        return;

    // Build a map of ALL rail snap positions (start + end) still present in this root
    var railSnapByPos = new Dictionary<long, Transform>();

    foreach (Transform t in fenceRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;
        bool isRail =
            (n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
             n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) &&
            n.EndsWith("_PREFAB");

        if (!isRail)
            continue;

        var s1 = FindSnap(t, RailStartSnap);
        var s2 = FindSnap(t, RailEndSnap);

        if (s1)
            railSnapByPos[PosKey(s1.position)] = s1;
        if (s2)
            railSnapByPos[PosKey(s2.position)] = s2;
    }

    // Separate lists: pillars to DELETE, REPLACE with V1E, or REPLACE V1T→V1M
    var toDelete = new List<GameObject>();
    var toReplace = new List<GameObject>();
    var toReplaceTPillarWithM = new List<GameObject>();
    var toReplaceTPillarWithC = new List<(GameObject pillar, Transform railSnap0, Transform railSnap1)>();

    foreach (Transform t in fenceRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;

        if (!IsPillarInstance(t.gameObject))
            continue;

        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string prefabName = src.name;
        bool isEndPillar = prefabName.Contains("V1E") || prefabName.Contains("V2E") || prefabName.Contains("V3E") || prefabName.Contains("V4E") || prefabName.Contains("V5E");
        bool isTPillar   = prefabName.Contains("V1T") || prefabName.Contains("V2T") || prefabName.Contains("V3T") || prefabName.Contains("V4T") || prefabName.Contains("V5T");

        var p1 = FindSnap(t, "SnapPoint1");
        var p2 = FindSnap(t, "SnapPoint2");

        if (!p1 && !p2)
            continue;

        bool occ1 = p1 && railSnapByPos.ContainsKey(PosKey(p1.position));
        bool occ2 = p2 && railSnapByPos.ContainsKey(PosKey(p2.position));

        // Distance fallback per unmatched snap. Split tolerances:
        // X/Z must be tight (pillar's own two snaps are only ~5cm apart),
        // Y can be loose (terrain slope ±3° causes up to ~0.13m Y mismatch
        // when rail stays at prev-Y while pillar climbs).
        if (railSnapByPos.Count > 0)
        {
            const float tolXZ = 0.02f;
            const float tolY  = 0.2f;
            foreach (var kvp in railSnapByPos)
            {
                Vector3 rp = kvp.Value.position;
                if (!occ1 && p1)
                {
                    Vector3 d = p1.position - rp;
                    if (Mathf.Abs(d.y) < tolY && new Vector2(d.x, d.z).sqrMagnitude < tolXZ * tolXZ)
                        occ1 = true;
                }
                if (!occ2 && p2)
                {
                    Vector3 d = p2.position - rp;
                    if (Mathf.Abs(d.y) < tolY && new Vector2(d.x, d.z).sqrMagnitude < tolXZ * tolXZ)
                        occ2 = true;
                }
                if (occ1 && occ2) break;
            }
        }

        // V1E/V2E: DELETE if has free snap (they only have SnapPoint1)
        if (isEndPillar)
        {
            if (!occ1)
                toDelete.Add(t.gameObject);
            continue;
        }

        // V1T/V2T: Count rails connected to this T-pillar.
        // IMPORTANT: V1T is placed with 90° Y rotation relative to V1M, so its snap
        // world positions do NOT match the rail snap positions. The rails were placed
        // to connect to the PREVIOUS V1M's snap positions before it was replaced by V1T.
        // Therefore we cannot use snap-to-snap matching at all.
        // Instead, count how many rail snaps are near the V1T's CENTER position.
        if (isTPillar)
        {
            // Compute tolerance from the pillar's own snap-to-center distances
            float maxSnapDist = 0f;
            var pt1 = FindSnap(t, "SnapPointT1");
            var pt2 = FindSnap(t, "SnapPointT2");
            if (p1)  maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, p1.position));
            if (p2)  maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, p2.position));
            if (pt1) maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, pt1.position));
            if (pt2) maxSnapDist = Mathf.Max(maxSnapDist, Vector3.Distance(t.position, pt2.position));
            float tolerance = maxSnapDist + 0.1f; // snap radius + buffer

            // Count unique rails that have a snap within tolerance of this pillar's center
            var nearbyRails = new HashSet<Transform>(); // track by rail root transform
            var nearbyRailSnaps = new List<Transform>(); // one snap per unique rail
            foreach (var kvp in railSnapByPos)
            {
                Transform railSnap = kvp.Value;
                if (Vector3.Distance(t.position, railSnap.position) < tolerance)
                {
                    // Add the rail's root (parent) to avoid counting both snaps of same rail
                    if (nearbyRails.Add(railSnap.parent))
                        nearbyRailSnaps.Add(railSnap);
                }
            }

            int connectedRailCount = nearbyRails.Count;

            // V1T has exactly 3 connections (user confirmed: all or nothing)
            if (connectedRailCount >= 3)
            {
                // All rails still connected → keep V1T (deleted rail was unrelated)
            }
            else if (connectedRailCount == 2)
            {
                // 2 rails remain: check angle to decide V1C (corner) vs V1M (straight)
                Vector3 d0 = (nearbyRailSnaps[0].position - t.position); d0.y = 0f; d0.Normalize();
                Vector3 d1 = (nearbyRailSnaps[1].position - t.position); d1.y = 0f; d1.Normalize();
                float dot = Vector3.Dot(d0, d1);

                if (dot > -0.7f) // angle < ~135° → corner → V1C
                    toReplaceTPillarWithC.Add((t.gameObject, nearbyRailSnaps[0], nearbyRailSnaps[1]));
                else
                    toReplaceTPillarWithM.Add(t.gameObject);
            }
            else if (connectedRailCount == 1)
            {
                // Lost 2 connections → replace with V1M (single-axis pillar)
                toReplaceTPillarWithM.Add(t.gameObject);
            }
            else
            {
                // No connections → delete
                toDelete.Add(t.gameObject);
            }
            continue;
        }

        // Other pillars: check snap occupancy
        bool bothOccupied = occ1 && occ2;
        bool hasFreeSnap = !occ1 || !occ2;

        if (bothOccupied)
        {
            // Both snaps still connected -> no action needed
            continue;
        }
        else if (hasFreeSnap && (occ1 || occ2))
        {
            // One snap free, one occupied -> REPLACE with V1E
            toReplace.Add(t.gameObject);
        }
        else
        {
            // Both snaps free -> DELETE
            toDelete.Add(t.gameObject);
        }
    }


    // --- DELETE pillars ---
    foreach (var pillar in toDelete)
    {
        if (!pillar) continue;
        protectedPillarIds.Remove(pillar.StableId());
        Undo.DestroyObjectImmediate(pillar);
    }

    // --- REPLACE pillars with V1E ---
    if (toReplace.Count > 0)
    {
        var pillarEPrefab = FindAsset<GameObject>($"post_{VariantTagOf(DetectVariantFromFenceRoot(fenceRoot))}E_PREFAB");
        if (!pillarEPrefab)
            return;

        foreach (var oldPillar in toReplace)
        {
            if (!oldPillar) continue;

            Transform parent = oldPillar.transform.parent;
            int sibling = oldPillar.transform.GetSiblingIndex();

            // Preserve top choice from the pillar we replace
            int topIdx = GetTopIndexFromPillar(oldPillar);

            // Find which side is still connected to a rail
            var oldT = oldPillar.transform;
            var oldS1 = FindSnap(oldT, "SnapPoint1");
            var oldS2 = FindSnap(oldT, "SnapPoint2");

            Transform connectedRailSnap = null;

            if (oldS1)
            {
                var k = PosKey(oldS1.position);
                if (railSnapByPos.TryGetValue(k, out var rs))
                    connectedRailSnap = rs;
            }

            if (!connectedRailSnap && oldS2)
            {
                var k = PosKey(oldS2.position);
                if (railSnapByPos.TryGetValue(k, out var rs))
                    connectedRailSnap = rs;
            }

            // Distance fallback after variant switch drift / terrain slope Y mismatch.
            // Split tolerances (see RepairEndPillarsAfterRailDelete for rationale).
            if (!connectedRailSnap)
            {
                const float tolXZ = 0.02f;
                const float tolY  = 0.2f;
                float bestXZ = tolXZ * tolXZ;
                foreach (var kvp in railSnapByPos)
                {
                    Vector3 rp = kvp.Value.position;
                    if (oldS1)
                    {
                        Vector3 d = oldS1.position - rp;
                        float xz = new Vector2(d.x, d.z).sqrMagnitude;
                        if (Mathf.Abs(d.y) < tolY && xz < bestXZ)
                        { bestXZ = xz; connectedRailSnap = kvp.Value; }
                    }
                    if (oldS2)
                    {
                        Vector3 d = oldS2.position - rp;
                        float xz = new Vector2(d.x, d.z).sqrMagnitude;
                        if (Mathf.Abs(d.y) < tolY && xz < bestXZ)
                        { bestXZ = xz; connectedRailSnap = kvp.Value; }
                    }
                }
            }

            // Create new V1E
            var newPillar = InstantiateAndSwap(pillarEPrefab);
            TransferIndex(fenceRoot, oldPillar, newPillar);
            ApplyCurrentTextureVariantToObject(newPillar);
            Undo.RegisterCreatedObjectUndo(newPillar, "Replace Pillar With V1E");

            if (parent)
                newPillar.transform.SetParent(parent, true);
            newPillar.transform.SetSiblingIndex(sibling);

            newPillar.transform.SetPositionAndRotation(oldPillar.transform.position, oldPillar.transform.rotation);
            newPillar.transform.localScale = oldPillar.transform.localScale;
            var v1eSnap1 = FindSnap(newPillar.transform, PillarSnapName);
            if (v1eSnap1 && connectedRailSnap)
            {
                Vector3 toDir = -connectedRailSnap.right;
                toDir.y = 0f;
                if (toDir.sqrMagnitude > 1e-6f)
                {
                    newPillar.transform.rotation =
                        YawDelta(v1eSnap1.right, toDir.normalized) * newPillar.transform.rotation;
                }

                // Snap-align X/Z only - Y must never change on section delete
                Vector3 alignDelta = connectedRailSnap.position - v1eSnap1.position;
                alignDelta.y = 0f;
                newPillar.transform.position += alignDelta;
            }

            // Apply top back
            if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
            {
                var snapTop = FindSnap(newPillar.transform, TopSnapName);
                var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                if (snapTop && topPrefab)
                {
                    RemoveTopFromPillar(newPillar.transform);

                    var top = InstantiateAndSwap(topPrefab);
                    ApplyCurrentTextureVariantToObject(top);
                    Undo.RegisterCreatedObjectUndo(top, "Restore Top");

                    top.transform.SetParent(newPillar.transform, false);
                    top.transform.position = snapTop.position;
                    top.transform.rotation = snapTop.rotation;

                    ApplyTopVisualRotation(top);
                }
            }

            // Transfer flip state
            TransferStartMarker(fenceRoot, oldPillar, newPillar);

            // Remove old pillar
            protectedPillarIds.Remove(oldPillar.StableId());
            Undo.DestroyObjectImmediate(oldPillar);
        }
    }

    // --- REPLACE V1T/V2T pillars with V1M/V2M (T-branch rail deleted) ---
    if (toReplaceTPillarWithM.Count > 0)
    {
        string v = VariantTagOf(DetectVariantFromFenceRoot(fenceRoot));
        var pillarMPrefab = FindAsset<GameObject>($"post_{v}M_PREFAB");
        if (pillarMPrefab)
        {
            foreach (var oldPillar in toReplaceTPillarWithM)
            {
                if (!oldPillar) continue;

                Transform parent = oldPillar.transform.parent;
                int sibling = oldPillar.transform.GetSiblingIndex();
                int topIdx = GetTopIndexFromPillar(oldPillar);

                // V1M uses SnapPoint1 + SnapPoint2, same as V1T main axis
                var newPillar = InstantiateAndSwap(pillarMPrefab);
                TransferIndex(fenceRoot, oldPillar, newPillar);
                ApplyCurrentTextureVariantToObject(newPillar);
                Undo.RegisterCreatedObjectUndo(newPillar, "Replace V1T With V1M");

                if (parent)
                    newPillar.transform.SetParent(parent, true);
                newPillar.transform.SetSiblingIndex(sibling);

                newPillar.transform.SetPositionAndRotation(
                    oldPillar.transform.position,
                    oldPillar.transform.rotation
                );
                newPillar.transform.localScale = oldPillar.transform.localScale;
                if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
                {
                    var snapTop = FindSnap(newPillar.transform, TopSnapName);
                    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                    if (snapTop && topPrefab)
                    {
                        RemoveTopFromPillar(newPillar.transform);

                        var top = InstantiateAndSwap(topPrefab);
                        ApplyCurrentTextureVariantToObject(top);
                        Undo.RegisterCreatedObjectUndo(top, "Restore Top");

                        top.transform.SetParent(newPillar.transform, false);
                        top.transform.position = snapTop.position;
                        top.transform.rotation = snapTop.rotation;

                        ApplyTopVisualRotation(top);
                    }
                }

                // Remove old T-pillar
                TransferStartMarker(fenceRoot, oldPillar, newPillar);
                protectedPillarIds.Remove(oldPillar.StableId());
                Undo.DestroyObjectImmediate(oldPillar);
            }
        }
    }

    // --- REPLACE V1T/V2T pillars with V1C/V2C (corner case: branch rail kept) ---
    if (toReplaceTPillarWithC.Count > 0)
    {
        string v = VariantTagOf(DetectVariantFromFenceRoot(fenceRoot));
        var pillarCPrefab = FindAsset<GameObject>($"post_{v}C_PREFAB");
        if (pillarCPrefab)
        {
            foreach (var (oldPillar, rs0, rs1) in toReplaceTPillarWithC)
            {
                if (!oldPillar) continue;

                Transform parent = oldPillar.transform.parent;
                int sibling = oldPillar.transform.GetSiblingIndex();
                int topIdx = GetTopIndexFromPillar(oldPillar);

                var corner = InstantiateAndSwap(pillarCPrefab);
                TransferIndex(fenceRoot, oldPillar, corner);
                ApplyCurrentTextureVariantToObject(corner);
                Undo.RegisterCreatedObjectUndo(corner, "Replace V1T With V1C");

                if (parent)
                    corner.transform.SetParent(parent, true);
                corner.transform.SetSiblingIndex(sibling);
                corner.transform.localScale = oldPillar.transform.localScale;

                // Orient V1C to best match both connected rail snaps
                var cSnap1 = FindSnap(corner.transform, "SnapPoint1");
                var cSnap2 = FindSnap(corner.transform, "SnapPoint2");

                if (cSnap1 && cSnap2)
                {
                    Quaternion origRot = corner.transform.rotation;
                    Vector3 origPos = corner.transform.position;
                    float bestScore = float.MinValue;
                    Quaternion bestRot = origRot;
                    Vector3 bestPos = origPos;

                    Transform[] cArr = { cSnap1, cSnap2 };
                    Transform[] rArr = { rs0, rs1 };

                    // Try all 4 snap-to-rail configurations, pick best alignment
                    for (int ci = 0; ci < 2; ci++)
                    {
                        for (int ri = 0; ri < 2; ri++)
                        {
                            var cIn = cArr[ci];
                            var cOut = cArr[1 - ci];
                            var rIn = rArr[ri];
                            var rOut = rArr[1 - ri];

                            corner.transform.SetPositionAndRotation(origPos, origRot);
                            corner.transform.rotation =
                                YawDelta(cIn.right, -rIn.right) * corner.transform.rotation;
                            corner.transform.position += rIn.position - cIn.position;

                            float score = Vector3.Dot(cOut.right.normalized, -rOut.right.normalized);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestRot = corner.transform.rotation;
                                bestPos = corner.transform.position;
                            }
                        }
                    }

                    corner.transform.SetPositionAndRotation(bestPos, bestRot);
                }

                // Restore top
                if (topIdx >= 0 && topIdx < TopPrefabNames.Length)
                {
                    var snapTop = FindSnap(corner.transform, TopSnapName);
                    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIdx]);
                    if (snapTop && topPrefab)
                    {
                        RemoveTopFromPillar(corner.transform);
                        var top = InstantiateAndSwap(topPrefab);
                        ApplyCurrentTextureVariantToObject(top);
                        Undo.RegisterCreatedObjectUndo(top, "Restore Top");
                        top.transform.SetParent(corner.transform, false);
                        top.transform.position = snapTop.position;
                        top.transform.rotation = snapTop.rotation;
                        ApplyTopVisualRotation(top);
                    }
                }

                TransferStartMarker(fenceRoot, oldPillar, corner);
                protectedPillarIds.Remove(oldPillar.StableId());
                Undo.DestroyObjectImmediate(oldPillar);
            }
        }
    }

    // Check if fence is now empty (no rails left) -> delete root and remove from list
    bool hasRailsLeft = false;
    foreach (Transform t in fenceRoot.GetComponentsInChildren<Transform>(true))
    {
        if (!t) continue;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;
        
        string n = src.name;
        if ((n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
             n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) && n.EndsWith("_PREFAB"))
        {
            hasRailsLeft = true;
            break;
        }
    }
    
    if (!hasRailsLeft)
    {
        // Remove from tracking lists
        finalizedFences.Remove(fenceRoot);
        fenceStartPillars.Remove(fenceRoot);
        chainIndexByRoot.Remove(fenceRoot);
        deletedRailIdsByRoot.Remove(fenceRoot);
        
        // Delete the empty root
        Undo.DestroyObjectImmediate(fenceRoot);
    }

    // Rebuild chain index for this fence (topology changed)
    if (hasRailsLeft)
        RebuildChainIndexForRoot(fenceRoot);

    // Reapply Full Detail to newly created replacement pillars
    if (hasRailsLeft && fullDetailMode)
        ApplyFullDetailToFence(fenceRoot, true);

    // Rebuild protection cache
    RebuildProtectedPillarIdCache();
}

}
} // namespace WB3DAssets.FenceModularSystem
