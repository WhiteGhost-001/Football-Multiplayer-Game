using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;

namespace WB3DAssets.FenceModularSystem
{
public partial class FenceModularWindow
{
void ReplaceBalusterStyle(int fromStyle, int toStyle, ContinueVariant variant)
{
var root = GetUiTargetFenceRoot();
if (!root) return;

    string v = VariantTagOf(variant);

    var replaceList = new List<(GameObject go, GameObject prefab)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        string sceneName = t.name;
        int cut = sceneName.IndexOf(" (");
        if (cut > 0)
            sceneName = sceneName.Substring(0, cut);

        // straight or curved
        bool isStraight = sceneName.StartsWith("sectionB_") && !sceneName.StartsWith("sectionBCrvd_");
        bool isCurved   = sceneName.StartsWith("sectionBCrvd_");
        if (!isStraight && !isCurved)
            continue;

        string fromTag = $"_V{fromStyle}_";
        if (!sceneName.Contains(fromTag))
            continue;

        string prefix = isCurved ? "sectionBCrvd_" : "sectionB_";
        string targetName = $"{prefix}V{toStyle}_PREFAB";

        var prefab = FindAsset<GameObject>(targetName);
        if (!prefab)
            continue;

        replaceList.Add((t.gameObject, prefab));
    }

    foreach (var (oldGO, prefab) in replaceList)
    {
        Transform parent = oldGO.transform.parent;
        Vector3 pos = oldGO.transform.position;
        Quaternion rot = oldGO.transform.rotation;
        int sibling = oldGO.transform.GetSiblingIndex();

        var newGO = InstantiateAndSwap(prefab);
TransferIndex(root, oldGO, newGO);
ApplyCurrentTextureVariantToObject(newGO);

// --- random visual variation on BALUSTER STYLE switch ---
var src = PrefabUtility.GetCorrespondingObjectFromSource(newGO);
if (src)
{
    string n = src.name;

    if (n.StartsWith("sectionBCrvd_"))
    {
        ApplyRailVisualVariation(newGO);
        ApplyCurvedRailVisualVariation(newGO);
    }
    else if (n.StartsWith("sectionB_"))
    {
        ApplyRailVisualVariation(newGO);
    }
}

        Undo.RegisterCreatedObjectUndo(newGO, "Replace Baluster Style");

        newGO.transform.SetParent(parent, true);
        newGO.transform.SetPositionAndRotation(pos, rot);
        newGO.transform.localScale = oldGO.transform.localScale;
        newGO.transform.SetSiblingIndex(sibling);

        Undo.DestroyObjectImmediate(oldGO);
    }

    if (fullDetailMode && root)
        ApplyFullDetailToFence(root, true);
}

void SyncUiFromFenceRoot(GameObject root)
{
    if (!root) return;

    // -------------------------------------------------
    // Variant detection from sections
    // -------------------------------------------------
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        string n = t.name;
        if (!n.StartsWith("sectionB_") && !n.StartsWith("sectionBCrvd_")) continue;

        if (IsExplicitFenceRootSelected())
        {
            selectedVariant = n.Contains("V5")
                ? ContinueVariant.V5
                : n.Contains("V4")
                    ? ContinueVariant.V4
                    : n.Contains("V3")
                        ? ContinueVariant.V3
                        : n.Contains("V2")
                            ? ContinueVariant.V2
                            : ContinueVariant.V1;
        }
        break;
    }

    // -------------------------------------------------
    // Texture variant (existing logic)
    // -------------------------------------------------
    var renderers = root.GetComponentsInChildren<Renderer>(true);
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("worn" + i)) { textureVariantIsWorn = true;  wornTextureIndex = i - 1; goto TOP_SCAN; }
                if (m.name.Contains("new"  + i)) { textureVariantIsWorn = false; newTextureIndex  = i - 1; goto TOP_SCAN; }
            }
        }
    }

TOP_SCAN:
    // -------------------------------------------------
    // TOPS: RECURSIVE SCAN (Root + ALL descendants)
    // -------------------------------------------------
    topPreviewIndex = topPreviews.Length; // default = No Tops

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        for (int i = 0; i < TopPrefabNames.Length; i++)
        {
            if (src.name == TopPrefabNames[i])
            {
                topPreviewIndex = i;
                return; // FIRST top defines UI
            }
        }
    }
}

void UpdateVariantAvailabilityFromHierarchy()
{
    variantV1Available = false;
    variantV2Available = false;

    var root = GetUiTargetFenceRoot();
    if (!root)
        return;

    // keep index in sync for top/style operations
    selectedFenceIndex = finalizedFences.IndexOf(root);

    bool hasV1 = false;
    bool hasV2 = false;

    // Detect variant ONLY from rails (pillars are always V1-prefabs in your setup)
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src)
            continue;

        string n = src.name;

        bool isRail =
            (n.StartsWith("sectionB_") || n.StartsWith("sectionBCrvd_")) &&
            n.EndsWith("_PREFAB");

        if (!isRail)
            continue;

        if (n.Contains("V1")) hasV1 = true;
        if (n.Contains("V2")) hasV2 = true;
        if (n.Contains("V3")) hasV1 = true; // V3 can switch to other types too
    }

    // Availability = you can switch to the OTHER variant if current rails exist.
    // If mixed (both), allow both (no locking).
    variantV2Available = hasV1;
    variantV1Available = hasV2;

    // IMPORTANT: no forced reset of selectedVariant here (prevents UI "blocking")
}

void UpdateTextureVariantFromHierarchy()
{
var sel = GetUiTargetFenceRoot();
if (!sel) return;

    var renderers = sel.GetComponentsInChildren<Renderer>(true);

    // First pass: look for ANY worn variant across all materials
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("worn" + i))
                {
                    textureVariantIsWorn = true;
                    wornTextureIndex = i - 1;
                    return;
                }
            }
        }
    }

    // Second pass: no worn found, look for new variant
    foreach (var r in renderers)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            for (int i = 4; i >= 1; i--)
            {
                if (m.name.Contains("new" + i))
                {
                    textureVariantIsWorn = false;
                    newTextureIndex = i - 1;
                    return;
                }
            }
        }
    }
}

void ReplaceFenceVariant(string fromTag, string toTag)
{
    if (!IsExplicitFenceRootSelected())
        return;

    var root = GetUiTargetFenceRoot();
    if (!root)
        return;

    // --- Clear stale rail tracking to prevent false repair triggers during swap ---
    lastSelectionWasRail = false;
    lastRailDeleteFenceRoot = null;
    railIdToFenceRoot.Clear();

    // --- TEMPORARILY DISABLE PILLAR DELETE PROTECTION ---
    bool prevSuppress = suppressDeleteUndo;
    suppressDeleteUndo = true;

    // --- Swap all prefabs (pos/rot preserved — snap offsets are identical across types) ---
    var replaceList = new List<(GameObject go, GameObject prefab)>();

    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (t == root.transform)
            continue;

        string sceneName = t.name;
        int cut = sceneName.IndexOf(" (");
        if (cut > 0)
            sceneName = sceneName.Substring(0, cut);

        if (!sceneName.Contains(fromTag))
            continue;

        string targetName = sceneName.Replace(fromTag, toTag);
        var targetPrefab = FindAsset<GameObject>(targetName);
        if (!targetPrefab)
            continue;

        replaceList.Add((t.gameObject, targetPrefab));
    }

    foreach (var (oldGO, prefab) in replaceList)
    {
        Transform parent = oldGO.transform.parent;
        Vector3 pos = oldGO.transform.position;
        Quaternion rot = oldGO.transform.rotation;
        Vector3 scale = oldGO.transform.localScale;
        int sibling = oldGO.transform.GetSiblingIndex();

        // Detect flip state before destroying old element
        bool wasFlipped = false;
        foreach (var r in oldGO.GetComponentsInChildren<Renderer>())
        {
            if (r.transform.localScale.z < 0f) { wasFlipped = true; break; }
        }

        bool wasStartPillar = false;
        if (fenceStartPillars.TryGetValue(root, out var sp) && sp == oldGO)
            wasStartPillar = true;

        var newGO = InstantiateAndSwap(prefab);
        TransferIndex(root, oldGO, newGO);
        ApplyCurrentTextureVariantToObject(newGO);

        var src = PrefabUtility.GetCorrespondingObjectFromSource(newGO);
        if (src)
        {
            string n = src.name;
            if (n.StartsWith("sectionBCrvd_"))
            {
                ApplyRailVisualVariation(newGO);
                ApplyCurvedRailVisualVariation(newGO);
            }
            else if (n.StartsWith("sectionB_"))
            {
                ApplyRailVisualVariation(newGO);
            }
            else if (IsPillarMPrefab(n))
            {
                ApplyPillarMVisualVariation(newGO);
            }
        }

        Undo.RegisterCreatedObjectUndo(newGO, "Replace Variant");

        newGO.transform.SetParent(parent, true);
        newGO.transform.SetPositionAndRotation(pos, rot);
        newGO.transform.localScale = scale;
        newGO.transform.SetSiblingIndex(sibling);

        if (wasFlipped && !IsPillarInstance(newGO)) FlipVisuals180(newGO);

        Undo.DestroyObjectImmediate(oldGO);

        if (wasStartPillar)
        {
            EnsureStartMarker(root, newGO);
            fenceStartPillars[root] = newGO;
        }
    }

    // --- RESTORE DELETE PROTECTION ---
    suppressDeleteUndo = prevSuppress;
    RebuildProtectedPillarIdCache();

    if (fullDetailMode)
        ApplyFullDetailToFence(root, true);
}
void ApplyTopToSelectedFence(int topIndex)
{
if (!IsExplicitFenceRootSelected())
    return;

    if (selectedVariant != ContinueVariant.V1)
        return;

GameObject root = null;

// Prefer selected root (UI works on root selection)
var sel = Selection.activeGameObject;
if (sel && finalizedFences.Contains(sel))
    root = sel;
else if (selectedFenceIndex >= 0 && selectedFenceIndex < finalizedFences.Count)
    root = finalizedFences[selectedFenceIndex];

if (!root)
    return;

if (topIndex < 0 || topIndex >= TopPrefabNames.Length)
{
    RemoveAllTopsFromSelectedFence();
    return;
}

    var topPrefab = FindAsset<GameObject>(TopPrefabNames[topIndex]);
    if (!topPrefab)
        return;

    // 1) COLLECT pillars FIRST (no hierarchy mutation here)
    var pillars = new List<Transform>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (IsPillarInstance(t.gameObject))
            pillars.Add(t);
    }

    // 2) APPLY tops (safe, isolated loop)
    foreach (var pillar in pillars)
    {
        var snapTop = FindSnap(pillar, TopSnapName);
        if (!snapTop)
            continue;

        RemoveTopFromPillar(pillar);

        var top = InstantiateAndSwap(topPrefab);
ApplyCurrentTextureVariantToObject(top);
        Undo.RegisterCreatedObjectUndo(top, "Place Top");

top.transform.SetParent(pillar, false);
top.transform.position = snapTop.position;
top.transform.rotation = snapTop.rotation;

// random visual rotation (Y only)
ApplyTopVisualRotation(top);
    }

    if (fullDetailMode && root)
        ApplyFullDetailToFence(root, true);
}

void RemoveAllTopsFromSelectedFence()
{
if (!IsExplicitFenceRootSelected())
    return;

GameObject root = null;

var sel = Selection.activeGameObject;
if (sel && finalizedFences.Contains(sel))
    root = sel;
else if (selectedFenceIndex >= 0 && selectedFenceIndex < finalizedFences.Count)
    root = finalizedFences[selectedFenceIndex];

if (!root)
    return;

    // 1) collect tops first
    var tops = new List<GameObject>();
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        if (IsTopInstance(t.gameObject))
            tops.Add(t.gameObject);
    }

    // 2) remove safely
    foreach (var top in tops)
    {
        if (top)
            Undo.DestroyObjectImmediate(top);
    }
}

ContinueVariant DetectVariantFromFenceRoot(GameObject root)
{
    if (!root) return ContinueVariant.V1;

    // Scan rails first, then pillars/gates as fallback
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;
        if (!n.EndsWith("_PREFAB")) continue;

        if (n.Contains("V5")) return ContinueVariant.V5;
        if (n.Contains("V4")) return ContinueVariant.V4;
        if (n.Contains("V3")) return ContinueVariant.V3;
        if (n.Contains("V2")) return ContinueVariant.V2;
        if (n.Contains("V1")) return ContinueVariant.V1;
    }

    return ContinueVariant.V1;
}

void ApplyUiStateFromFenceRoot(GameObject root)
{
    if (!root) return;

    // ---------- Defaults ----------
    ContinueVariant detectedVariant = ContinueVariant.V1;
bool hasAnyTop = false;
int detectedTopIndex = -1;
    bool detectedWorn = false;
    int detectedWornIndex = 0;
    int detectedNewIndex = 0;

    // ---------- Scan ----------
    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
    {
        var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
        if (!src) continue;

        string n = src.name;

        // Variant detection (pillar or rail)
        if (n.Contains("V5")) detectedVariant = ContinueVariant.V5;
        else if (n.Contains("V4")) detectedVariant = ContinueVariant.V4;
        else if (n.Contains("V3")) detectedVariant = ContinueVariant.V3;
        else if (n.Contains("V2")) detectedVariant = ContinueVariant.V2;

// Tops detection (exact index)
for (int i = 0; i < TopPrefabNames.Length; i++)
{
    if (n == TopPrefabNames[i])
    {
        hasAnyTop = true;
        detectedTopIndex = i;
        break;
    }
}

        // Texture variant detection
var r = t.GetComponent<Renderer>();
if (r)
{
    var mats = r.sharedMaterials;
    for (int mi = 0; mi < mats.Length; mi++)
    {
        var m = mats[mi];
        if (!m) continue;

        string mn = m.name.ToLowerInvariant();
        for (int wi = 4; wi >= 1; wi--)
        {
            if (mn.Contains("worn" + wi)) { detectedWorn = true; detectedWornIndex = wi - 1; break; }
        }
        if (!detectedWorn)
        {
            for (int ni = 4; ni >= 1; ni--)
            {
                if (mn.Contains("new" + ni)) { detectedNewIndex = ni - 1; break; }
            }
        }
    }
}
    }

    // ---------- Apply to UI ----------
    selectedVariant = detectedVariant;
// ALWAYS sync top when switching UI target fence
if (hasAnyTop)
{
    topPreviewIndex = detectedTopIndex >= 0
        ? detectedTopIndex
        : topPreviews.Length;
}
else
{
    topPreviewIndex = topPreviews.Length; // No Tops
}

    textureVariantIsWorn = detectedWorn;
    // FREE: only matching-index pairs ship (e.g. new3 + worn3) -> keep both indices in sync
    if (detectedWorn) { wornTextureIndex = detectedWornIndex; newTextureIndex = detectedWornIndex; }
    else { newTextureIndex = detectedNewIndex; wornTextureIndex = detectedNewIndex; }
}

void ApplyTextureVariantToSelectedFence(string fromToken, string toToken)
{
if (!IsExplicitFenceRootSelected())
    return;

var root = GetUiTargetFenceRoot();
if (!root)
    return;

    var renderers = root.GetComponentsInChildren<Renderer>(true);

    foreach (var r in renderers)
    {
        var mats = r.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (!m) continue;

            if (!m.name.Contains(fromToken))
                continue;

            string targetName = m.name.Replace(fromToken, toToken);
            var newMat = FindAsset<Material>(targetName);
            if (!newMat) continue;

            mats[i] = newMat;
            changed = true;
        }

        if (changed)
            r.sharedMaterials = mats;
    }
}

void ApplyCurrentTextureVariantToObject(GameObject go)
{
    if (!go)
        return;

    // Determine the target token based on current UI state
    string targetToken = !textureVariantIsWorn
        ? "new"  + (newTextureIndex  + 1)
        : "worn" + (wornTextureIndex + 1);

    // Replace any variant token with the current target
    string[] allTokens = { "new1", "new2", "new3", "new4", "worn1", "worn2", "worn3", "worn4" };
    foreach (string from in allTokens)
    {
        if (from == targetToken) continue;
        ApplyTextureVariantToObjectInternal(go, from, targetToken);
    }
}

void ApplyTextureVariantToObjectInternal(
    GameObject root,
    string fromToken,
    string toToken
)
{
    var renderers = root.GetComponentsInChildren<Renderer>(true);

    foreach (var r in renderers)
    {
        var mats = r.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < mats.Length; i++)
        {
            var m = mats[i];
            if (!m) continue;

            if (!m.name.Contains(fromToken))
                continue;

            string targetName = m.name.Replace(fromToken, toToken);
            var newMat = FindAsset<Material>(targetName);
            if (!newMat) continue;

            mats[i] = newMat;
            changed = true;
        }

        if (changed)
            r.sharedMaterials = mats;
    }
}

void ApplyRailVisualVariation(GameObject railRoot)
{
    // Apply visual-only variation to LOD meshes
    // Never touch the rail root or snap points

    if (!railRoot)
        return;

    // Collect LOD roots
    var lods = new List<Transform>();

    var lod0 = railRoot.transform.Find("LOD0");
    var lod1 = railRoot.transform.Find("LOD1");
    var lod2 = railRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

// Random X mirror only
    Vector3 scale = Vector3.one;
    if (Random.value > 0.5f) scale.x = -1f;

    foreach (var lod in lods)
        lod.localScale = scale;
}

void ApplyCurvedRailVisualVariation(GameObject railRoot)
{
    // Visual-only variation for CURVED rails
    // Rule: at most 2 unmirrored in a row -> at least every 3rd must be mirrored
    // Only local X mirroring (Z would flip concave/convex)

    if (!railRoot)
        return;

    var lods = new List<Transform>();

    var lod0 = railRoot.transform.Find("LOD0");
    var lod1 = railRoot.transform.Find("LOD1");
    var lod2 = railRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

    bool mirrorX;

    if (consecutiveUnmirroredCurvedRails >= 2)
    {
        // Force mirror to guarantee variation
        mirrorX = true;
    }
    else
    {
        // Optional mirror before the limit
        mirrorX = Random.value > 0.5f;
    }

    Vector3 scale = Vector3.one;
    if (mirrorX) scale.x = -1f;

    foreach (var lod in lods)
    {
        lod.localRotation = Quaternion.identity;
        lod.localScale = scale;
    }

    // Update streak counter
    if (mirrorX)
        consecutiveUnmirroredCurvedRails = 0;
    else
        consecutiveUnmirroredCurvedRails++;
}

void ApplyPillarMVisualVariation(GameObject pillarRoot)
{
    // Visual-only variation for V1M pillars
    // Rule: at most 2 unmirrored in a row -> at least every 3rd must be mirrored
    // Local X/Z only, never touch root or snap points

    if (!pillarRoot)
        return;

    var lods = new List<Transform>();

    var lod0 = pillarRoot.transform.Find("LOD0");
    var lod1 = pillarRoot.transform.Find("LOD1");
    var lod2 = pillarRoot.transform.Find("LOD2");

    if (lod0) lods.Add(lod0);
    if (lod1) lods.Add(lod1);
    if (lod2) lods.Add(lod2);

    if (lods.Count == 0)
        return;

    bool mirror;

    if (consecutiveUnmirroredPillarM >= 2)
    {
        // Force mirror to guarantee variation
        mirror = true;
    }
    else
    {
        // Optional mirror before the limit
        mirror = Random.value > 0.5f;
    }

    // Decide axis when mirroring
    int mirrorMode = mirror ? Random.Range(1, 4) : 0; // 1=X, 2=Z, 3=X+Z

    Vector3 scale = Vector3.one;
    if (mirrorMode == 1 || mirrorMode == 3) scale.x = -1f;
    if (mirrorMode == 2 || mirrorMode == 3) scale.z = -1f;

    foreach (var lod in lods)
    {
        lod.localRotation = Quaternion.identity;
        lod.localScale = scale;
    }

    // Update streak counter
    if (mirror)
        consecutiveUnmirroredPillarM = 0;
    else
        consecutiveUnmirroredPillarM++;
}

void ApplyTopVisualRotation(GameObject topRoot)
{
    // Visual-only rotation for tops
    // Allowed rotations: 90° steps around LOCAL Y
    // Safe because tops have no snap points and are centered

    if (!topRoot)
        return;

    int step = Random.Range(0, 4); // 0..3
    float angle = step * 90f;

    topRoot.transform.localRotation =
        Quaternion.Euler(0f, angle, 0f);
}

}
} // namespace WB3DAssets.FenceModularSystem
