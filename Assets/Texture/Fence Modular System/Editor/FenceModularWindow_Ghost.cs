using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{
void HideLastGhostPillarM()
{
    if (ghostPillarsM.Count == 0) return;

    var lastGhost = ghostPillarsM[^1];
    if (!lastGhost) return;

foreach (var rend in lastGhost.GetComponentsInChildren<Renderer>(true))
        rend.enabled = false;
}

void HideCurvedGhostEndPillar()
{
    if (!curvedGhostActive || !ghostCurvedPillar) return;

    foreach (var rend in ghostCurvedPillar.GetComponentsInChildren<Renderer>(true))
        rend.enabled = false;
}

void ShowCurvedGhostEndPillar()
{
    if (!hasCurvedRails) return;
    if (!ghostCurvedPillar) return;

    foreach (var rend in ghostCurvedPillar.GetComponentsInChildren<Renderer>(true))
        rend.enabled = true;
}

void ShowLastGhostPillarM()
{
    if (ghostPillarsM.Count == 0) return;

    var lastGhost = ghostPillarsM[^1];
    if (!lastGhost) return;

foreach (var rend in lastGhost.GetComponentsInChildren<Renderer>(true))
        rend.enabled = true;
}

void ClearCurvedGhost()
{
    // if ghost currently points to the curved root, detach first
    if (ghost == curvedGhostRoot)
        ghost = null;

    if (curvedGhostRoot)
        DestroyImmediate(curvedGhostRoot);

    curvedGhostRoot = null;
    ghostCurvedRail = null;
    ghostCurvedPillar = null;
    curvedGhostEndSnap = null;
}

void ClearHover90Preview()
{
// Do not disable continue ghost here; it's the base visual replacement in continue mode.

    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    ClearCloseLoopFull();

    if (hover90Root)
        DestroyImmediate(hover90Root);

    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;
    
    // Clear chain lists (objects are children of hover90Root, already destroyed)
    hoverChainRails.Clear();
    hoverChainPillarsM.Clear();
    hoverChainStartSnap = null;
}

void EnsureHoverChainSegs(int count)
{
    if (!hover90Root) return;
    
    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    
    if (!railPrefab || !pillarMPrefab) return;

    while (hoverChainRails.Count < count)
    {
        var newRail = CreateGhost(railPrefab, hover90Root.transform);
        if (continueFlipElements) FlipVisuals180(newRail);
        hoverChainRails.Add(newRail);
        var newPillar = CreateGhost(pillarMPrefab, hover90Root.transform);
        AddGhostTopToPillar(newPillar);
        hoverChainPillarsM.Add(newPillar);
    }

    while (hoverChainRails.Count > count)
    {
        DestroyImmediate(hoverChainRails[^1]);
        hoverChainRails.RemoveAt(hoverChainRails.Count - 1);

        DestroyImmediate(hoverChainPillarsM[^1]);
        hoverChainPillarsM.RemoveAt(hoverChainPillarsM.Count - 1);
    }
}

void LayoutHoverChain(Vector3 dir, Transform startSnap)
{
    if (hoverChainRails.Count == 0 || !startSnap) return;
    
    Transform target = startSnap;

    // Baseline Y from start snap (covers continue build, anchor pillar snap)
    Vector3 prevPos = startSnap.position;
    float prevY = prevPos.y;
    float maxTan = Mathf.Tan(3f * Mathf.Deg2Rad);

    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        var rail = hoverChainRails[i];
        var pillar = hoverChainPillarsM[i];

        var rs = FindSnap(rail.transform, RailStartSnap);
        var re = FindSnap(rail.transform, RailEndSnap);
        var ps = FindSnap(pillar.transform, PillarSnapName);
        var psNext = FindSnap(pillar.transform, "SnapPoint2");
        if (!rs || !re || !ps) return;

        // Rail to previous snap
        AlignRailToTarget(rail.transform, rs, -dir, target);

        // PillarM to rail end
        pillar.transform.rotation =
            YawDelta(ps.right, -dir) * pillar.transform.rotation;
        pillar.transform.position += re.position - ps.position;

        // Terrain Y-snap, clamped to ±3°. Skip entirely if ground is flat between
        // prev and current (no spurious Y shift on level terrain).
        // Going down: pillar+rail follow ground. Going up: pillar follows, rail stays.
        Vector3 pp = pillar.transform.position;
        float currGround = GroundYAt(pp);
        float prevGround = GroundYAt(prevPos);
        if (!float.IsNaN(currGround) && !float.IsNaN(prevGround))
        {
            float distH = Vector2.Distance(new Vector2(pp.x, pp.z),
                                           new Vector2(prevPos.x, prevPos.z));
            float groundDelta = currGround - prevGround;
            if (distH > 1e-4f && Mathf.Abs(groundDelta) > 1e-3f)
            {
                float tanA = Mathf.Clamp(groundDelta / distH, -maxTan, maxTan);
                float clampedY = prevY + tanA * distH;
                float dy = clampedY - pp.y;

                pillar.transform.position = new Vector3(pp.x, clampedY, pp.z);
                if (clampedY < pp.y)
                    rail.transform.position += new Vector3(0f, dy, 0f);
            }
        }

        prevPos = pillar.transform.position;
        prevY = prevPos.y;
        target = psNext;
    }
    
    // Update hover90Rail and hover90PillarM to point to the last elements
    if (hoverChainRails.Count > 0)
    {
        hover90Rail = hoverChainRails[^1];
        hover90PillarM = hoverChainPillarsM[^1];
    }
}

// Adjust last hover chain rail Y for close-loop bridge (call AFTER UpdateCloseLoopDetection)
void AdjustCloseLoopBridge()
{
    if (!closeLoopDetected || hoverChainRails.Count == 0 || !closeLoopTargetPillar) return;
    if (!continueAnchorPillar) return;

    var lastRail = hoverChainRails[^1];
    var lastHoverPillar = hoverChainPillarsM[^1];
    if (!lastRail) return;

    // Loop closing DOWN (target lower than continue start) → rail Y = target pillar Y
    // Loop closing UP   (target higher) → rail Y = continue anchor Y
    float targetY = closeLoopTargetPillar.transform.position.y;
    float anchorY = continueAnchorPillar.transform.position.y;
    bool loopDown = targetY < anchorY;
    float refY = loopDown ? targetY : anchorY;

    var rp = lastRail.transform.position;
    lastRail.transform.position = new Vector3(rp.x, refY, rp.z);

    // Only on DOWN loop: last hover pillar takes target pillar Y
    if (loopDown && lastHoverPillar)
    {
        var pp = lastHoverPillar.transform.position;
        lastHoverPillar.transform.position = new Vector3(pp.x, targetY, pp.z);
    }
}

GameObject CommitHoverChainOnly()
{
    if (hoverChainRails.Count == 0)
    {
        ApplyFullDetailToCurrentBuild();
        return null;
    }

    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!railPrefab || !pillarMPrefab)
        return null;

    GameObject lastCommittedPillarM = null;

    // Commit Corner pillar (V1C/V1C45) if present (for 90°/45° turns)
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);

                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation
                );
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    // Commit the entire chain (Rails + PillarsM)
    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit Rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);

        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation
        );
        realRail.transform.localScale = continueScale;
        if (continueFlipElements) FlipVisuals180(realRail);

        // Commit PillarM
        var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        ApplyContinueTopToPillar(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);

        realPillar.transform.SetPositionAndRotation(
            hoverChainPillarsM[i].transform.position,
            hoverChainPillarsM[i].transform.rotation
        );
        realPillar.transform.localScale = continueScale;

        lastCommittedPillarM = realPillar;
    }

    ApplyFullDetailToCurrentBuild();
    return lastCommittedPillarM;
}

void CommitHover90PreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root)
        return;

    // Get prefabs
    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!railPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // 1) FINALIZE: Convert hover90 ghost to REAL objects
    
    // If Corner pillar (V1C/V1C45) is present, remove previous V1M (it gets replaced)
    if (hover90EndPillarE && continueAnchorPillar)
    {
        // Temporarily disable delete protection
        bool prevSuppress = suppressDeleteUndo;
        suppressDeleteUndo = true;

        currentBuildObjects.Remove(continueAnchorPillar);
        protectedPillarIds.Remove(continueAnchorPillar.StableId());
        Undo.DestroyObjectImmediate(continueAnchorPillar);
        continueAnchorPillar = null;

        // Re-enable delete protection
        suppressDeleteUndo = prevSuppress;
        RebuildProtectedPillarIdCache();
    }
    // Straight: Show previous V1M again (was hidden in continue mode)
    else if (continueAnchorPillar)
    {
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    }

    // Commit Corner pillar (V1C/V1C45) if present (for 90°/45° turns)
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);

                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation
                );
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    // Commit the entire chain (Rails + PillarsM)
    GameObject lastCommittedPillarM = null;
    
    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit Rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);

        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation
        );
        realRail.transform.localScale = continueScale;
        if (continueFlipElements) FlipVisuals180(realRail);

        // Commit PillarM
        var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
        ApplyCurrentTextureVariantToObject(realPillar);
        ApplyContinueTopToPillar(realPillar);
        Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
        currentBuildObjects.Add(realPillar);
        ApplyPillarMVisualVariation(realPillar);

        realPillar.transform.SetPositionAndRotation(
            hoverChainPillarsM[i].transform.position,
            hoverChainPillarsM[i].transform.rotation
        );
        realPillar.transform.localScale = continueScale;
        
        lastCommittedPillarM = realPillar;
        lastPlacedPillarM = realPillar;
    }

    // Clear the hover preview (destroy ghost objects)
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;
    hoverChainRails.Clear();
    hoverChainPillarsM.Clear();
    hoverChainStartSnap = null;

    // 2) Set up Continue Build Mode (same pattern as RailPreview -> CornerSelect)
    if (lastCommittedPillarM)
    {
        continueAnchorPillar = lastCommittedPillarM;
        continueAnchorActive = true;

        // Hide the anchor pillar
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        // Ensure ghost material exists
        if (!ghostMat)
            ghostMat = FindAsset<Material>(GhostMatName);

        // Find the connected snap (snap1 is connected to the rail)
        var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

        // Setup snap proxy
        if (continueSnapProxy)
            DestroyImmediate(continueSnapProxy.gameObject);

        var proxyGO = new GameObject("ContinueSnapProxy");
        proxyGO.hideFlags = HideFlags.HideAndDontSave;
        continueSnapProxy = proxyGO.transform;
        continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

        lastPillarSnap = continueSnapProxy;
        
        // Set activeDir to the new direction
        activeDir = newActiveDir.normalized;
        activeDir.y = 0f;
        if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

        // Create Ghost V1M
        if (continueGhostPillarM)
            DestroyImmediate(continueGhostPillarM);

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

            AddGhostTopToPillar(continueGhostPillarM);
        }
    }

    ApplyFullDetailToCurrentBuild();
}

void CommitHover90CurvedPreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM)
        return;

    // Get prefabs
    var curvedPrefab = FindAsset<GameObject>(
        $"sectionBCrvd_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!curvedPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // Show the previous anchor pillar again (was hidden in continue mode)
    if (continueAnchorPillar)
    {
        foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    }

    // 1) FINALIZE: Commit Curved Rail
    var realCurvedRail = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCurvedRail);
    Undo.RegisterCreatedObjectUndo(realCurvedRail, "Place Curved Rail");
    currentBuildObjects.Add(realCurvedRail);
    ApplyRailVisualVariation(realCurvedRail);
    ApplyCurvedRailVisualVariation(realCurvedRail);

    realCurvedRail.transform.SetPositionAndRotation(
        hover90Rail.transform.position,
        hover90Rail.transform.rotation
    );
    realCurvedRail.transform.localScale = continueScale;
    if (continueFlipElements) FlipVisuals180(realCurvedRail);

    // 2) FINALIZE: Commit PillarM
    var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
    ApplyCurrentTextureVariantToObject(realPillar);
    ApplyContinueTopToPillar(realPillar);
    Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
    currentBuildObjects.Add(realPillar);
    ApplyPillarMVisualVariation(realPillar);

    realPillar.transform.SetPositionAndRotation(
        hover90PillarM.transform.position,
        hover90PillarM.transform.rotation
    );
    realPillar.transform.localScale = continueScale;

    lastPlacedPillarM = realPillar;

    // Clear the hover preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;

    // 3) Set up Continue Build Mode
    continueAnchorPillar = realPillar;
    continueAnchorActive = true;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;

    if (!ghostMat)
        ghostMat = FindAsset<Material>(GhostMatName);

    var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

    lastPillarSnap = continueSnapProxy;

    activeDir = newActiveDir.normalized;
    activeDir.y = 0f;
    if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

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

    ApplyFullDetailToCurrentBuild();
}

void CommitHover90OuterArcPreviewAndEnterContinueMode(Vector3 newActiveDir)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM || !hover90EndPillarE)
        return;

    // Get prefabs
    var curvedPrefab = FindAsset<GameObject>(
        $"sectionBCrvd_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));

    if (!curvedPrefab || !pillarMPrefab)
    {
        ClearHover90Preview();
        return;
    }

    // OUTER ARC: Remove previous V1M (replaced by V1C)
    // Temporarily disable delete protection
    bool prevSuppress = suppressDeleteUndo;
    suppressDeleteUndo = true;

    // --- CONTINUE ANCHOR CLEANUP ---
    // When outer arc is the FIRST commit from Continue Anchor mode,
    // continueAnchorPillar (hidden V1E) still exists and must be removed.
    // The V1C corner replaces it in the chain.
    if (continueAnchorActive)
    {
        if (continueGhostPillarM)
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
    }

    if (lastPlacedPillarM)
    {
        currentBuildObjects.Remove(lastPlacedPillarM);
        Undo.DestroyObjectImmediate(lastPlacedPillarM);
        lastPlacedPillarM = null;
    }

    // Re-enable delete protection
    suppressDeleteUndo = prevSuppress;
    RebuildProtectedPillarIdCache();

    // 1) FINALIZE: Commit Corner Pillar (V1C)
    var cornerSrc = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
    if (cornerSrc != null)
    {
        var cornerPrefab = FindAsset<GameObject>(cornerSrc.name);
        if (cornerPrefab)
        {
            var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
            ApplyCurrentTextureVariantToObject(realCorner);
            ApplyContinueTopToPillar(realCorner);
            Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar V1C");
            currentBuildObjects.Add(realCorner);

            realCorner.transform.SetPositionAndRotation(
                hover90EndPillarE.transform.position,
                hover90EndPillarE.transform.rotation
            );
            realCorner.transform.localScale = continueScale;
        }
    }

    // 2) FINALIZE: Commit Curved Rail
    var realCurvedRail = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCurvedRail);
    Undo.RegisterCreatedObjectUndo(realCurvedRail, "Place Curved Rail");
    currentBuildObjects.Add(realCurvedRail);
    ApplyRailVisualVariation(realCurvedRail);
    ApplyCurvedRailVisualVariation(realCurvedRail);

    realCurvedRail.transform.SetPositionAndRotation(
        hover90Rail.transform.position,
        hover90Rail.transform.rotation
    );
    realCurvedRail.transform.localScale = continueScale;
    if (continueFlipElements) FlipVisuals180(realCurvedRail);

    // 3) FINALIZE: Commit PillarM
    var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
    ApplyCurrentTextureVariantToObject(realPillar);
    ApplyContinueTopToPillar(realPillar);
    Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
    currentBuildObjects.Add(realPillar);
    ApplyPillarMVisualVariation(realPillar);

    realPillar.transform.SetPositionAndRotation(
        hover90PillarM.transform.position,
        hover90PillarM.transform.rotation
    );
    realPillar.transform.localScale = continueScale;

    lastPlacedPillarM = realPillar;

    // Clear the hover preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root)
        DestroyImmediate(hover90Root);
    hover90Root = null;
    hover90EndPillarE = null;
    hover90Rail = null;
    hover90PillarM = null;

    // 4) Set up Continue Build Mode
    continueAnchorPillar = realPillar;
    continueAnchorActive = true;

    foreach (var r in continueAnchorPillar.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;

    if (!ghostMat)
        ghostMat = FindAsset<Material>(GhostMatName);

    var snap1 = FindSnap(continueAnchorPillar.transform, "SnapPoint1");

    if (continueSnapProxy)
        DestroyImmediate(continueSnapProxy.gameObject);

    var proxyGO = new GameObject("ContinueSnapProxy");
    proxyGO.hideFlags = HideFlags.HideAndDontSave;
    continueSnapProxy = proxyGO.transform;
    continueSnapProxy.SetPositionAndRotation(snap1.position, snap1.rotation);

    lastPillarSnap = continueSnapProxy;

    activeDir = newActiveDir.normalized;
    activeDir.y = 0f;
    if (activeDir.sqrMagnitude > 1e-6f) activeDir.Normalize();

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

    ApplyFullDetailToCurrentBuild();
}

GameObject CreateGhost(GameObject prefab, Transform parent = null)
{
    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
    go.hideFlags = HideFlags.HideAndDontSave;

    // Inherit scale from target fence in continue mode
    if (continueScale != Vector3.one)
        go.transform.localScale = continueScale;

    if (parent)
        go.transform.SetParent(parent, true);

    // Remove colliders
    foreach (var c in go.GetComponentsInChildren<Collider>(true))
        DestroyImmediate(c);

    // Force LOD0 only: remove ALL LODGroups recursively
    foreach (var lg in go.GetComponentsInChildren<LODGroup>(true))
        DestroyImmediate(lg);

    // Destroy all LOD1+ meshes, keep only LOD0
    var lodChildren = new List<GameObject>();
    foreach (var t in go.GetComponentsInChildren<Transform>(true))
    {
        if (t == go.transform) continue;
        string n = t.name;
        if (n.Contains("LOD") && !n.Contains("LOD0"))
            lodChildren.Add(t.gameObject);
    }
    foreach (var lod in lodChildren)
        DestroyImmediate(lod);

    // Force ghost material ONLY
    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            mats[i] = ghostMat;
        r.sharedMaterials = mats;
    }

    return go;
}

void AddGhostTopToPillar(GameObject ghostPillar)
{
    if (!ghostPillar)
        return;

    // Only add tops if continueTopIndex is valid
    if (continueTopIndex < 0 || continueTopIndex >= TopPrefabNames.Length)
        return;

    var snapTop = FindSnap(ghostPillar.transform, TopSnapName);
    if (!snapTop)
        return;

    var topPrefab = FindAsset<GameObject>(TopPrefabNames[continueTopIndex]);
    if (!topPrefab)
        return;

    // Create ghost top
    var ghostTop = (GameObject)PrefabUtility.InstantiatePrefab(topPrefab);
    ghostTop.hideFlags = HideFlags.HideAndDontSave;

    // Remove colliders
    foreach (var c in ghostTop.GetComponentsInChildren<Collider>(true))
        DestroyImmediate(c);

    // Force LOD0 only
    var topLodGroup = ghostTop.GetComponent<LODGroup>();
    if (topLodGroup) DestroyImmediate(topLodGroup);
    var topLodChildren = new List<GameObject>();
    foreach (var t in ghostTop.GetComponentsInChildren<Transform>(true))
    {
        if (t == ghostTop.transform) continue;
        string n = t.name;
        if (n.StartsWith("LOD") && n != "LOD0")
            topLodChildren.Add(t.gameObject);
    }
    foreach (var lod in topLodChildren)
        DestroyImmediate(lod);

    // Apply ghost material
    foreach (var r in ghostTop.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            mats[i] = ghostMat;
        r.sharedMaterials = mats;
    }

    // Parent to pillar and position at SnapPointTop
    ghostTop.transform.SetParent(ghostPillar.transform, false);
    ghostTop.transform.position = snapTop.position;
    ghostTop.transform.rotation = snapTop.rotation;
}

    static void ApplyGhostTint(GameObject ghostObj, Color color)
    {
        if (!ghostObj) return;

        // Lazy-init: MaterialPropertyBlock cannot be created in static field initializers
        if (ghostPropBlock == null) ghostPropBlock = new MaterialPropertyBlock();

        // Set all color properties to cover HDRP, URP, and Built-in shaders
        ghostPropBlock.SetColor(PropBaseColor, color);   // HDRP Lit + URP Lit/Unlit
        ghostPropBlock.SetColor(PropUnlitColor, color);  // HDRP Unlit
        ghostPropBlock.SetColor(PropColor, color);       // Built-in fallback

        foreach (var r in ghostObj.GetComponentsInChildren<Renderer>(true))
            r.SetPropertyBlock(ghostPropBlock);
    }

    static void ClearGhostTint(GameObject ghostObj)
    {
        if (!ghostObj) return;

        foreach (var r in ghostObj.GetComponentsInChildren<Renderer>(true))
            r.SetPropertyBlock(null);
    }

// -- Close Loop Detection & Try-Fit --

void UpdateCloseLoopDetection()
{
    GameObject lastGhost = null;
    if (state == State.DirectionSelect && dirSelectHoverPillars.Count > 0)
        lastGhost = dirSelectHoverPillars[^1];
    else if (hoverChainPillarsM.Count > 0)
        lastGhost = hoverChainPillarsM[^1];
    else if (hover90PillarM)
        lastGhost = hover90PillarM;

    if (!lastGhost) { if (closeLoopDetected) ClearCloseLoopFull(); return; }

    // Ghost's chain-side snap (Snap1) = where it connects to the chain
    var ghostSnap1 = FindSnap(lastGhost.transform, PillarSnapName);
    // Ghost's free-side snap (Snap2) = the end approaching the target
    var ghostSnap2 = FindSnap(lastGhost.transform, "SnapPoint2");
    if (!ghostSnap1 || !ghostSnap2) { if (closeLoopDetected) ClearCloseLoopFull(); return; }

    Vector3 freePos = ghostSnap2.position;

    // Find nearest real pillar within proximity of the ghost's free end
    GameObject found = null;
    float bestDist = float.MaxValue;
    string[] pillarSnaps = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };

    System.Action<GameObject> testCandidate = (go) =>
    {
        if (!go || go == continueAnchorPillar || go == lastPlacedPillarM) return;
        if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!src || !src.name.StartsWith("post_") || !src.name.EndsWith("_PREFAB")) return;
        if (src.name.Contains("T_PREFAB")) return; // V1T/V2T fully occupied
        if (src.name.Contains("C45_PREFAB")) return; // V1C45 geometry incompatible with close-loop
        foreach (var sn in pillarSnaps)
        {
            var snap = FindSnap(go.transform, sn);
            if (!snap) continue;
            float d = Vector3.Distance(snap.position, freePos);
            float thresh = 0.8f * Mathf.Max(continueScale.x, 1f);
            if (d < thresh && d < bestDist) { bestDist = d; found = go; }
        }
    };

    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        foreach (Transform child in root.transform)
            testCandidate(child ? child.gameObject : null);
    }
    foreach (var go in currentBuildObjects)
        testCandidate(go);

    if (found != null)
    {
        // Collect all real rail snap positions
        var allRailSnaps = CollectAllRailSnapPositions();

        // Try-fit: place each candidate at the ghost's position, check free snap vs real rails
        char repType = TryFitCloseLoop(ghostSnap1, allRailSnaps, found);

        if (repType == '\0')
        {
            if (closeLoopDetected) ClearCloseLoopFull();
        }
        else if (!closeLoopDetected || closeLoopTargetPillar != found || closeLoopReplacementType != repType)
        {
            ClearCloseLoop();
            closeLoopTargetPillar = found;
            closeLoopDetected = true;
            closeLoopReplacementType = repType;
            closeLoopApproachDir = ghostSnap1.right;
            closeLoopApproachDir.y = 0f;
            if (closeLoopApproachDir.sqrMagnitude > 1e-6f) closeLoopApproachDir.Normalize();

            foreach (var r in found.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            TintAllActiveGhosts(GhostCloseLoopCol);
            UpdateCloseLoopGhostSwap(lastGhost, repType);
        }
        else
        {
            TintAllActiveGhosts(GhostCloseLoopCol);
            UpdateCloseLoopGhostSwap(lastGhost, repType);
        }
    }
    else if (closeLoopDetected)
    {
        ClearCloseLoopFull();
    }
}

List<Vector3> CollectAllRailSnapPositions()
{
    var positions = new List<Vector3>(64);
    System.Action<GameObject> collect = (go) =>
    {
        if (!go || (go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        var s = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!s) return;
        string n = s.name;
        if (!(n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
              n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) || !n.EndsWith("_PREFAB")) return;
        var rs = FindSnap(go.transform, RailStartSnap);
        var re = FindSnap(go.transform, RailEndSnap);
        if (rs) positions.Add(rs.position);
        if (re) positions.Add(re.position);
    };
    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        foreach (Transform child in root.transform)
            if (child) collect(child.gameObject);
    }
    foreach (var go in currentBuildObjects)
        collect(go);
    return positions;
}

// Place each candidate type at the ghost's position (chain-aligned via ghostSnap1),
// then check if any FREE snap touches a real rail snap.
char TryFitCloseLoop(Transform ghostSnap1, List<Vector3> allRailSnaps, GameObject targetPillar)
{
    Vector3 chainDir = ghostSnap1.right; chainDir.y = 0f;
    if (chainDir.sqrMagnitude < 1e-6f) return '\0';
    chainDir.Normalize();
    Vector3 chainPos = ghostSnap1.position;

    // Count occupied snaps on target to determine valid candidates
    var occupiedDirs = new List<Vector3>(4);
    GatherOccupiedSnapDirs(targetPillar, occupiedDirs);
    int occupiedCount = occupiedDirs.Count;

    // 1 occupied + 1 new = 2 total → M/C/C45
    // 2 occupied + 1 new = 3 total → T only
    char[] candidates;
    if (occupiedCount <= 1) candidates = new[] { 'M', 'C', '4' };
    else if (occupiedCount == 2) candidates = new[] { 'T' };
    else return '\0';

    // If 1 occupied snap exists, determine type by angle (no snap-match needed)
    // This is robust across fence types with different snap offsets.
    if (occupiedCount == 1)
    {
        float dot = Vector3.Dot(chainDir, occupiedDirs[0]);
        char result;
        if (dot > -0.7f) result = 'C'; // angle < ~135° = corner
        else              result = 'M'; // ~180° = straight

        // Compute fit position/rotation for the chosen type
        var prefab = FindAsset<GameObject>(GetPillarPrefabName(result));
        if (!prefab) return '\0';
        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.localScale = continueScale;

        if (result == 'C')
        {
            // Dual-orientation test like build system (ScoreCornerCandidate)
            var sa = FindSnap(temp.transform, "SnapPoint1");
            var sb = FindSnap(temp.transform, "SnapPoint2");
            if (sa && sb)
            {
                Quaternion origRot = temp.transform.rotation;
                Vector3 origPos = temp.transform.position;

                // Option A: SnapPoint1 = input
                Vector3 da = sa.right; da.y = 0f; da.Normalize();
                temp.transform.rotation = YawDelta(da, chainDir) * origRot;
                temp.transform.position = chainPos - sa.position;
                Vector3 outA = sb.right; outA.y = 0f;
                Quaternion rotA = temp.transform.rotation;
                Vector3 posA = temp.transform.position;

                temp.transform.SetPositionAndRotation(origPos, origRot);

                // Option B: SnapPoint2 = input
                Vector3 db = sb.right; db.y = 0f; db.Normalize();
                temp.transform.rotation = YawDelta(db, chainDir) * origRot;
                temp.transform.position = chainPos - sb.position;
                Vector3 outB = sa.right; outB.y = 0f;
                Quaternion rotB = temp.transform.rotation;
                Vector3 posB = temp.transform.position;

                // Pick orientation whose output best matches existing rail direction
                if (Vector3.Dot(outA.normalized, occupiedDirs[0]) >= Vector3.Dot(outB.normalized, occupiedDirs[0]))
                { closeLoopFitRotation = rotA; closeLoopFitPosition = posA; }
                else
                { closeLoopFitRotation = rotB; closeLoopFitPosition = posB; }
            }
        }
        else
        {
            var s1 = FindSnap(temp.transform, "SnapPoint1");
            if (s1)
            {
                Vector3 sd = s1.right; sd.y = 0f; sd.Normalize();
                temp.transform.rotation = YawDelta(sd, chainDir) * temp.transform.rotation;
                temp.transform.position = chainPos - s1.position;
                closeLoopFitRotation = temp.transform.rotation;
                closeLoopFitPosition = temp.transform.position;
            }
        }
        // Snap Y to target pillar height (close loop pillar takes target's elevation)
        if (targetPillar)
            closeLoopFitPosition.y = targetPillar.transform.position.y;
        DestroyImmediate(temp);
        return result;
    }

    // If 0 occupied snaps, default to M (straight)
    if (occupiedCount == 0)
    {
        var prefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
        if (!prefab) return '\0';
        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.localScale = continueScale;
        var s1 = FindSnap(temp.transform, "SnapPoint1");
        if (s1)
        {
            Vector3 sd = s1.right; sd.y = 0f; sd.Normalize();
            temp.transform.rotation = YawDelta(sd, chainDir) * temp.transform.rotation;
            temp.transform.position = chainPos - s1.position;
            closeLoopFitRotation = temp.transform.rotation;
            closeLoopFitPosition = temp.transform.position;
        }
        DestroyImmediate(temp);
        return 'M';
    }

    // Include target pillar snap positions in match set
    var matchPositions = new List<Vector3>(allRailSnaps);
    foreach (var sn in new[] { "SnapPoint1", "SnapPoint2" })
    {
        var s = FindSnap(targetPillar.transform, sn);
        if (s) matchPositions.Add(s.position);
    }

    float matchTol = 0.25f * Mathf.Max(continueScale.x, 1f);

    char bestType = '\0';
    int bestScore = 0;

    foreach (char type in candidates)
    {
        var prefab = FindAsset<GameObject>(GetPillarPrefabName(type));
        if (!prefab) continue;

        var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.hideFlags = HideFlags.HideAndDontSave;
        temp.transform.localScale = continueScale;
        Quaternion origRot = temp.transform.rotation;

        string[] sNames = type == 'T'
            ? new[] { "SnapPoint1", "SnapPoint2", "SnapPoint3" }
            : new[] { "SnapPoint1", "SnapPoint2" };

        var snaps = new List<Transform>(4);
        foreach (var sn in sNames)
        {
            var s = FindSnap(temp.transform, sn);
            if (s) snaps.Add(s);
        }

        int typeScore = 0;
        Quaternion bestRot = origRot;
        Vector3 bestPos = Vector3.zero;

        for (int si = 0; si < snaps.Count; si++)
        {
            temp.transform.SetPositionAndRotation(Vector3.zero, origRot);
            Vector3 sd = snaps[si].right; sd.y = 0f;
            if (sd.sqrMagnitude < 1e-6f) continue;
            sd.Normalize();
            temp.transform.rotation = YawDelta(sd, chainDir) * origRot;
            temp.transform.position = chainPos - snaps[si].position;

            int matchCount = 0;
            for (int ci = 0; ci < snaps.Count; ci++)
            {
                if (ci == si) continue;
                foreach (var rp in matchPositions)
                {
                    if (Vector3.Distance(snaps[ci].position, rp) < matchTol)
                    { matchCount++; break; }
                }
            }

            if (matchCount > typeScore)
            {
                typeScore = matchCount;
                bestRot = temp.transform.rotation;
                bestPos = temp.transform.position;
            }
        }

        DestroyImmediate(temp);

        if (typeScore > bestScore)
        {
            bestScore = typeScore;
            bestType = type;
            closeLoopFitRotation = bestRot;
            closeLoopFitPosition = bestPos;
        }
    }

    return bestScore > 0 ? bestType : '\0';
}

// Collect world-space direction vectors for each occupied snap on a pillar.
// Only counts real rail connections (skips ghost objects).
void GatherOccupiedSnapDirs(GameObject pillar, List<Vector3> outDirs)
{
    // Collect real rail snap positions first
    var railSnapPositions = new List<Vector3>(64);

    System.Action<GameObject> collectRailSnaps = (go) =>
    {
        if (!go || (go.hideFlags & HideFlags.HideAndDontSave) != 0) return;
        var s = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (!s) return;
        string n = s.name;
        bool isRail = (n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_") ||
                       n.StartsWith("single_gate_") || n.StartsWith("double_gate_")) && n.EndsWith("_PREFAB");
        if (!isRail) return;

        var rs1 = FindSnap(go.transform, RailStartSnap);
        var rs2 = FindSnap(go.transform, RailEndSnap);
        if (rs1) railSnapPositions.Add(rs1.position);
        if (rs2) railSnapPositions.Add(rs2.position);
    };

    foreach (var root in finalizedFences)
    {
        if (!root) continue;
        foreach (Transform child in root.transform)
            if (child) collectRailSnaps(child.gameObject);
    }
    foreach (var go in currentBuildObjects)
        collectRailSnaps(go);

    // Check each pillar snap against rail positions
    string[] snapNames = { "SnapPoint1", "SnapPoint2", "SnapPoint3" };
    float threshold = 0.15f * Mathf.Max(continueScale.x, 1f);

    foreach (var sn in snapNames)
    {
        var snap = FindSnap(pillar.transform, sn);
        if (!snap) continue;

        bool occupied = false;
        foreach (var rp in railSnapPositions)
        {
            if (Vector3.Distance(rp, snap.position) < threshold)
            {
                occupied = true;
                break;
            }
        }

        if (occupied)
        {
            Vector3 dir = snap.right;
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-6f)
                outDirs.Add(dir.normalized);
        }
    }
}

void TintAllActiveGhosts(Color col)
{
    // Hover chains (CornerSelect)
    foreach (var go in hoverChainRails) ApplyGhostTint(go, col);
    foreach (var go in hoverChainPillarsM) ApplyGhostTint(go, col);
    if (hover90EndPillarE) ApplyGhostTint(hover90EndPillarE, col);
    if (hover90Rail && !hoverChainRails.Contains(hover90Rail)) ApplyGhostTint(hover90Rail, col);
    if (hover90PillarM && !hoverChainPillarsM.Contains(hover90PillarM)) ApplyGhostTint(hover90PillarM, col);

    // Direction select chains
    foreach (var go in dirSelectHoverRails) ApplyGhostTint(go, col);
    foreach (var go in dirSelectHoverPillars) ApplyGhostTint(go, col);

    // Continue ghost
    if (continueGhostPillarM) ApplyGhostTint(continueGhostPillarM, col);

    // Close loop replacement ghost
    if (closeLoopReplacementGhost) ApplyGhostTint(closeLoopReplacementGhost, col);
}

void ClearTintAllActiveGhosts()
{
    foreach (var go in hoverChainRails) ClearGhostTint(go);
    foreach (var go in hoverChainPillarsM) ClearGhostTint(go);
    if (hover90EndPillarE) ClearGhostTint(hover90EndPillarE);
    if (hover90Rail) ClearGhostTint(hover90Rail);
    if (hover90PillarM) ClearGhostTint(hover90PillarM);
    foreach (var go in dirSelectHoverRails) ClearGhostTint(go);
    foreach (var go in dirSelectHoverPillars) ClearGhostTint(go);
    if (continueGhostPillarM) ClearGhostTint(continueGhostPillarM);
    if (closeLoopReplacementGhost) ClearGhostTint(closeLoopReplacementGhost);
}

// Commit the ghost hover chain for a close-loop finalize.
// All intermediate pillars = V1M, last pillar = replacement type.
// Deletes the hidden target pillar and clears close-loop state.
GameObject CommitCloseLoopChain()
{
    if (!closeLoopDetected || closeLoopReplacementType == '\0')
        return null;

    var railPrefab = FindAsset<GameObject>(
        $"sectionB_{VariantTag}_PREFAB"
    );
    var pillarMPrefab = FindAsset<GameObject>(GetPillarPrefabName('M'));
    var repPrefab = FindAsset<GameObject>(GetPillarPrefabName(closeLoopReplacementType));
    if (!railPrefab || !pillarMPrefab || !repPrefab) return null;

    // Commit corner pillar if present
    if (hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src != null)
        {
            var cornerPrefab = FindAsset<GameObject>(src.name);
            if (cornerPrefab)
            {
                var realCorner = (GameObject)PrefabUtility.InstantiatePrefab(cornerPrefab);
                ApplyCurrentTextureVariantToObject(realCorner);
                ApplyContinueTopToPillar(realCorner);
                Undo.RegisterCreatedObjectUndo(realCorner, "Place Corner Pillar");
                currentBuildObjects.Add(realCorner);
                realCorner.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation);
                realCorner.transform.localScale = continueScale;
            }
        }
    }

    GameObject lastPillar = null;
    int lastIdx = hoverChainRails.Count - 1;

    for (int i = 0; i < hoverChainRails.Count; i++)
    {
        // Commit rail
        var realRail = (GameObject)PrefabUtility.InstantiatePrefab(railPrefab);
        ApplyCurrentTextureVariantToObject(realRail);
        Undo.RegisterCreatedObjectUndo(realRail, "Place Rail");
        currentBuildObjects.Add(realRail);
        ApplyRailVisualVariation(realRail);
        realRail.transform.SetPositionAndRotation(
            hoverChainRails[i].transform.position,
            hoverChainRails[i].transform.rotation);
        realRail.transform.localScale = continueScale;
        if (continueFlipElements) FlipVisuals180(realRail);

        if (i < lastIdx)
        {
            // Intermediate pillar → V1M
            var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(pillarMPrefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Pillar V1M");
            currentBuildObjects.Add(realPillar);
            ApplyPillarMVisualVariation(realPillar);
            realPillar.transform.SetPositionAndRotation(
                hoverChainPillarsM[i].transform.position,
                hoverChainPillarsM[i].transform.rotation);
            realPillar.transform.localScale = continueScale;
        }
        else
        {
            // Last pillar → replacement type at replacement ghost pos/rot
            var realPillar = (GameObject)PrefabUtility.InstantiatePrefab(repPrefab);
            ApplyCurrentTextureVariantToObject(realPillar);
            ApplyContinueTopToPillar(realPillar);
            Undo.RegisterCreatedObjectUndo(realPillar, "Place Close Loop Pillar");
            currentBuildObjects.Add(realPillar);

            if (closeLoopReplacementGhost)
            {
                realPillar.transform.SetPositionAndRotation(
                    closeLoopReplacementGhost.transform.position,
                    closeLoopReplacementGhost.transform.rotation);
            }
            else
            {
                realPillar.transform.SetPositionAndRotation(
                    hoverChainPillarsM[i].transform.position,
                    hoverChainPillarsM[i].transform.rotation);
            }
            realPillar.transform.localScale = continueScale;

            lastPillar = realPillar;
        }
    }

    // Permanently delete the hidden target pillar
    if (closeLoopTargetPillar)
    {
        bool prevSuppress = suppressDeleteUndo;
        suppressDeleteUndo = true;
        currentBuildObjects.Remove(closeLoopTargetPillar);
        Undo.DestroyObjectImmediate(closeLoopTargetPillar);
        suppressDeleteUndo = prevSuppress;
    }

    // Clear close-loop state (no re-show, target is destroyed)
    ClearCloseLoopGhostSwap();
    closeLoopTargetPillar = null;
    closeLoopDetected = false;
    closeLoopReplacementType = '\0';

    ApplyFullDetailToCurrentBuild();
    return lastPillar;
}

void ClearCloseLoop()
{
    ClearCloseLoopGhostSwap();
    if (closeLoopTargetPillar)
        foreach (var r in closeLoopTargetPillar.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    closeLoopTargetPillar = null;
    closeLoopDetected = false;
    closeLoopReplacementType = '\0';
}

void ClearCloseLoopFull()
{
    ClearCloseLoop();
    ClearTintAllActiveGhosts();
}

// Swap the last ghost pillar to visually match the replacement type.
// V1M stays as-is (already the default ghost). Other types get a visual override.
void UpdateCloseLoopGhostSwap(GameObject lastGhost, char repType)
{
    if (repType == '\0' || repType == 'M')
    {
        ClearCloseLoopGhostSwap();
        return;
    }

    if (closeLoopReplacementGhost && closeLoopOriginalGhost == lastGhost)
    {
        ApplyGhostTint(closeLoopReplacementGhost, GhostCloseLoopCol);
        return;
    }

    ClearCloseLoopGhostSwap();

    var prefab = FindAsset<GameObject>(GetPillarPrefabName(repType));
    if (!prefab || !closeLoopTargetPillar) return;

    closeLoopReplacementGhost = CreateGhost(prefab);
    closeLoopReplacementGhost.name = "CloseLoopSwapGhost";
    AddGhostTopToPillar(closeLoopReplacementGhost);

    // Use the exact rotation+position found by TryFitCloseLoop
    closeLoopReplacementGhost.transform.SetPositionAndRotation(closeLoopFitPosition, closeLoopFitRotation);

    ApplyGhostTint(closeLoopReplacementGhost, GhostCloseLoopCol);

    closeLoopOriginalGhost = lastGhost;
    foreach (var r in lastGhost.GetComponentsInChildren<Renderer>(true))
        r.enabled = false;
}

void ClearCloseLoopGhostSwap()
{
    if (closeLoopReplacementGhost)
        DestroyImmediate(closeLoopReplacementGhost);
    closeLoopReplacementGhost = null;

    // Restore original ghost visibility
    if (closeLoopOriginalGhost)
        foreach (var r in closeLoopOriginalGhost.GetComponentsInChildren<Renderer>(true))
            r.enabled = true;
    closeLoopOriginalGhost = null;
}

// Commit arc preview objects + close-loop chain in one transaction.
// isOuterArc: true for outer arc (has V1C corner), false for inner arc.
void CommitArcAndCloseLoop(bool isOuterArc)
{
    if (!hover90Root || !hover90Rail || !hover90PillarM) return;

    var curvedPrefab = FindAsset<GameObject>(
        $"sectionBCrvd_{VariantTag}_PREFAB");
    if (!curvedPrefab) { ClearHover90Preview(); return; }

    // --- 1) Commit straight rail segments, skip last V1M (close-loop handles junction) ---
    CommitRailSegs(null, skipLastPillar: true);

    // --- 2) Outer arc: remove pre-existing lastPlacedPillarM (V1C corner replaces it) ---
    bool ps = suppressDeleteUndo; suppressDeleteUndo = true;
    if (isOuterArc && lastPlacedPillarM)
    {
        currentBuildObjects.Remove(lastPlacedPillarM);
        Undo.DestroyObjectImmediate(lastPlacedPillarM);
        lastPlacedPillarM = null;
    }
    suppressDeleteUndo = ps;
    if (isOuterArc) RebuildProtectedPillarIdCache();

    // --- 3) Commit arc objects ---
    // Outer arc: V1C corner pillar
    if (isOuterArc && hover90EndPillarE)
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(hover90EndPillarE);
        if (src)
        {
            var pf = FindAsset<GameObject>(src.name);
            if (pf)
            {
                var rc = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                ApplyCurrentTextureVariantToObject(rc);
                ApplyContinueTopToPillar(rc);
                Undo.RegisterCreatedObjectUndo(rc, "Place V1C (Arc Close Loop)");
                currentBuildObjects.Add(rc);
                rc.transform.SetPositionAndRotation(
                    hover90EndPillarE.transform.position,
                    hover90EndPillarE.transform.rotation);
                rc.transform.localScale = continueScale;
            }
        }
    }

    // Curved rail
    var realCrv = (GameObject)PrefabUtility.InstantiatePrefab(curvedPrefab);
    ApplyCurrentTextureVariantToObject(realCrv);
    Undo.RegisterCreatedObjectUndo(realCrv, "Place Curved Rail (Arc Close Loop)");
    currentBuildObjects.Add(realCrv);
    ApplyRailVisualVariation(realCrv);
    ApplyCurvedRailVisualVariation(realCrv);
    realCrv.transform.SetPositionAndRotation(hover90Rail.transform.position, hover90Rail.transform.rotation);
    realCrv.transform.localScale = continueScale;
    if (continueFlipElements) FlipVisuals180(realCrv);

    // Save junction position before destroying preview
    Vector3 jPos = hover90PillarM.transform.position;
    Quaternion jRot = hover90PillarM.transform.rotation;

    // Clear hover90 preview
    ShowLastGhostPillarM();
    ShowCurvedGhostEndPillar();
    if (hover90Root) DestroyImmediate(hover90Root);
    hover90Root = null; hover90EndPillarE = null; hover90Rail = null; hover90PillarM = null;

    // --- 4) Junction pillar (between curved rail and close-loop chain) ---
    char jType = 'M';
    var v1cPf = FindAsset<GameObject>(GetPillarPrefabName('C'));
    if (v1cPf)
    {
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(v1cPf);
        tmp.transform.localScale = continueScale;
        tmp.transform.SetPositionAndRotation(jPos, jRot);
        if (FitV1TToRails(tmp, jPos)) jType = 'C';
        DestroyImmediate(tmp);
    }
    var jPrefab = FindAsset<GameObject>(GetPillarPrefabName(jType));
    if (jPrefab)
    {
        var jp = (GameObject)PrefabUtility.InstantiatePrefab(jPrefab);
        jp.transform.localScale = continueScale;
        ApplyCurrentTextureVariantToObject(jp);
        ApplyContinueTopToPillar(jp);
        Undo.RegisterCreatedObjectUndo(jp, $"Place V1{jType} (Arc Close Loop Junction)");
        currentBuildObjects.Add(jp);
        jp.transform.SetPositionAndRotation(jPos, jRot);
        FitV1TToRails(jp, jPos);
        if (jType == 'M') ApplyPillarMVisualVariation(jp);
    }

    // --- 5) Commit close-loop chain ---
    CommitCloseLoopChain();

    // --- 6) Handle continue anchor ---
    CleanupContinueAnchorForCloseLoop(isOuterArc);
}

}
} // namespace WB3DAssets.FenceModularSystem
