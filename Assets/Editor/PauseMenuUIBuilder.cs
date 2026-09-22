using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bakes the ESC pause menu into the currently open scene as a real, editable
/// UI hierarchy (Tools &gt; Build Pause Menu Canvas). PauseMenu.Start() then only
/// wires the three buttons by name (WireExistingUI) and skips the procedural
/// BuildUI(). Idempotent: re-running replaces an existing PauseMenu_Canvas.
///
/// PausePanel is baked ACTIVE so it can be edited comfortably; at runtime
/// PauseMenu.Start() calls SetPaused(false), hiding it before the first frame,
/// so there is no flash.
/// </summary>
public static class PauseMenuUIBuilder
{
    private static Font _font;

    [MenuItem("Tools/Build Pause Menu Canvas")]
    public static void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Build Pause Menu Canvas",
                "Save the scene first (File > Save), then run this again.", "OK");
            return;
        }

        // Remove EVERY existing PauseMenu_Canvas (stale duplicates included)
        // before baking the fresh one.
        GameObject any = null;
        Canvas[] existing = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i].gameObject.name == "PauseMenu_Canvas")
            {
                if (any == null) any = existing[i].gameObject;
                else Object.DestroyImmediate(existing[i].gameObject);
            }
        }

        if (any != null)
        {
            if (!EditorUtility.DisplayDialog("Build Pause Menu Canvas",
                "PauseMenu_Canvas already exists in this scene. Rebuild it from the template layout?",
                "Rebuild", "Cancel"))
                return;
            Object.DestroyImmediate(any);
        }

        GameObject canvasGo = new GameObject("PauseMenu_Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // PausePanel is ACTIVE so the layout is fully editable in the editor.
        GameObject panel = new GameObject("PausePanel");
        panel.transform.SetParent(canvasGo.transform, false);

        // Full-screen dim behind the options
        GameObject overlay = new GameObject("Overlay");
        overlay.transform.SetParent(panel.transform, false);
        Image overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0.02f, 0.03f, 0.06f, 0.72f);
        overlayImg.raycastTarget = false;
        RectTransform overlayRt = overlayImg.rectTransform;
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = overlayRt.offsetMax = Vector2.zero;

        // Centered card
        GameObject card = new GameObject("Card");
        card.transform.SetParent(panel.transform, false);
        Image cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.08f, 0.1f, 0.16f, 0.96f);
        cardImg.raycastTarget = false;
        RectTransform cardRt = cardImg.rectTransform;
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(520, 520);

        MakeText(card.transform, "Title", "PAUSED", 46, FontStyle.Bold,
            new Color(0.93f, 1f, 0.9f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -40), new Vector2(460, 70));

        MakeButton(card.transform, "ResumeButton", "RESUME", new Vector2(0, -110));
        MakeButton(card.transform, "MainMenuButton", "MAIN MENU", new Vector2(0, -230));
        MakeButton(card.transform, "QuitButton", "QUIT GAME", new Vector2(0, -350));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorGUIUtility.PingObject(canvasGo);
        Debug.Log("[PauseMenuUIBuilder] PauseMenu_Canvas created. Edit it in the Scene/Hierarchy, then save the scene.");
    }

    private static void MakeButton(Transform parent, string name, string label, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.55f, 0.25f);
        RectTransform rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(380, 68);

        go.AddComponent<Button>();

        MakeText(go.transform, "Label", label, 26, FontStyle.Bold, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void MakeText(Transform parent, string name, string content, int size, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = _font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.raycastTarget = false;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
    }
}