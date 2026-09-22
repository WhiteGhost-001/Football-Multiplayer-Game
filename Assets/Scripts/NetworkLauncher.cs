using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using Photon.Pun;
using Photon.Realtime;

/// <summary>
/// Handles Photon connection and match initialization.
/// Shows a lobby/loading screen while connecting, then spawns the players in.
/// </summary>
public class NetworkLauncher : MonoBehaviourPunCallbacks
{
    private bool hasSpawned = false;

    private Canvas lobbyCanvas;
    private Text statusText;
    private Text playerCountText;
    private RectTransform spinner;
    private GameObject lobbyRoot;
    private float spinnerAngle = 0f;
    private bool matchStarted = false;

    private void Start()
    {
        BuildLobbyScreen();
        gameObject.AddComponent<PauseMenu>();
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        SetStatus("FOUND SERVER\nJoining match room...", false);
        PhotonNetwork.JoinOrCreateRoom("FootballRoom", new RoomOptions { MaxPlayers = 2 }, TypedLobby.Default);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        SetStatus("CONNECTION FAILED\nRetrying...", true);
        StartCoroutine(RetryJoin());
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (lobbyRoot != null && lobbyRoot.activeSelf && !matchStarted)
        {
            SetStatus("DISCONNECTED\nReconnecting...", true);
            StartCoroutine(RetryConnect());
        }
    }

    public override void OnJoinedRoom()
    {
        if (lobbyRoot != null && lobbyRoot.activeSelf)
        {
            SetStatus("JOINED MATCH\nWaiting for opponent...", false);

            string count = PhotonNetwork.CurrentRoom.PlayerCount.ToString();
            if (playerCountText != null)
                playerCountText.text = $"PLAYERS   {count} / 2";

            StartCoroutine(PollForOpponent());
        }

        if (!hasSpawned)
        {
            hasSpawned = true;
            StartMatch();
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (lobbyRoot != null && lobbyRoot.activeSelf)
        {
            string count = PhotonNetwork.CurrentRoom.PlayerCount.ToString();
            if (playerCountText != null)
                playerCountText.text = $"PLAYERS   {count} / 2";

            if (PhotonNetwork.CurrentRoom.PlayerCount >= 2)
                StartCoroutine(FadeOutLobby());
        }
    }

    private IEnumerator RetryConnect()
    {
        yield return new WaitForSeconds(1.5f);
        if (!PhotonNetwork.IsConnected)
            PhotonNetwork.ConnectUsingSettings();
    }

    private IEnumerator RetryJoin()
    {
        yield return new WaitForSeconds(1.5f);
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.JoinOrCreateRoom("FootballRoom", new RoomOptions { MaxPlayers = 2 }, TypedLobby.Default);
    }

    private IEnumerator PollForOpponent()
    {
        // Keep the lobby up until a second player arrives (or a max wait elapses)
        float elapsed = 0f;
        while (lobbyRoot != null && lobbyRoot.activeSelf)
        {
            int players = PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.PlayerCount : 0;
            if (players >= 2 || elapsed > 60f)
            {
                StartCoroutine(FadeOutLobby());
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator FadeOutLobby()
    {
        if (lobbyCanvas == null) yield break;

        CanvasGroup group = lobbyCanvas.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = lobbyCanvas.gameObject.AddComponent<CanvasGroup>();
        }
        matchStarted = true;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.8f;
            group.alpha = 1f - Mathf.SmoothStep(0, 1, t);
            yield return null;
        }
        group.alpha = 0f;
        Destroy(lobbyRoot);
    }

    private void Update()
    {
        if (lobbyRoot != null && lobbyRoot.activeSelf && spinner != null)
        {
            spinnerAngle += Time.deltaTime * 180f;
            spinner.localRotation = Quaternion.Euler(0, 0, spinnerAngle);
        }
    }

    private void SetStatus(string msg, bool red)
    {
        if (statusText == null) return;
        statusText.text = msg;
        statusText.color = red ? new Color(1f, 0.4f, 0.35f) : new Color(0.8f, 0.85f, 0.95f);
    }

    // =========================================================================
    // LOBBY SCREEN CONSTRUCTION
    // =========================================================================

    private void BuildLobbyScreen()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        lobbyCanvas = CreateCanvas("LobbyCanvas", 100);

        // Full-screen dark background
        GameObject bg = new GameObject("LobbyBG");
        bg.transform.SetParent(lobbyCanvas.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.05f, 0.06f, 0.1f, 1f);
        RectTransform bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

        // Top accent line
        GameObject topAccent = new GameObject("TopAccent");
        topAccent.transform.SetParent(lobbyCanvas.transform, false);
        Image taImg = topAccent.AddComponent<Image>();
        taImg.color = new Color(0.2f, 0.5f, 1f);
        RectTransform taRT = topAccent.GetComponent<RectTransform>();
        taRT.anchorMin = new Vector2(0, 1f);
        taRT.anchorMax = new Vector2(1, 1f);
        taRT.pivot = new Vector2(0.5f, 1f);
        taRT.sizeDelta = new Vector2(0, 5);

        // Bottom accent line
        GameObject bottomAccent = new GameObject("BottomAccent");
        bottomAccent.transform.SetParent(lobbyCanvas.transform, false);
        Image baImg = bottomAccent.AddComponent<Image>();
        baImg.color = new Color(1f, 0.3f, 0.3f);
        RectTransform baRT = bottomAccent.GetComponent<RectTransform>();
        baRT.anchorMin = new Vector2(0, 0);
        baRT.anchorMax = new Vector2(1, 0);
        baRT.pivot = new Vector2(0.5f, 0);
        baRT.sizeDelta = new Vector2(0, 5);

        // Title
        MakeText(lobbyCanvas.transform, "Title", "FOOTBALL", font, 72,
            FontStyle.Bold, new Color(0.85f, 0.9f, 1f), new Vector2(0.5f, 0.72f), new Vector2(0.5f, 0.72f),
            Vector2.zero, new Vector2(600, 90));

        MakeText(lobbyCanvas.transform, "SubTitle", "MULTIPLAYER", font, 26,
            FontStyle.Bold, new Color(0.2f, 0.5f, 1f), new Vector2(0.5f, 0.645f), new Vector2(0.5f, 0.645f),
            Vector2.zero, new Vector2(400, 40));

        // Spinner (soccer ball feel)
        GameObject ballSpinner = new GameObject("BallSpinner");
        ballSpinner.transform.SetParent(lobbyCanvas.transform, false);
        RectTransform bsRT = ballSpinner.AddComponent<RectTransform>();
        bsRT.anchorMin = new Vector2(0.5f, 0.5f);
        bsRT.anchorMax = new Vector2(0.5f, 0.5f);
        bsRT.anchoredPosition = new Vector2(0, 25);
        bsRT.sizeDelta = new Vector2(120, 120);
        spinner = bsRT;

        // Radar-sweep arms (4 thin bars, rotated 45° apart) — rotates for a search feel
        for (int i = 0; i < 4; i++)
        {
            GameObject arm = new GameObject($"SweepArm_{i}");
            arm.transform.SetParent(ballSpinner.transform, false);
            arm.transform.localRotation = Quaternion.Euler(0, 0, i * 45f);
            Image armImg = arm.AddComponent<Image>();
            Color armColor = (i % 2 == 0) ? new Color(0.2f, 0.5f, 1f, 0.85f) : new Color(1f, 0.3f, 0.3f, 0.85f);
            armImg.color = armColor;
            RectTransform armRT = arm.GetComponent<RectTransform>();
            armRT.anchorMin = new Vector2(0.5f, 0.5f);
            armRT.anchorMax = new Vector2(0.5f, 0.5f);
            armRT.anchoredPosition = Vector2.zero;
            armRT.sizeDelta = new Vector2(10, 108);
        }

        // Center ball
        Image ballCore = new GameObject("BallCore").AddComponent<Image>();
        ballCore.transform.SetParent(ballSpinner.transform, false);
        ballCore.color = new Color(0.15f, 0.16f, 0.2f, 0.95f);
        RectTransform ballCoreRT = ballCore.GetComponent<RectTransform>();
        ballCoreRT.anchorMin = new Vector2(0.5f, 0.5f);
        ballCoreRT.anchorMax = new Vector2(0.5f, 0.5f);
        ballCoreRT.anchoredPosition = Vector2.zero;
        ballCoreRT.sizeDelta = new Vector2(52, 52);

        Text splashText = MakeText(ballSpinner.transform, "Splash", "⚽", font, 30,
            FontStyle.Bold, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(60, 60));

        statusText = MakeText(lobbyCanvas.transform, "StatusText", "CONNECTING...", font, 22,
            FontStyle.Bold, new Color(0.8f, 0.85f, 0.95f), new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.42f),
            Vector2.zero, new Vector2(560, 56));

        playerCountText = MakeText(lobbyCanvas.transform, "PlayerCount", "", font, 18,
            FontStyle.Normal, new Color(0.5f, 0.55f, 0.6f), new Vector2(0.5f, 0.36f), new Vector2(0.5f, 0.36f),
            Vector2.zero, new Vector2(300, 30));

        MakeText(lobbyCanvas.transform, "Footer", "Powered by Photon PUN", font, 13,
            FontStyle.Normal, new Color(0.35f, 0.4f, 0.45f), new Vector2(0.5f, 0.03f), new Vector2(0.5f, 0.03f),
            Vector2.zero, new Vector2(300, 20));

        lobbyRoot = lobbyCanvas.gameObject;
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

    private void StartMatch()
    {
        EnsureGoalPosts();

        if (MatchManager.Instance == null)
        {
            GameObject mm = new GameObject("MatchManager");
            mm.AddComponent<PhotonView>().ViewID = 999;
            mm.AddComponent<MatchManager>();
        }

        SetupGoalDetectors();

        Vector3 spawnPos = PhotonNetwork.IsMasterClient
            ? new Vector3(0f, 1f, -8f)
            : new Vector3(0f, 1f,  8f);

        Quaternion spawnRot = PhotonNetwork.IsMasterClient
            ? Quaternion.LookRotation(Vector3.forward)
            : Quaternion.LookRotation(Vector3.back);

        PhotonNetwork.Instantiate("NetworkPlayer", spawnPos, spawnRot);

        if (PhotonNetwork.IsMasterClient)
            PhotonNetwork.Instantiate("SoccerBall_01", new Vector3(0f, 0.5f, 0f), Quaternion.identity);
    }

    private void EnsureGoalPosts()
    {
        GameObject host = GameObject.Find("GoalPostSetup");
        if (host == null)
        {
            host = new GameObject("GoalPostSetup");
        }
        GoalPostBuilder builder = host.GetComponent<GoalPostBuilder>();
        if (builder == null)
        {
            builder = host.AddComponent<GoalPostBuilder>();
        }
        builder.ReplaceGoalPosts();
    }

    private void SetupGoalDetectors()
    {
        // The +Z goal (Goal_North) belongs to the Guest (Team B, owner team 1)
        // and the -Z goal (Goal_South) belongs to the MasterClient (Team A,
        // owner team 0). Triggers are placed exactly on the goal mouth so a
        // goal only counts when the ball actually crosses between the posts.
        CreateGoalTriggerForGoal("Goal_North", "GoalZone_TeamB", 1, 1f);
        CreateGoalTriggerForGoal("Goal_South", "GoalZone_TeamA", 0, -1f);
    }

    private void CreateGoalTriggerForGoal(string goalName, string zoneName, int goalOwnerTeam, float direction)
    {
        if (GameObject.Find(zoneName) != null) return;

        Transform goal = GameObject.Find(goalName)?.transform;
        if (goal != null)
        {
            Transform leftPost = goal.Find("LeftPost");
            Transform rightPost = goal.Find("RightPost");
            Transform crossbar = goal.Find("Crossbar");
            if (leftPost != null && rightPost != null && crossbar != null)
            {
                float leftX  = leftPost.position.x;
                float rightX = rightPost.position.x;
                float goalLineZ = leftPost.position.z; // posts sit on the goal mouth plane
                float crossbarY = crossbar.position.y;

                float width  = Mathf.Abs(rightX - leftX);
                float height = Mathf.Max(0.5f, crossbarY);

                // A thin slab just behind the goal line spanning only the
                // opening between the posts and up to the crossbar.
                Vector3 center = new Vector3((leftX + rightX) / 2f, height / 2f, goalLineZ + direction * 0.6f);
                CreateGoalTrigger(zoneName, center, new Vector3(width, height, 1.2f), goalOwnerTeam, direction);
                return;
            }
        }

        // Fallback if the built goal posts can't be found yet: derive positions
        // from the stadium scale so the trigger still sits on the goal line.
        float scale = GetStadiumScale();
        float fbWidth  = 6f * scale;
        float fbHeight = 2.9f * scale;
        float fbLine   = 14.7f * scale;
        CreateGoalTrigger(zoneName,
            new Vector3(0f, fbHeight / 2f, fbLine * direction + direction * 0.6f),
            new Vector3(fbWidth, fbHeight, 1.2f),
            goalOwnerTeam, direction);
    }

    private float GetStadiumScale()
    {
        Transform envRoot = GoalPostBuilder.FindStadiumRoot();
        if (envRoot == null) return 1f;
        float scale = Mathf.Abs(envRoot.lossyScale.y);
        if (scale <= 0f || float.IsInfinity(scale) || float.IsNaN(scale)) return 1f;
        return scale;
    }

    private void CreateGoalTrigger(string name, Vector3 pos, Vector3 size, int goalOwnerTeam, float direction)
    {
        if (GameObject.Find(name) != null) return;

        GameObject zone = new GameObject(name);
        zone.transform.position = pos;

        BoxCollider col = zone.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size = size;

        GoalDetector detector = zone.AddComponent<GoalDetector>();
        detector.goalOwnerTeam = goalOwnerTeam;
        detector.goalDirection = direction;
        detector.requireCrossing = true;
    }
}