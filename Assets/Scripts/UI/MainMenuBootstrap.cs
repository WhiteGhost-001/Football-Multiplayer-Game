using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the main menu UI at runtime and wires up scene navigation:
/// Play Online -> Match scene, How to Play, Quit.
/// </summary>
public class MainMenuBootstrap : MonoBehaviour
{
    /// <summary>
    /// Shows the splash only once per app session. Cleared after the splash is
    /// dismissed, so returning to the Main Menu from other scenes goes straight
    /// to the menu — never the splash again.
    /// </summary>
    private static bool splashShown = false;

    private Font font;
    private Canvas splashCanvas;
    private GameObject menuCanvas;

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        EnsureEventSystem();

        // A PauseMenu_Canvas baked into this scene has no running PauseMenu to
        // hide it (the pause menu only exists in training/online scenes), so it
        // would render on top of the menu and block all input. Shut it down.
        GameObject strayPause = GameObject.Find("PauseMenu_Canvas");
        if (strayPause != null) strayPause.SetActive(false);

        // Prefer an editable Canvas placed in the scene (see
        // Tools > Build Main Menu Canvas). Only fall back to the
        // runtime-generated layout when that canvas is absent.
        if (!WireExistingCanvas())
            BuildCanvas();

        // Skip the splash entirely once it has already been dismissed this session.
        if (splashShown)
        {
            // The splash canvas is baked active in the scene — always shut it off
            // again on re-entry so it never covers the menu.
            GameObject splashGo = GameObject.Find("SplashCanvas");
            if (splashGo != null) splashGo.SetActive(false);

            GameObject menu = GameObject.Find("MainMenu_Canvas");
            if (menu != null) menu.SetActive(true);
            return;
        }

        BuildSplashScreen();
    }

    // =================================================================
    // SPLASH SCREEN — "press to begin"
    // =================================================================

    private void BuildSplashScreen()
    {
        // If a scene-placed splash canvas already exists, use it.
        GameObject existing = GameObject.Find("SplashCanvas");
        if (existing != null)
        {
            splashCanvas = existing.GetComponent<Canvas>();
            if (splashCanvas == null) return;

            // Wire up the click area when a scene-placed splash is used.
            Transform clickArea = splashCanvas.transform.Find("ClickArea");
            if (clickArea != null)
            {
                Button cb = clickArea.GetComponent<Button>();
                if (cb == null) cb = clickArea.gameObject.AddComponent<Button>();
                Image ci = clickArea.GetComponent<Image>();
                if (ci == null)
                {
                    ci = clickArea.gameObject.AddComponent<Image>();
                    ci.color = Color.clear;
                }
                cb.targetGraphic = ci;
                cb.transition = Selectable.Transition.None;
                cb.onClick.AddListener(OnSplashClicked);
            }
        }
        else
        {
            GameObject scGo = new GameObject("SplashCanvas");
            splashCanvas = scGo.AddComponent<Canvas>();
            splashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            splashCanvas.sortingOrder = 50;
            CanvasScaler sc = scGo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            scGo.AddComponent<GraphicRaycaster>();

            Transform sr = splashCanvas.transform;

            // Full-screen dark background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sr, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.02f, 0.06f, 0.13f);
            Stretch(bg.GetComponent<RectTransform>());

            // Circle (ring)
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
            txt.font = font;
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

            // Invisible full-screen button to catch clicks
            GameObject clickArea = new GameObject("ClickArea");
            clickArea.transform.SetParent(sr, false);
            Image clickImg = clickArea.AddComponent<Image>();
            clickImg.color = Color.clear;
            Stretch(clickArea.GetComponent<RectTransform>());
            Button clickBtn = clickArea.AddComponent<Button>();
            clickBtn.targetGraphic = clickImg;
            clickBtn.transition = Selectable.Transition.None;
            clickBtn.onClick.AddListener(OnSplashClicked);
        }

        // Hide main menu until splash is dismissed
        menuCanvas = GameObject.Find("MainMenu_Canvas");
        if (menuCanvas != null)
            menuCanvas.SetActive(false);
    }

    private void OnSplashClicked()
    {
        splashCanvas.gameObject.SetActive(false);
        splashShown = true;

        // Show the main menu (cached reference — Find() can't see inactive
        // objects, and the menu is deactivated while the splash is up).
        if (menuCanvas != null)
            menuCanvas.SetActive(true);
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Finds the scene-placed MainMenu_Canvas and attaches the click handlers
    /// to its four named buttons. Returns false if the canvas or any button is
    /// missing (caller then builds the menu procedurally).
    /// </summary>
    private bool WireExistingCanvas()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null || canvas.name != "MainMenu_Canvas") return false;

        if (!WireButton(canvas.transform, "PlayButton", () => SceneManager.LoadScene("SampleScene"))) return false;
        if (!WireButton(canvas.transform, "TrainingButton", () => SceneManager.LoadScene("TrainingScene"))) return false;
        if (!WireButton(canvas.transform, "HowToPlayButton", () => SceneManager.LoadScene("HowToPlay"))) return false;
        if (!WireButton(canvas.transform, "QuitButton", () => Application.Quit())) return false;

        return true;
    }

    private bool WireButton(Transform root, string buttonName, UnityEngine.Events.UnityAction onClick)
    {
        Transform t = root.Find(buttonName);
        if (t == null) return false;
        Button btn = t.GetComponent<Button>();
        if (btn == null) return false;
        btn.onClick.AddListener(onClick);
        return true;
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private void BuildCanvas()
    {
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
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // Pitch stripe decoration
        MakeImage(root, "PitchBand", new Color(0.08f, 0.26f, 0.14f),
            new Vector2(0f, 0f), new Vector2(1f, 0.2f), new Vector2(0, -280), Vector2.zero);

        // Title
        MakeText(root, "Title", "FOOTBALL MULTIPLAYER", 76, FontStyle.Bold,
            new Color(0.93f, 1f, 0.96f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -170), new Vector2(1300, 120));

        MakeText(root, "Subtitle", "UNITY NETWORKED FOOTBALL  •  PHOTON PUN 2", 24, FontStyle.Bold,
            new Color(1f, 1f, 1f, 0.7f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -280), new Vector2(1100, 40));

        // Buttons
        MakeButton(root, "PlayButton", "PLAY ONLINE", new Vector2(0, 60),
            () => SceneManager.LoadScene("SampleScene"));
        MakeButton(root, "TrainingButton", "TRAINING", new Vector2(0, -40),
            () => SceneManager.LoadScene("TrainingScene"));
        MakeButton(root, "HowToPlayButton", "HOW TO PLAY", new Vector2(0, -140),
            () => SceneManager.LoadScene("HowToPlay"));
        MakeButton(root, "QuitButton", "QUIT", new Vector2(0, -240),
            () => Application.Quit());

        MakeText(root, "Footer", "A four-scene Unity project • WASD to move • Space to shoot", 18,
            FontStyle.Normal, new Color(1f, 1f, 1f, 0.45f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0, 30), new Vector2(900, 30));
    }

    private void MakeButton(Transform parent, string name, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
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

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText(go.transform, "Label", label, 30, FontStyle.Bold, Color.white,
            Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private void MakeImage(Transform parent, string name, Color color,
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

    private Text MakeText(Transform parent, string name, string content, int size, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size2)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = font;
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
        return t;
    }
}