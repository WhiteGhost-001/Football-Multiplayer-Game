using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Playable step-by-step tutorial for the How To Play scene. Loads the real
/// Training scene (stadium) additively, spawns the player + ball, and walks
/// the player through each control. A centred card explains the command; the
/// player clicks to dismiss it and tries the action. Once performed, a NEXT
/// button appears in the top-right corner — the player can keep practicing
/// the command as long as they want, then press NEXT to move on. Ends with a
/// summary + back-to-menu.
/// </summary>
public class HowToPlayBootstrap : MonoBehaviour
{
    private Font font;
    private Canvas canvas;

    private OfflinePlayerController player;
    private OfflineBall ball;

    private GameObject overlay;
    private Text stepLabel;
    private Text keyText;
    private Text titleText;
    private Text descText;
    private Text hintText;
    private GameObject nextButton;
    private Text successLabel;

    private GameObject summaryPanel;

    private int currentStep;
    private bool actionDone;
    private bool nextClicked;
    private bool overlayClosed;
    private float backendCooldown;

    private static readonly Color BgDark    = new Color(0.03f, 0.05f, 0.09f, 0.8f);
    private static readonly Color CardBg    = new Color(0.08f, 0.08f, 0.12f, 0.97f);
    private static readonly Color KeyBlue   = new Color(0.45f, 0.85f, 1f);
    private static readonly Color TitleGold = new Color(1f, 0.84f, 0.2f);
    private static readonly Color DescWhite = new Color(0.92f, 0.93f, 0.97f);
    private static readonly Color HintDim   = new Color(0.5f, 0.53f, 0.6f);
    private static readonly Color AccentGold = new Color(1f, 0.84f, 0.2f);
    private static readonly Color Green     = new Color(0.35f, 0.95f, 0.5f);

    private enum TutorialAction { Move, Sprint, Shoot, Pass, Tackle }

    private struct Step
    {
        public string key;
        public string title;
        public string desc;
        public TutorialAction action;
        public Step(string key, string title, string desc, TutorialAction action)
        {
            this.key = key;
            this.title = title;
            this.desc = desc;
            this.action = action;
        }
    }

    private static readonly Step[] Steps =
    {
        new Step("W A S D",     "MOVE",   "Use W, A, S, D keys to move your player around the pitch.", TutorialAction.Move),
        new Step("LEFT SHIFT",  "SPRINT", "Hold LEFT SHIFT while moving to sprint.\nIt's faster, but it drains your stamina!", TutorialAction.Sprint),
        new Step("SPACE",       "SHOOT",  "Stand near the ball and press SPACE\nto kick it hard toward the goal.", TutorialAction.Shoot),
        new Step("RIGHT MOUSE", "PASS",   "Stand near the ball and click the\nRIGHT MOUSE BUTTON to pass.", TutorialAction.Pass),
        new Step("E",           "TACKLE", "Press E to lunge forward and\nwin the ball from an opponent.", TutorialAction.Tackle),
    };

    private void Awake()
    {
        // Destroy every baked PauseMenu_Canvas the instant this MonoBehaviour
        // wakes up. Awake() runs before the very first frame is rendered, so
        // the canvas never has a chance to appear on screen.
        // We use Destroy() — not SetActive(false) — because a disabled canvas
        // can still be re-enabled by other code. Destroyed objects are gone for
        // good; the procedural PauseMenu built by AddComponent<PauseMenu>() +
        // forceProcedural=true needs no pre-existing canvas at all.
        DestroyBakedPauseCanvases();
    }

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        EnsureEventSystem();

        // Destroy any baked pause canvas before we do anything else.
        DestroyBakedPauseCanvases();

        LoadTrainingStadium();

        // The additive Training scene also ships a baked canvas — destroy it too.
        DestroyBakedPauseCanvases();

        SpawnPlayer();
        SpawnBall();

        PauseMenu pause = gameObject.AddComponent<PauseMenu>();
        pause.forceProcedural = true;

        BuildUI();
        ShowStep(0);
    }

    /// <summary>
    /// Permanently removes every GameObject named "PauseMenu_Canvas" from all
    /// loaded scenes. Called in both Awake() and Start() so it catches the
    /// HowToPlay scene's own baked copy AND any copy brought in by the
    /// additively loaded TrainingScene.
    /// </summary>
    private void DestroyBakedPauseCanvases()
    {
        Canvas[] all = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].gameObject.name == "PauseMenu_Canvas")
                Destroy(all[i].gameObject);
        }
    }

    // =====================================================================
    // STADIUM (reuses the exact Training scene environment)
    // =====================================================================

    private void LoadTrainingStadium()
    {
        if (SceneManager.GetSceneByName("TrainingScene").isLoaded) return;

        // Don't let the training bootstrap spawn players/HUD/cones.
        TrainingSceneBootstrap.SuppressStart = true;

        try
        {
            SceneManager.LoadScene("TrainingScene", LoadSceneMode.Additive);
        }
        catch (System.Exception e)
        {
            // The tutorial must keep working even without the stadium loaded.
            Debug.LogError("[HowToPlay] Could not load TrainingScene: " + e.Message);
        }

        // The tutorial has its own camera, so silence EVERY camera that came
        // with the Training scene. It is also tagged MainCamera, sits at the
        // same render depth (-1) as ours, and would otherwise render a static
        // view over our follow camera and break Camera.main lookups.
        Scene training = SceneManager.GetSceneByName("TrainingScene");
        if (training.IsValid())
        {
            foreach (GameObject root in training.GetRootGameObjects())
            {
                Camera[] cams = root.GetComponentsInChildren<Camera>(true);
                for (int i = 0; i < cams.Length; i++)
                    cams[i].gameObject.SetActive(false);

                // The Training scene may carry editor-baked canvases (pause
                // menu, match HUD). Silencing them here keeps them from
                // rendering over the tutorial AND stops GameObject.Find from
                // grabbing the Training scene's PauseMenu_Canvas — so the
                // tutorial's own pause menu is always the one that gets wired.
                Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < canvases.Length; i++)
                    canvases[i].gameObject.SetActive(false);

                // The Training scene ships its own EventSystem. Two active
                // EventSystems break ALL UI input (the tutorial overlay could
                // not be clicked away), so suppress every copy that came with
                // the additively loaded scene.
                EventSystem[] systems = root.GetComponentsInChildren<EventSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                    systems[i].gameObject.SetActive(false);
            }
        }

        // The Training bootstrap consumes the one-shot flag in its own Awake()
        // (which runs synchronously during LoadScene), so a later normal
        // Training session starts fresh and nothing here needs resetting.
    }

    private void SpawnPlayer()
    {
        GameObject go = new GameObject("TutorialPlayer");
        go.transform.SetPositionAndRotation(
            new Vector3(0f, 1f, -8f), Quaternion.LookRotation(Vector3.forward));

        CapsuleCollider col = go.AddComponent<CapsuleCollider>();
        col.radius = 0.5f;
        col.height = 2f;
        col.direction = 1;
        col.center = Vector3.zero;

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.constraints = RigidbodyConstraints.FreezeRotationX
                       | RigidbodyConstraints.FreezeRotationY
                       | RigidbodyConstraints.FreezeRotationZ;

        player = go.AddComponent<OfflinePlayerController>();

        GameObject modelPrefab = Resources.Load<GameObject>("Floreswa/male01_1");
        if (modelPrefab != null)
        {
            GameObject model = Instantiate(modelPrefab, go.transform);
            model.name = "PlayerVisuals";
            model.transform.localPosition = new Vector3(0f, -1f, 0f);
            model.transform.localScale = Vector3.one * 0.9f;

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            RuntimeAnimatorController controller =
                Resources.Load<RuntimeAnimatorController>("PlayerAnimatorController");
            if (controller != null) animator.runtimeAnimatorController = controller;
        }

        player.LockInput();

        // The bootstrap lives on the HowToPlay camera, so grab it directly —
        // Camera.main can resolve to the (deactivated) Training scene camera
        // that temporarily also carries the MainCamera tag.
        Camera cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            // Render above any leftover Training scene camera and make it the
            // sole MainCamera so Camera.main lookups stay unambiguous.
            cam.depth = 0;
            cam.tag = "MainCamera";
            foreach (Camera other in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (other != cam)
                    other.gameObject.SetActive(false);
            }

            ThirdPersonCameraFollow follow = cam.GetComponent<ThirdPersonCameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<ThirdPersonCameraFollow>();
            follow.target = player.transform;
            follow.SnapToTarget();
        }
    }

    private void SpawnBall()
    {
        GameObject b = new GameObject("Ball");
        b.transform.position = new Vector3(0f, 0.5f, 0f);
        b.transform.localScale = Vector3.one * 1.8f;

        GameObject sourcePrefab = Resources.Load<GameObject>("SoccerBall_01");
        if (sourcePrefab != null)
        {
            MeshFilter sourceMesh = sourcePrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer sourceRenderer = sourcePrefab.GetComponentInChildren<MeshRenderer>();

            MeshFilter mf = b.AddComponent<MeshFilter>();
            if (sourceMesh != null) mf.sharedMesh = sourceMesh.sharedMesh;

            MeshRenderer mr = b.AddComponent<MeshRenderer>();
            if (sourceRenderer != null) mr.sharedMaterial = sourceRenderer.sharedMaterial;
        }

        SphereCollider col = b.AddComponent<SphereCollider>();
        col.radius = 0.10357375f;

        Rigidbody rb = b.AddComponent<Rigidbody>();
        rb.mass = 0.45f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        ball = b.AddComponent<OfflineBall>();
    }

    // =====================================================================
    // UI BUILD
    // =====================================================================

    private void BuildUI()
    {
        canvas = CreateCanvas("HowToPlay_Canvas", 10);

        overlay = new GameObject("Overlay");
        overlay.transform.SetParent(canvas.transform, false);
        Image overlayImg = overlay.AddComponent<Image>();
        overlayImg.color = BgDark;
        RectTransform overlayRT = overlay.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = overlayRT.offsetMax = Vector2.zero;

        Button catcher = overlay.AddComponent<Button>();
        catcher.targetGraphic = overlayImg;
        catcher.transition = Selectable.Transition.None;
        catcher.onClick.AddListener(OnOverlayClicked);

        GameObject card = new GameObject("Card");
        card.transform.SetParent(overlay.transform, false);
        Image cardImg = card.AddComponent<Image>();
        cardImg.color = CardBg;
        // The card is decorative — it must NOT swallow clicks, or the player
        // can never click the overlay's "click anywhere to close" button.
        cardImg.raycastTarget = false;
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.5f, 0.5f);
        cardRT.anchorMax = new Vector2(0.5f, 0.5f);
        cardRT.pivot     = new Vector2(0.5f, 0.5f);
        cardRT.anchoredPosition = Vector2.zero;
        cardRT.sizeDelta = new Vector2(820, 440);

        GameObject topLine = new GameObject("TopLine");
        topLine.transform.SetParent(card.transform, false);
        Image tlImg = topLine.AddComponent<Image>();
        tlImg.color = KeyBlue;
        tlImg.raycastTarget = false;
        RectTransform tlRT = topLine.GetComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.08f, 0.94f);
        tlRT.anchorMax = new Vector2(0.92f, 0.965f);
        tlRT.offsetMin = tlRT.offsetMax = Vector2.zero;

        stepLabel = MakeText(card.transform, "StepLabel", "", 16, FontStyle.Bold, HintDim,
            new Vector2(0.5f, 0.9f), new Vector2(0.5f, 0.9f),
            Vector2.zero, new Vector2(400, 24));

        keyText = MakeText(card.transform, "Key", "", 72, FontStyle.Bold, KeyBlue,
            new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f),
            Vector2.zero, new Vector2(700, 90));

        titleText = MakeText(card.transform, "Title", "", 34, FontStyle.Bold, TitleGold,
            new Vector2(0.5f, 0.38f), new Vector2(0.5f, 0.38f),
            Vector2.zero, new Vector2(600, 40));

        descText = MakeText(card.transform, "Desc", "", 24, FontStyle.Normal, DescWhite,
            new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.2f),
            Vector2.zero, new Vector2(700, 80));

        hintText = MakeText(card.transform, "Hint", "", 16, FontStyle.Italic, HintDim,
            new Vector2(0.5f, 0.06f), new Vector2(0.5f, 0.06f),
            Vector2.zero, new Vector2(600, 26));

        // NEXT button — top-right corner, sibling of the overlay (not a child)
        // so it survives while the overlay card is hidden during practice.
        nextButton = new GameObject("NextButton");
        nextButton.transform.SetParent(canvas.transform, false);
        Image nbImg = nextButton.AddComponent<Image>();
        nbImg.color = new Color(0.12f, 0.55f, 0.28f);
        RectTransform nbRT = nextButton.GetComponent<RectTransform>();
        nbRT.anchorMin = new Vector2(1f, 1f);
        nbRT.anchorMax = new Vector2(1f, 1f);
        nbRT.pivot     = new Vector2(1f, 1f);
        nbRT.anchoredPosition = new Vector2(-20f, -20f);
        nbRT.sizeDelta = new Vector2(220, 56);

        Button nb = nextButton.AddComponent<Button>();
        nb.targetGraphic = nbImg;
        nb.onClick.AddListener(OnNextClicked);

        MakeText(nextButton.transform, "Label", "NEXT \u25B6", 22, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(200, 40));

        nextButton.SetActive(false);

        // "Nice!" feedback — top-centre, sibling of the overlay so it stays
        // visible while the player keeps practicing after completing a step.
        successLabel = MakeText(canvas.transform, "SuccessLabel", "", 28, FontStyle.Bold, Green,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -90f), new Vector2(620, 40));
        successLabel.alignment = TextAnchor.MiddleCenter;

        BuildSummary();
    }

    private void BuildSummary()
    {
        summaryPanel = new GameObject("SummaryPanel");
        summaryPanel.transform.SetParent(canvas.transform, false);
        Image sBg = summaryPanel.AddComponent<Image>();
        sBg.color = BgDark;
        RectTransform sRT = summaryPanel.GetComponent<RectTransform>();
        sRT.anchorMin = Vector2.zero;
        sRT.anchorMax = Vector2.one;
        sRT.offsetMin = sRT.offsetMax = Vector2.zero;

        GameObject sCard = new GameObject("SCard");
        sCard.transform.SetParent(summaryPanel.transform, false);
        Image sCardImg = sCard.AddComponent<Image>();
        sCardImg.color = CardBg;
        sCardImg.raycastTarget = false;
        RectTransform sCardRT = sCard.GetComponent<RectTransform>();
        sCardRT.anchorMin = new Vector2(0.5f, 0.5f);
        sCardRT.anchorMax = new Vector2(0.5f, 0.5f);
        sCardRT.pivot     = new Vector2(0.5f, 0.5f);
        sCardRT.anchoredPosition = Vector2.zero;
        sCardRT.sizeDelta = new Vector2(820, 560);

        GameObject sLine = new GameObject("SLine");
        sLine.transform.SetParent(sCard.transform, false);
        Image sLineImg = sLine.AddComponent<Image>();
        sLineImg.color = AccentGold;
        sLineImg.raycastTarget = false;
        RectTransform sLineRT = sLine.GetComponent<RectTransform>();
        sLineRT.anchorMin = new Vector2(0.08f, 0.935f);
        sLineRT.anchorMax = new Vector2(0.92f, 0.96f);
        sLineRT.offsetMin = sLineRT.offsetMax = Vector2.zero;

        MakeText(sCard.transform, "STitle", "YOU'RE ALL SET!", 38, FontStyle.Bold, AccentGold,
            new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.84f),
            Vector2.zero, new Vector2(500, 48));

        Text body = MakeText(sCard.transform, "SBody", "", 22, FontStyle.Normal, DescWhite,
            new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
            Vector2.zero, new Vector2(700, 280));
        body.alignment = TextAnchor.UpperCenter;
        body.supportRichText = true;
        body.text =
            "<b>W A S D</b>  -  Move\n\n" +
            "<b>LEFT SHIFT</b>  -  Sprint (uses stamina)\n\n" +
            "<b>SPACE</b>  -  Shoot the ball\n\n" +
            "<b>RIGHT MOUSE</b>  -  Pass the ball\n\n" +
            "<b>E</b>  -  Tackle opponent";

        MakeText(sCard.transform, "SSub", "Score by kicking the ball into the opponent's net.\nFirst to 3 goals wins the match!",
            18, FontStyle.Normal, HintDim,
            new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.26f),
            Vector2.zero, new Vector2(650, 50));

        GameObject backBtn = new GameObject("BackBtn");
        backBtn.transform.SetParent(sCard.transform, false);
        Image btnImg = backBtn.AddComponent<Image>();
        btnImg.color = new Color(0.12f, 0.55f, 0.28f);
        RectTransform btnRT = backBtn.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.5f, 0.07f);
        btnRT.anchorMax = new Vector2(0.5f, 0.07f);
        btnRT.pivot     = new Vector2(0.5f, 0.5f);
        btnRT.sizeDelta = new Vector2(320, 54);

        Button btn = backBtn.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(() => SceneManager.LoadScene("MainMenu"));

        MakeText(backBtn.transform, "BtnLabel", "BACK TO MENU", 22, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(300, 40));

        summaryPanel.SetActive(false);
    }

    // =====================================================================
    // STEP FLOW
    // =====================================================================

    private void ShowStep(int index)
    {
        currentStep = index;
        overlay.SetActive(true);
        summaryPanel.SetActive(false);
        nextButton.SetActive(false);
        actionDone = false;
        overlayClosed = false;

        stepLabel.text = $"STEP {index + 1} of {Steps.Length}";
        keyText.text   = Steps[index].key;
        titleText.text = Steps[index].title;
        descText.text  = Steps[index].desc;
        hintText.text  = "Click anywhere to close, then try it!";
        successLabel.text = "";

        if (player != null) player.ResetActionFlags();
        if (player != null) player.LockInput();
    }

    private void OnOverlayClicked()
    {
        if (overlayClosed) return;
        overlayClosed = true;
        overlay.SetActive(false);
        nextButton.SetActive(false);

        // Unlock controls so the player can try the command.
        if (player != null)
        {
            player.ResetActionFlags();
            player.UnlockInput();
        }
        PlaceBallForStep(currentStep);
        backendCooldown = 0.35f;
    }

    private void OnNextClicked()
    {
        nextClicked = true;
    }

    /// <summary>
    /// Called every frame while the player is trying the current command.
    /// When the action is detected we freeze the game and show NEXT.
    /// </summary>
    private void Update()
    {
        if (backendCooldown > 0f)
        {
            backendCooldown -= Time.deltaTime;
            return;
        }

        if (overlayClosed && !actionDone)
        {
            bool done = IsActionDone(currentStep);
            if (done)
            {
                actionDone = true;
                successLabel.text = "Nice!";
                nextButton.SetActive(true);
                // Input stays unlocked — the player may keep practicing
                // this command as long as they want before pressing NEXT.
            }
        }

        KeepPracticeBallReady();

        if (nextClicked)
        {
            nextClicked = false;
            nextButton.SetActive(false);
            successLabel.text = "";

            int next = currentStep + 1;
            if (next < Steps.Length)
            {
                ShowStep(next);
            }
            else
            {
                overlay.SetActive(false);
                if (player != null) player.UnlockInput();
                summaryPanel.SetActive(true);
            }
        }
    }

    /// <summary>
    /// For the SHOOT and PASS steps, once the command has been completed the
    /// ball may be kicked far away; bring it back in front of the player when
    /// it rests at a distance so they can keep practicing.
    /// </summary>
    private void KeepPracticeBallReady()
    {
        if (!overlayClosed || !actionDone) return;
        if (ball == null || player == null) return;

        TutorialAction action = Steps[currentStep].action;
        if (action != TutorialAction.Shoot && action != TutorialAction.Pass) return;

        Vector3 toBall = ball.transform.position - player.transform.position;
        toBall.y = 0f;
        if (toBall.magnitude < 6f) return;

        Rigidbody ballRb = ball.GetComponent<Rigidbody>();
        if (ballRb != null && ballRb.linearVelocity.magnitude > 1f) return;

        Vector3 front = player.transform.position + player.transform.forward * 1.2f;
        ball.ResetBallForKickoff(new Vector3(front.x, 0.5f, front.z));
    }

    private bool IsActionDone(int step)
    {
        if (player == null) return false;
        switch (Steps[step].action)
        {
            case TutorialAction.Move:   return player.HasMoved;
            case TutorialAction.Sprint: return player.HasSprinted;
            case TutorialAction.Shoot:  return player.HasShot;
            case TutorialAction.Pass:   return player.HasPassed;
            case TutorialAction.Tackle: return player.HasTackled;
            default: return true;
        }
    }

    private void PlaceBallForStep(int step)
    {
        if (ball == null || player == null) return;
        if (Steps[step].action == TutorialAction.Shoot
            || Steps[step].action == TutorialAction.Pass)
        {
            Vector3 front = player.transform.position + player.transform.forward * 1.2f;
            ball.ResetBallForKickoff(new Vector3(front.x, 0.5f, front.z));
        }
    }

    // =====================================================================
    // FACTORY
    // =====================================================================

    private Canvas CreateCanvas(string name, int sortOrder)
    {
        GameObject go = new GameObject(name);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortOrder;
        CanvasScaler s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920, 1080);
        s.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    private void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private Text MakeText(Transform parent, string name, string content, int size,
        FontStyle style, Color color, Vector2 anchor, Vector2 pivot,
        Vector2 pos, Vector2 sz)
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
        t.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sz;
        return t;
    }
}