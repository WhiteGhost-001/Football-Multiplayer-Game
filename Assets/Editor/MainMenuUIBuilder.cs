using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Bakes the MainMenu canvas into the currently open scene as a real,
/// editable UI hierarchy (Tools &gt; Build Main Menu Canvas). After running
/// this once and saving, you can drag/reshape every element in the Inspector;
/// MainMenuBootstrap then only wires up the button click actions by name.
/// Idempotent: re-running replaces the existing MainMenu_Canvas.
/// </summary>
public static class MainMenuUIBuilder
{
    private static Font _font;

    [MenuItem("Tools/Build Main Menu Canvas")]
    public static void Build()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var scene = EditorSceneManager.GetActiveScene();

        GameObject existing = GameObject.Find("MainMenu_Canvas");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Build Main Menu Canvas",
                "A MainMenu_Canvas already exists. Rebuild it from the template layout?",
                "Rebuild", "Cancel"))
                return;
            Object.DestroyImmediate(existing);
        }

        EnsureEventSystem();

        BuildSplashCanvas(scene);

        GameObject canvasGo = new GameObject("MainMenu_Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        Transform root = canvas.transform;

        // Background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(root, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.06f, 0.13f);
        Stretch(bg.GetComponent<RectTransform>());

        // Pitch stripe decoration
        MakeImage(root, "PitchBand", new Color(0.08f, 0.26f, 0.14f),
            new Vector2(0f, 0f), new Vector2(1f, 0.2f), new Vector2(0, -280), Vector2.zero);

        // Title
        MakeText(root, "Title", "FOOTBALL MULTIPLAYER", 76, FontStyle.Bold,
            new Color(0.93f, 1f, 0.96f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -170), new Vector2(1300, 120));

        MakeText(root, "Subtitle", "UNITY NETWORKED FOOTBALL  \u2022  PHOTON PUN 2", 24, FontStyle.Bold,
            new Color(1f, 1f, 1f, 0.7f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -280), new Vector2(1100, 40));

        // Buttons (named so MainMenuBootstrap can find & wire them)
        MakeButton(root, "PlayButton", "PLAY ONLINE", new Vector2(0, 60));
        MakeButton(root, "TrainingButton", "TRAINING", new Vector2(0, -40));
        MakeButton(root, "HowToPlayButton", "HOW TO PLAY", new Vector2(0, -140));
        MakeButton(root, "QuitButton", "QUIT", new Vector2(0, -240));

        MakeText(root, "Footer", "A four-scene Unity project \u2022 WASD to move \u2022 Space to shoot", 18,
            FontStyle.Normal, new Color(1f, 1f, 1f, 0.45f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 30), new Vector2(900, 30));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorGUIUtility.PingObject(canvasGo);
        Debug.Log("[MainMenuUIBuilder] MainMenu_Canvas created. Edit it in the Scene/Hierarchy, then save the scene.");
    }

    private static void MakeButton(Transform parent, string name, string label, Vector2 pos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.1f, 0.55f, 0.25f);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(420, 78);

        go.AddComponent<Button>();

        MakeText(go.transform, "Label", label, 30, FontStyle.Bold, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void MakeImage(Transform parent, string name, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private static void MakeText(Transform parent, string name, string content, int size, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size2)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = _font;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size2;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void BuildSplashCanvas(UnityEngine.SceneManagement.Scene scene)
    {
        GameObject existing = GameObject.Find("SplashCanvas");
        if (existing != null) Object.DestroyImmediate(existing);

        GameObject scGo = new GameObject("SplashCanvas");
        Canvas canvas = scGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler sc = scGo.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        scGo.AddComponent<GraphicRaycaster>();

        Transform sr = canvas.transform;

        // Full-screen dark background
        GameObject bg = new GameObject("Background");
        bg.transform.SetParent(sr, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.06f, 0.13f);
        Stretch(bg.GetComponent<RectTransform>());

        // Circle
        GameObject circle = new GameObject("Circle");
        circle.transform.SetParent(sr, false);
        Image circleImg = circle.AddComponent<Image>();
        circleImg.color = new Color(0.93f, 1f, 0.96f);
        RectTransform crt = circle.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.55f);
        crt.anchorMax = new Vector2(0.5f, 0.55f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(120f, 120f);

        // "PRESS TO BEGIN" text
        GameObject txtGo = new GameObject("PromptText");
        txtGo.transform.SetParent(sr, false);
        Text txt = txtGo.AddComponent<Text>();
        txt.text = "PRESS TO BEGIN";
        txt.font = _font;
        txt.fontSize = 28;
        txt.fontStyle = FontStyle.Bold;
        txt.color = new Color(1f, 1f, 1f, 0.6f);
        txt.alignment = TextAnchor.MiddleCenter;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform txtRt = txtGo.GetComponent<RectTransform>();
        txtRt.anchorMin = new Vector2(0.5f, 0.55f);
        txtRt.anchorMax = new Vector2(0.5f, 0.55f);
        txtRt.pivot = new Vector2(0.5f, 0.5f);
        txtRt.anchoredPosition = new Vector2(0, -100);
        txtRt.sizeDelta = new Vector2(400, 40);

        // Invisible full-screen click area
        GameObject clickArea = new GameObject("ClickArea");
        clickArea.transform.SetParent(sr, false);
        Image clickImg = clickArea.AddComponent<Image>();
        clickImg.color = Color.clear;
        Stretch(clickArea.GetComponent<RectTransform>());
        clickArea.AddComponent<Button>();

        // Splash active by default, MainMenu_Canvas hidden until splash is clicked
        // (bootstrap handles the runtime toggle — both stay active in editor
        // so you can edit either via the Hierarchy).
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }
}