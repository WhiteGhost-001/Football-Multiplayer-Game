using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Photon.Pun;

public class MatchManager : MonoBehaviourPun
{
    public static MatchManager Instance { get; private set; }

    [Header("Match Settings")]
    public float matchDuration = 300f;
    public float goalFreezeTime = 3f;

    private int scoreTeamA = 0;
    private int scoreTeamB = 0;
    private float matchTimer;
    private bool matchActive = false;
    private bool isFrozen = false;

    private Vector3 masterSpawnPos = new Vector3(0f, 1f, -8f);
    private Vector3 guestSpawnPos  = new Vector3(0f, 1f,  8f);
    private Vector3 ballSpawnPos   = new Vector3(0f, 0.5f, 0f);

    private Canvas canvas;
    private Font font;

    private Text scoreTextA;
    private Text scoreTextB;
    private Text timerText;
    private Text goalBannerText;
    private Text goalBannerSubText;
    private Image staminaBarFill;
    private Image staminaBarBg;
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
    private static readonly Color TeamRed  = new Color(1f, 0.3f, 0.3f);
    private static readonly Color DarkBg   = new Color(0.08f, 0.08f, 0.12f, 0.92f);
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

    [PunRPC]
    public void OnGoalScoredRPC(int scoringTeam)
    {
        if (scoringTeam == 0)
            scoreTeamA++;
        else
            scoreTeamB++;

        StartCoroutine(GoalSequence(scoringTeam));
    }

    public void SetStamina(float current, float max)
    {
        if (staminaBarFill != null)
        {
            float ratio = current / max;
            staminaBarFill.fillAmount = ratio;

            Color low  = new Color(1f, 0.35f, 0.3f);
            Color mid  = new Color(1f, 0.85f, 0.2f);
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

    private IEnumerator GoalSequence(int scoringTeam)
    {
        isFrozen = true;

        string teamName = scoringTeam == 0 ? "BLUE" : "RED";
        Color teamColor = scoringTeam == 0 ? TeamBlue : TeamRed;

        goalBannerText.text = "GOAL!";
        goalBannerText.color = teamColor;
        goalBannerSubText.text = $"{teamName} TEAM SCORES!";
        goalBannerSubText.color = Color.white;
        goalBannerGlow.color = new Color(teamColor.r, teamColor.g, teamColor.b, 0.3f);
        goalBannerPanel.SetActive(true);
        goalBannerAnimTimer = 0f;

        goalFlashOverlay.SetActive(true);
        goalFlashAlpha = 0.6f;
        goalFlashOverlay.GetComponent<Image>().color = new Color(teamColor.r, teamColor.g, teamColor.b, goalFlashAlpha);

        CrowdAudioController crowd = FindAnyObjectByType<CrowdAudioController>();
        if (crowd != null) crowd.TriggerGoalCheer();

        ThirdPersonCameraFollow cam = FindAnyObjectByType<ThirdPersonCameraFollow>();
        if (cam != null) cam.TriggerShake(0.5f, 0.2f);

        yield return new WaitForSeconds(goalFreezeTime);

        goalBannerPanel.SetActive(false);
        goalFlashOverlay.SetActive(false);
        isFrozen = false;

        if (PhotonNetwork.IsMasterClient)
        {
            // Send everyone (both players and the ball) back to their starting spots.
            photonView.RPC(nameof(ResetGamePositionsForKickoff), RpcTarget.All);
            ResetBallPosition();
        }
    }

    /// <summary>
    /// Moves both players back to their original kickoff positions after a goal.
    /// </summary>
    [PunRPC]
    public void ResetGamePositionsForKickoff()
    {
        NetworkPlayerController[] players = FindObjectsByType<NetworkPlayerController>(FindObjectsSortMode.None);
        foreach (NetworkPlayerController player in players)
        {
            if (player.photonView == null || player.photonView.Owner == null) continue;

            bool isMaster = player.photonView.Owner.IsMasterClient;
            Vector3 pos = isMaster ? masterSpawnPos : guestSpawnPos;
            Quaternion rot = isMaster
                ? Quaternion.LookRotation(Vector3.forward)
                : Quaternion.LookRotation(Vector3.back);

            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            player.transform.SetPositionAndRotation(pos, rot);
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

    private void ResetBallPosition()
    {
        BallController ball = BallController.Instance != null
            ? BallController.Instance
            : FindAnyObjectByType<BallController>();

        if (ball == null || ball.photonView == null) return;

        // Broadcast on the ball's PhotonView: whatever client currently owns
        // the ball performs the reset, and PhotonRigidbodyView syncs it to both.
        ball.photonView.RPC(nameof(BallController.ResetBallForKickoff), RpcTarget.All, ballSpawnPos);
    }

    private void ShowMatchEnd()
    {
        string result;
        Color resultColor;
        if (scoreTeamA > scoreTeamB)
        {
            result = "BLUE TEAM WINS!";
            resultColor = TeamBlue;
        }
        else if (scoreTeamB > scoreTeamA)
        {
            result = "RED TEAM WINS!";
            resultColor = TeamRed;
        }
        else
        {
            result = "IT'S A DRAW!";
            resultColor = AccentGold;
        }

        matchEndScore.text = $"{scoreTeamA}  -  {scoreTeamB}";
        matchEndResult.text = result;
        matchEndResult.color = resultColor;
        matchEndPanel.SetActive(true);
    }

    private void UpdateHUD()
    {
        scoreTextA.text = scoreTeamA.ToString();
        scoreTextB.text = scoreTeamB.ToString();

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
        ShowSprintFeedback("TACKLE!", TeamRed);
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

        GameObject separator = new GameObject("Separator");
        separator.transform.SetParent(container.transform, false);
        Image sepImg = separator.AddComponent<Image>();
        sepImg.color = new Color(1f, 1f, 1f, 0.15f);
        RectTransform sepRT = separator.GetComponent<RectTransform>();
        sepRT.anchorMin = new Vector2(0.5f, 0.15f);
        sepRT.anchorMax = new Vector2(0.5f, 0.85f);
        sepRT.sizeDelta = new Vector2(2, 0);

        GameObject teamBluePanel = new GameObject("TeamBluePanel");
        teamBluePanel.transform.SetParent(container.transform, false);
        Image tbpImg = teamBluePanel.AddComponent<Image>();
        tbpImg.color = new Color(TeamBlue.r, TeamBlue.g, TeamBlue.b, 0.15f);
        RectTransform tbpRT = teamBluePanel.GetComponent<RectTransform>();
        tbpRT.anchorMin = Vector2.zero;
        tbpRT.anchorMax = new Vector2(0.48f, 1f);
        tbpRT.offsetMin = Vector2.zero;
        tbpRT.offsetMax = Vector2.zero;

        GameObject teamRedPanel = new GameObject("TeamRedPanel");
        teamRedPanel.transform.SetParent(container.transform, false);
        Image trpImg = teamRedPanel.AddComponent<Image>();
        trpImg.color = new Color(TeamRed.r, TeamRed.g, TeamRed.b, 0.15f);
        RectTransform trpRT = teamRedPanel.GetComponent<RectTransform>();
        trpRT.anchorMin = new Vector2(0.52f, 0f);
        trpRT.anchorMax = Vector2.one;
        trpRT.offsetMin = Vector2.zero;
        trpRT.offsetMax = Vector2.zero;

        Text teamALabel = MakeText(container.transform, "TeamALabel", "BLUE", font, 13,
            FontStyle.Bold, TeamBlue, new Vector2(0.24f, 0.5f), new Vector2(0.24f, 0.5f),
            new Vector2(0, 14), new Vector2(100, 20));

        scoreTextA = MakeText(container.transform, "ScoreA", "0", font, 40,
            FontStyle.Bold, Color.white, new Vector2(0.24f, 0.5f), new Vector2(0.24f, 0.5f),
            new Vector2(0, -8), new Vector2(100, 50));

        Text vsText = MakeText(container.transform, "VS", "-", font, 32,
            FontStyle.Bold, new Color(1f, 1f, 1f, 0.4f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(40, 50));

        Text teamBLabel = MakeText(container.transform, "TeamBLabel", "RED", font, 13,
            FontStyle.Bold, TeamRed, new Vector2(0.76f, 0.5f), new Vector2(0.76f, 0.5f),
            new Vector2(0, 14), new Vector2(100, 20));

        scoreTextB = MakeText(container.transform, "ScoreB", "0", font, 40,
            FontStyle.Bold, Color.white, new Vector2(0.76f, 0.5f), new Vector2(0.76f, 0.5f),
            new Vector2(0, -8), new Vector2(100, 50));
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

        staminaBarBg = wrapper.AddComponent<Image>();
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
            FontStyle.Bold, TeamRed, new Vector2(0.72f, 0.65f), new Vector2(0.72f, 0.65f),
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

        GameObject bottomLine = new GameObject("BottomLine");
        bottomLine.transform.SetParent(innerPanel.transform, false);
        Image blImg = bottomLine.AddComponent<Image>();
        blImg.color = TeamRed;
        RectTransform blRT = bottomLine.GetComponent<RectTransform>();
        blRT.anchorMin = new Vector2(0.1f, 0.06f);
        blRT.anchorMax = new Vector2(0.9f, 0.08f);
        blRT.offsetMin = blRT.offsetMax = Vector2.zero;

        goalBannerText = MakeText(innerPanel.transform, "GoalText", "GOAL!", font, 80,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.6f),
            new Vector2(0, 15), new Vector2(600, 100));

        goalBannerSubText = MakeText(innerPanel.transform, "GoalSubText", "TEAM SCORES!", font, 22,
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

        matchEndScore = MakeText(card.transform, "EndScore", "0 - 0", font, 64,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
            Vector2.zero, new Vector2(400, 80));

        matchEndResult = MakeText(card.transform, "EndResult", "", font, 32,
            FontStyle.Bold, AccentGold, new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f),
            Vector2.zero, new Vector2(500, 50));

        MakeText(card.transform, "WaitingText", "Waiting for host to restart...", font, 14,
            FontStyle.Normal, new Color(0.45f, 0.45f, 0.5f), new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.1f),
            Vector2.zero, new Vector2(400, 25));

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
