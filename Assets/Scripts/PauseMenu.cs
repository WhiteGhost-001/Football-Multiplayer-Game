using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Photon.Pun;

/// <summary>
/// Runtime ESC pause menu used in the Training and Online match scenes.
/// Pauses the game, and offers Resume / Return to Main Menu / Quit Game.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private Font font;
    private GameObject panel;
    private GameObject rootGo;
    private bool isPaused = false;
    private float timeScaleBeforePause = 1f;

    /// <summary>
    /// When true, the pause UI is always built from code and no scene-baked
    /// PauseMenu_Canvas is adopted. Set right after AddComponent (and before
    /// Start runs). Used by the How To Play scene, where baked copies have
    /// historically lingered visible and unwired at load.
    /// </summary>
    public bool forceProcedural = false;

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        EnsureEventSystem();

        if (forceProcedural)
        {
            BuildUI();
        }
        else if (!WireExistingUI())
        {
            BuildUI();
        }

        SetPaused(false);

        // The pause UI is only revealed on the player's FIRST ESC press.
        // Keeping the whole hull deactivated makes a visible "pause menu at
        // load" impossible in every scene, no matter what was baked or spawned
        // at runtime.
        if (rootGo != null)
            rootGo.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (rootGo != null && !rootGo.activeSelf)
                rootGo.SetActive(true);
            SetPaused(!isPaused);
        }
    }

    private void SetPaused(bool paused)
    {
        isPaused = paused;

        if (panel != null)
            panel.SetActive(paused);

        if (paused)
        {
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Time.timeScale = timeScaleBeforePause;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void Resume()
    {
        SetPaused(false);
    }

    private void GoToMainMenu()
    {
        Time.timeScale = 1f;

        if (PhotonNetwork.IsConnectedAndReady)
            PhotonNetwork.Disconnect();

        SceneManager.LoadScene("MainMenu");
    }

    private void QuitGame()
    {
        Time.timeScale = 1f;
        Application.Quit();
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adopts a scene-baked PauseMenu_Canvas (Tools &gt; Build Pause Menu Canvas).
    /// Wires the three buttons by name, then reports success so the procedural
    /// BuildUI() is skipped. Returns false if no usable canvas is found so the
    /// old runtime fallback still applies.
    ///
    /// Scenes can end up with more than one PauseMenu_Canvas (duplicate bakes,
    /// or an additively loaded Training scene in How To Play). Every copy but
    /// the one that gets wired is deactivated here, so a stale canvas can never
    /// sit on top of the game and swallow input.
    /// </summary>
    private bool WireExistingUI()
    {
        // Collect every PauseMenu_Canvas currently in loaded scenes.
        List<GameObject> candidates = new List<GameObject>();
        Canvas[] all = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].gameObject.name == "PauseMenu_Canvas")
                candidates.Add(all[i].gameObject);
        }

        if (candidates.Count == 0) return false;

        // Adopt the first copy whose layout is complete (active or not — the
        // HowToPlay scene deactivates ALL baked copies at load as a safety
        // measure, so adoption must work on a deactivated hull too). Skip and
        // later deactivate any broken/stale ones.
        foreach (GameObject canvasGo in candidates)
        {
            Transform found = canvasGo.transform.Find("PausePanel");
            if (found == null) continue;

            Button resume = found.Find("Card/ResumeButton")?.GetComponent<Button>();
            Button menu   = found.Find("Card/MainMenuButton")?.GetComponent<Button>();
            Button quit   = found.Find("Card/QuitButton")?.GetComponent<Button>();
            if (resume == null || menu == null || quit == null) continue;

            panel = found.gameObject;
            resume.onClick.AddListener(Resume);
            menu.onClick.AddListener(GoToMainMenu);
            quit.onClick.AddListener(QuitGame);
            rootGo = canvasGo;

            // The panel stays hidden until the player pauses; the whole hull is
            // deactivated again at the end of Start() anyway.
            panel.SetActive(false);

            // Kill every other copy so nothing stale lingers on screen.
            for (int j = 0; j < candidates.Count; j++)
            {
                if (candidates[j] != canvasGo)
                    candidates[j].SetActive(false);
            }
            return true;
        }

        // No copy was fully wired — hide them all so the procedural fallback
        // runs without a visible duplicate.
        for (int i = 0; i < candidates.Count; i++)
            candidates[i].SetActive(false);
        return false;
    }

    private void BuildUI()
    {
        GameObject canvasGo = new GameObject("PauseMenu_Canvas");
        rootGo = canvasGo;
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        panel = new GameObject("PausePanel");
        panel.transform.SetParent(canvasGo.transform, false);

        // Full-screen dim behind the options
        GameObject overlay = new GameObject("Overlay");
        overlay.transform.SetParent(panel.transform, false);
        Image overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = new Color(0.02f, 0.03f, 0.06f, 0.72f);
        RectTransform overlayRt = overlay.GetComponent<RectTransform>();
        overlayRt.anchorMin = Vector2.zero;
        overlayRt.anchorMax = Vector2.one;
        overlayRt.offsetMin = overlayRt.offsetMax = Vector2.zero;

        // Centered card
        GameObject card = new GameObject("Card");
        card.transform.SetParent(panel.transform, false);
        Image cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.08f, 0.1f, 0.16f, 0.96f);
        RectTransform cardRt = card.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.anchoredPosition = Vector2.zero;
        cardRt.sizeDelta = new Vector2(520, 520);

        MakeText(card.transform, "Title", "PAUSED", 46, FontStyle.Bold,
            new Color(0.93f, 1f, 0.9f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, -30), new Vector2(400, 64));

        MakeButton(card.transform, "ResumeButton", "RESUME", new Vector2(0, -146),
            () => Resume());
        MakeButton(card.transform, "MainMenuButton", "MAIN MENU", new Vector2(0, -256),
            () => GoToMainMenu());
        MakeButton(card.transform, "QuitButton", "QUIT GAME", new Vector2(0, -366),
            () => QuitGame());
    }

    private void MakeButton(Transform parent, string name, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.12f, 0.55f, 0.25f);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(380, 68);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        MakeText(go.transform, "Label", label, 26, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(360, 44));
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