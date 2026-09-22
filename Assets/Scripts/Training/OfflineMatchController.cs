using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Offline twin of MatchManager (no Photon). Single-player training HUD:
/// a GOALS counter, match timer, stamina bar, controls hint, goal banner/flash
/// and a training-complete end screen. Any goal the player scores counts +1.
/// </summary>
public class OfflineMatchController : MonoBehaviour
{
    public static OfflineMatchController Instance { get; private set; }

    [Header("Match Settings")]
    public float matchDuration = 300f;
    public float goalFreezeTime = 3f;

    private int score = 0;
    private float matchTimer;
    private bool matchActive = false;
    private bool isFrozen = false;

    private Vector3 playerSpawnPos = new Vector3(0f, 1f, -8f);
    private Vector3 ballSpawnPos = new Vector3(0f, 0.5f, 0f);

    private Canvas canvas;
    private Font font;

    private Text scoreText;
    private Text timerText;
    private Text goalBannerText;
    private Text goalBannerSubText;
    private Image staminaBarFill;
    private GameObject goalBannerPanel;
    private Text matchEndResult;
    private Text matchEndScore;
    private GameObject matchEndPanel;
    private Image goalBannerGlow;
    private Text sprintFeedbackText;
    private GameObject sprintFeedbackObj;
    private GameObject goalFlashOverlay;
    private Image timerBg;

    private float lastStamina = 100f;
    private float goalBannerAnimTimer = 0f;
    private float goalFlashAlpha = 0f;

    private static readonly Color TeamBlue = new Color(0.2f, 0.5f, 1f);
    private static readonly Color DarkBg = new Color(0.08f, 0.08f, 0.12f, 0.92f);
    private static readonly Color AccentGold = new Color(1f, 0.84f, 0.2f);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        matchTimer = matchDuration;
        matchActive = true;
        BuildHUD();
    }

    private void Update()
    {
        if (!matchActive) return;

        matchTimer -= Time.deltaTime;
        if (matchTimer <= 0)
        {
            matchTimer = 0;
            matchActive = false;
            ShowMatchEnd();
        }

        UpdateHUD();
        UpdateGoalBannerAnim();
        UpdateGoalFlash();
    }

    public void OnGoalScored()
    {
        score++;
        StartCoroutine(GoalSequence());
    }

    public void SetStamina(float current, float max)
    {
        if (staminaBarFill != null)
        {
            float ratio = current / max;
            staminaBarFill.fillAmount = ratio;

            Color low = new Color(1f, 0.35f, 0.3f);
            Color mid = new Color(1f, 0.85f, 0.2f);
            Color high = new Color(0.25f, 0.95f, 0.4f);

            if (ratio > 0.5f)
                staminaBarFill.color = Color.Lerp(mid, high, (ratio - 0.5f) * 2f);
            else
                staminaBarFill.color = Color.Lerp(low, mid, ratio * 2f);

            if (current < 20f && lastStamina >= 20f)
                ShowSprintFeedback("LOW STAMINA", new Color(1f, 0.4f, 0.3f));
        }
        lastStamina = current;
    }

    public bool IsFrozen => isFrozen;

    public void ShowSprintFeedback(string msg, Color color)
    {
        if (sprintFeedbackObj == null) return;
        sprintFeedbackText.text = msg;
        sprintFeedbackText.color = color;
        sprintFeedbackObj.SetActive(true);
        CancelInvoke(nameof(HideSprintFeedback));
        Invoke(nameof(HideSprintFeedback), 1.2f);
    }

    private void HideSprintFeedback()
    {
        if (sprintFeedbackObj != null)
            sprintFeedbackObj.SetActive(false);
    }

    private IEnumerator GoalSequence()
    {
        isFrozen = true;

        goalBannerText.text = "GOAL!";
        goalBannerText.color = TeamBlue;
        goalBannerSubText.text = "YOU SCORE!";
        goalBannerSubText.color = Color.white;
        goalBannerGlow.color = new Color(TeamBlue.r, TeamBlue.g, TeamBlue.b, 0.3f);
        goalBannerPanel.SetActive(true);
        goalBannerAnimTimer = 0f;

        goalFlashOverlay.SetActive(true);
        goalFlashAlpha = 0.6f;
        goalFlashOverlay.GetComponent<Image>().color = new Color(TeamBlue.r, TeamBlue.g, TeamBlue.b, goalFlashAlpha);

        ThirdPersonCameraFollow cam = FindAnyObjectByType<ThirdPersonCameraFollow>();
        if (cam != null) cam.TriggerShake(0.5f, 0.2f);

        yield return new WaitForSeconds(goalFreezeTime);

        goalBannerPanel.SetActive(false);
        goalFlashOverlay.SetActive(false);
        isFrozen = false;

        ResetGamePositionsForKickoff();
    }

    /// <summary>
    /// Moves the player back to the blue kickoff spot and the ball to the
    /// centre spot after a goal.
    /// </summary>
    private void ResetGamePositionsForKickoff()
    {
        OfflinePlayerController player = FindAnyObjectByType<OfflinePlayerController>();
        if (player != null) player.ResetForKickoff();

        if (OfflineBall.Instance != null)
        {
            OfflineBall.Instance.ResetBallForKickoff(ballSpawnPos);
        }
    }

    private void UpdateGoalBannerAnim()
    {
        if (goalBannerPanel != null && goalBannerPanel.activeSelf)
        {
            goalBannerAnimTimer += Time.deltaTime * 4f;
            float scale = Mathf.Lerp(1.8f, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(goalBannerAnimTimer)));
            goalBannerPanel.transform.localScale = new Vector3(scale, scale, 1f);

            if (goalBannerText != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.03f;
                goalBannerText.transform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }
    }

    private void UpdateGoalFlash()
    {
        if (goalFlashOverlay != null && goalFlashOverlay.activeSelf)
        {
            goalFlashAlpha -= Time.deltaTime * 1.5f;
            if (goalFlashAlpha <= 0)
            {
                goalFlashOverlay.SetActive(false);
            }
            else
            {
                Image img = goalFlashOverlay.GetComponent<Image>();
                img.color = new Color(img.color.r, img.color.g, img.color.b, goalFlashAlpha);
            }
        }
    }

    private void ShowMatchEnd()
    {
        matchEndScore.text = $"{score}";
        matchEndResult.text = "TRAINING COMPLETE";
        matchEndResult.color = AccentGold;
        matchEndPanel.SetActive(true);
    }

    private void UpdateHUD()
    {
        scoreText.text = score.ToString();

        int mins = Mathf.FloorToInt(matchTimer / 60f);
        int secs = Mathf.FloorToInt(matchTimer % 60f);
        timerText.text = $"{mins:00}:{secs:00}";

        if (matchTimer <= 60f)
        {
            timerBg.color = new Color(0.6f, 0.15f, 0.1f, 0.7f);
            timerText.color = new Color(1f, 0.4f, 0.35f);
        }
    }

    public void ShowShootFeedback()
    {
        ShowSprintFeedback("SHOT!", AccentGold);
    }

    public void ShowTackleFeedback()
    {
        ShowSprintFeedback("TACKLE!", TeamBlue);
    }

    // =========================================================================
    // HUD CONSTRUCTION
    // =========================================================================

    private void BuildHUD()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        canvas = CreateCanvas("MatchHUD_Canvas", 50);

        BuildScoreboard();
        BuildTimer();
        BuildStaminaBar();
        BuildControlsHint();
        BuildGoalBanner();
        BuildMatchEndScreen();
        BuildSprintFeedback();
    }

    private Canvas CreateCanvas(string name, int sortOrder)
    {
        GameObject canvasGo = new GameObject(name);
        Canvas c = canvasGo.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortOrder;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        return c;
    }

    private void BuildScoreboard()
    {
        Transform root = canvas.transform;

        GameObject container = new GameObject("Scoreboard");
        container.transform.SetParent(root, false);
        RectTransform crt = container.AddComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 1f);
        crt.anchorMax = new Vector2(0.5f, 1f);
        crt.pivot = new Vector2(0.5f, 1f);
        crt.anchoredPosition = new Vector2(0, -10);
        crt.sizeDelta = new Vector2(420, 72);

        Image containerBg = container.AddComponent<Image>();
        containerBg.color = new Color(0.06f, 0.06f, 0.1f, 0.88f);

        GameObject teamPanel = new GameObject("TeamPanel");
        teamPanel.transform.SetParent(container.transform, false);
        Image tbpImg = teamPanel.AddComponent<Image>();
        tbpImg.color = new Color(TeamBlue.r, TeamBlue.g, TeamBlue.b, 0.15f);
        RectTransform tbpRT = teamPanel.GetComponent<RectTransform>();
        tbpRT.anchorMin = Vector2.zero;
        tbpRT.anchorMax = Vector2.one;
        tbpRT.offsetMin = Vector2.zero;
        tbpRT.offsetMax = Vector2.zero;

        Text teamLabel = MakeText(container.transform, "TeamLabel", "GOALS", font, 13,
            FontStyle.Bold, TeamBlue, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, 14), new Vector2(200, 20));

        scoreText = MakeText(container.transform, "Score", "0", font, 40,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(0, -8), new Vector2(200, 50));
    }

    private void BuildTimer()
    {
        Transform root = canvas.transform;

        GameObject timerContainer = new GameObject("TimerContainer");
        timerContainer.transform.SetParent(root, false);
        RectTransform tcrt = timerContainer.AddComponent<RectTransform>();
        tcrt.anchorMin = new Vector2(0.5f, 1f);
        tcrt.anchorMax = new Vector2(0.5f, 1f);
        tcrt.pivot = new Vector2(0.5f, 1f);
        tcrt.anchoredPosition = new Vector2(0, -86);
        tcrt.sizeDelta = new Vector2(120, 36);

        timerBg = timerContainer.AddComponent<Image>();
        timerBg.color = new Color(0.06f, 0.06f, 0.1f, 0.8f);

        timerText = MakeText(timerContainer.transform, "TimerText", "05:00", font, 22,
            FontStyle.Bold, new Color(0.85f, 0.85f, 0.9f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(120, 36));
    }

    private void BuildStaminaBar()
    {
        Transform root = canvas.transform;

        GameObject wrapper = new GameObject("StaminaWrapper");
        wrapper.transform.SetParent(root, false);
        RectTransform wrt = wrapper.AddComponent<RectTransform>();
        wrt.anchorMin = new Vector2(0, 0);
        wrt.anchorMax = new Vector2(0, 0);
        wrt.pivot = new Vector2(0, 0);
        wrt.anchoredPosition = new Vector2(30, 30);
        wrt.sizeDelta = new Vector2(280, 32);

        Image staminaBarBg = wrapper.AddComponent<Image>();
        staminaBarBg.color = new Color(0.12f, 0.12f, 0.16f, 0.9f);

        Text staminaIcon = MakeText(wrapper.transform, "StaminaIcon", "SPRINT", font, 11,
            FontStyle.Bold, new Color(0.65f, 0.65f, 0.7f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0, 18), new Vector2(280, 16));

        GameObject fillContainer = new GameObject("FillContainer");
        fillContainer.transform.SetParent(wrapper.transform, false);
        RectTransform fcrt = fillContainer.AddComponent<RectTransform>();
        fcrt.anchorMin = new Vector2(0.03f, 0.15f);
        fcrt.anchorMax = new Vector2(0.97f, 0.7f);
        fcrt.offsetMin = Vector2.zero;
        fcrt.offsetMax = Vector2.zero;

        Image fillBg = fillContainer.AddComponent<Image>();
        fillBg.color = new Color(0.08f, 0.08f, 0.1f, 1f);

        GameObject fillGo = new GameObject("StaminaFill");
        fillGo.transform.SetParent(fillContainer.transform, false);
        staminaBarFill = fillGo.AddComponent<Image>();
        staminaBarFill.color = new Color(0.25f, 0.95f, 0.4f);
        staminaBarFill.type = Image.Type.Filled;
        staminaBarFill.fillMethod = Image.FillMethod.Horizontal;
        staminaBarFill.fillAmount = 1f;
        RectTransform fillRT = fillGo.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = Vector2.zero;
        fillRT.offsetMax = Vector2.zero;
    }

    private void BuildControlsHint()
    {
        Transform root = canvas.transform;

        GameObject hintBg = new GameObject("ControlsHintBg");
        hintBg.transform.SetParent(root, false);
        Image hbImg = hintBg.AddComponent<Image>();
        hbImg.color = new Color(0.06f, 0.06f, 0.1f, 0.7f);
        RectTransform hbrt = hintBg.GetComponent<RectTransform>();
        hbrt.anchorMin = new Vector2(1, 0);
        hbrt.anchorMax = new Vector2(1, 0);
        hbrt.pivot = new Vector2(1, 0);
        hbrt.anchoredPosition = new Vector2(-15, 20);
        hbrt.sizeDelta = new Vector2(560, 50);

        MakeText(hintBg.transform, "MoveLabel", "WASD", font, 12,
            FontStyle.Bold, TeamBlue, new Vector2(0.12f, 0.65f), new Vector2(0.12f, 0.65f),
            Vector2.zero, new Vector2(60, 18));
        MakeText(hintBg.transform, "MoveDesc", "Move", font, 11,
            FontStyle.Normal, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.12f, 0.25f), new Vector2(0.12f, 0.25f),
            Vector2.zero, new Vector2(60, 16));

        MakeText(hintBg.transform, "SprintLabel", "SHIFT", font, 12,
            FontStyle.Bold, AccentGold, new Vector2(0.32f, 0.65f), new Vector2(0.32f, 0.65f),
            Vector2.zero, new Vector2(60, 18));
        MakeText(hintBg.transform, "SprintDesc", "Sprint", font, 11,
            FontStyle.Normal, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.32f, 0.25f), new Vector2(0.32f, 0.25f),
            Vector2.zero, new Vector2(60, 16));

        MakeText(hintBg.transform, "ShootLabel", "SPACE", font, 12,
            FontStyle.Bold, new Color(1f, 0.5f, 0.3f), new Vector2(0.52f, 0.65f), new Vector2(0.52f, 0.65f),
            Vector2.zero, new Vector2(70, 18));
        MakeText(hintBg.transform, "ShootDesc", "Shoot", font, 11,
            FontStyle.Normal, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.52f, 0.25f), new Vector2(0.52f, 0.25f),
            Vector2.zero, new Vector2(70, 16));

        MakeText(hintBg.transform, "TackleLabel", "E", font, 12,
            FontStyle.Bold, new Color(1f, 0.4f, 0.4f), new Vector2(0.72f, 0.65f), new Vector2(0.72f, 0.65f),
            Vector2.zero, new Vector2(40, 18));
        MakeText(hintBg.transform, "TackleDesc", "Tackle", font, 11,
            FontStyle.Normal, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.72f, 0.25f), new Vector2(0.72f, 0.25f),
            Vector2.zero, new Vector2(40, 16));

        MakeText(hintBg.transform, "PassLabel", "RMB", font, 12,
            FontStyle.Bold, new Color(0.4f, 0.8f, 1f), new Vector2(0.9f, 0.65f), new Vector2(0.9f, 0.65f),
            Vector2.zero, new Vector2(50, 18));
        MakeText(hintBg.transform, "PassDesc", "Pass", font, 11,
            FontStyle.Normal, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.9f, 0.25f), new Vector2(0.9f, 0.25f),
            Vector2.zero, new Vector2(50, 16));
    }

    private void BuildGoalBanner()
    {
        Transform root = canvas.transform;

        goalFlashOverlay = new GameObject("GoalFlashOverlay");
        goalFlashOverlay.transform.SetParent(root, false);
        Image flashImg = goalFlashOverlay.AddComponent<Image>();
        flashImg.color = new Color(1f, 1f, 1f, 0);
        RectTransform flashRT = goalFlashOverlay.GetComponent<RectTransform>();
        flashRT.anchorMin = Vector2.zero;
        flashRT.anchorMax = Vector2.one;
        flashRT.offsetMin = flashRT.offsetMax = Vector2.zero;
        goalFlashOverlay.SetActive(false);

        goalBannerPanel = new GameObject("GoalBannerPanel");
        goalBannerPanel.transform.SetParent(root, false);
        RectTransform bannerRT = goalBannerPanel.AddComponent<RectTransform>();
        bannerRT.anchorMin = new Vector2(0, 0.25f);
        bannerRT.anchorMax = new Vector2(1, 0.75f);
        bannerRT.offsetMin = bannerRT.offsetMax = Vector2.zero;

        goalBannerGlow = goalBannerPanel.AddComponent<Image>();
        goalBannerGlow.color = new Color(0.2f, 0.5f, 1f, 0.15f);

        GameObject innerPanel = new GameObject("InnerPanel");
        innerPanel.transform.SetParent(goalBannerPanel.transform, false);
        Image innerImg = innerPanel.AddComponent<Image>();
        innerImg.color = new Color(0.04f, 0.04f, 0.08f, 0.92f);
        RectTransform innerRT = innerPanel.GetComponent<RectTransform>();
        innerRT.anchorMin = new Vector2(0.15f, 0.05f);
        innerRT.anchorMax = new Vector2(0.85f, 0.95f);
        innerRT.offsetMin = innerRT.offsetMax = Vector2.zero;

        GameObject topLine = new GameObject("TopLine");
        topLine.transform.SetParent(innerPanel.transform, false);
        Image tlImg = topLine.AddComponent<Image>();
        tlImg.color = TeamBlue;
        RectTransform tlRT = topLine.GetComponent<RectTransform>();
        tlRT.anchorMin = new Vector2(0.1f, 0.92f);
        tlRT.anchorMax = new Vector2(0.9f, 0.94f);
        tlRT.offsetMin = tlRT.offsetMax = Vector2.zero;

        goalBannerText = MakeText(innerPanel.transform, "GoalText", "GOAL!", font, 80,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f),
            new Vector2(0, 15), new Vector2(600, 100));

        goalBannerSubText = MakeText(innerPanel.transform, "GoalSubText", "YOU SCORE!", font, 22,
            FontStyle.Bold, new Color(0.8f, 0.8f, 0.85f), new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f),
            new Vector2(0, -5), new Vector2(500, 30));

        goalBannerPanel.SetActive(false);
    }

    private void BuildMatchEndScreen()
    {
        Transform root = canvas.transform;

        matchEndPanel = new GameObject("MatchEndPanel");
        matchEndPanel.transform.SetParent(root, false);
        Image endBg = matchEndPanel.AddComponent<Image>();
        endBg.color = DarkBg;
        RectTransform endRT = matchEndPanel.GetComponent<RectTransform>();
        endRT.anchorMin = Vector2.zero;
        endRT.anchorMax = Vector2.one;
        endRT.offsetMin = endRT.offsetMax = Vector2.zero;

        GameObject card = new GameObject("EndCard");
        card.transform.SetParent(matchEndPanel.transform, false);
        Image cardImg = card.AddComponent<Image>();
        cardImg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        RectTransform cardRT = card.GetComponent<RectTransform>();
        cardRT.anchorMin = new Vector2(0.25f, 0.2f);
        cardRT.anchorMax = new Vector2(0.75f, 0.8f);
        cardRT.offsetMin = cardRT.offsetMax = Vector2.zero;

        GameObject topStripe = new GameObject("TopStripe");
        topStripe.transform.SetParent(card.transform, false);
        Image tsImg = topStripe.AddComponent<Image>();
        tsImg.color = AccentGold;
        RectTransform tsRT = topStripe.GetComponent<RectTransform>();
        tsRT.anchorMin = new Vector2(0, 0.93f);
        tsRT.anchorMax = new Vector2(1, 0.96f);
        tsRT.offsetMin = tsRT.offsetMax = Vector2.zero;

        MakeText(card.transform, "FullTimeLabel", "FULL TIME", font, 16,
            FontStyle.Bold, new Color(0.6f, 0.6f, 0.65f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f),
            Vector2.zero, new Vector2(300, 30));

        matchEndScore = MakeText(card.transform, "EndScore", "0", font, 64,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
            Vector2.zero, new Vector2(400, 80));

        matchEndResult = MakeText(card.transform, "EndResult", "", font, 32,
            FontStyle.Bold, AccentGold, new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f),
            Vector2.zero, new Vector2(500, 50));

        MakeText(card.transform, "WaitingText", "Goals recorded. Press ESC to return to the menu.", font, 14,
            FontStyle.Normal, new Color(0.45f, 0.45f, 0.5f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.1f),
            Vector2.zero, new Vector2(700, 25));

        matchEndPanel.SetActive(false);
    }

    private void BuildSprintFeedback()
    {
        Transform root = canvas.transform;

        sprintFeedbackObj = new GameObject("SprintFeedback");
        sprintFeedbackObj.transform.SetParent(root, false);
        RectTransform sfRT = sprintFeedbackObj.AddComponent<RectTransform>();
        sfRT.anchorMin = new Vector2(0.5f, 0.15f);
        sfRT.anchorMax = new Vector2(0.5f, 0.15f);
        sfRT.pivot = new Vector2(0.5f, 0.5f);
        sfRT.anchoredPosition = Vector2.zero;
        sfRT.sizeDelta = new Vector2(300, 40);

        sprintFeedbackText = MakeText(sprintFeedbackObj.transform, "SprintFeedbackText", "", font, 20,
            FontStyle.Bold, AccentGold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(300, 40));

        sprintFeedbackObj.SetActive(false);
    }

    private Text MakeText(Transform parent, string name, string content, Font f,
        int fontSize, FontStyle style, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.text = content;
        t.font = f;
        t.fontSize = fontSize;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return t;
    }
}