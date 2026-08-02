using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 「既存の武器 / 新規作成」の選択と、新規作成の一連の画面。
///
///   ModeSelect … 2人同時にどちらかを選ぶ
///   Forge      … 1人ずつ。QR → 錬成中 → 抜刀 → ステータス → 戦うか決める
///
/// QRコードは自前で生成せず、用意された画像を貼るだけ。
/// StreamingAssets/qr.png に置くか、Inspector の qrImage に差し込む。
/// </summary>
public class ForgeUI : MonoBehaviour
{
    static readonly Color[] PlayerColors =
    {
        new Color(0.30f, 0.55f, 0.95f),
        new Color(0.95f, 0.42f, 0.35f),
    };

    static readonly string[] ModeLabels = { "既存の武器", "新規作成" };

    [Header("QR")]
    [Tooltip("未設定なら StreamingAssets/qr.png を読む")]
    public Texture2D qrImage;

    public string qrFileName = "qr.png";

    DuelManager _duel;
    Canvas _canvas;
    static Font _font;

    // モード選択
    RectTransform _modeRoot;
    readonly RectTransform[][] _modeCards = new RectTransform[2][];
    readonly RectTransform[] _modeCursor = new RectTransform[2];
    readonly int[] _modeIndex = new int[2];
    readonly bool[] _stickLatched = new bool[2];
    Text _modeStatus;

    // 錬成
    RectTransform _forgeRoot;
    RectTransform _qrPanel;
    RawImage _qrView;
    Text _qrMissingNote;
    Text _qrCaption;
    RectTransform _forgingPanel;
    WavyText _forgingText;
    RectTransform _forgingRing;
    RectTransform _revealPanel;
    RawImage _revealSword;
    Text _revealName;
    Text _revealStats;
    Text _revealPrompt;
    WavyText _revealHeadline;
    RectTransform _drawCommandRoot;
    WavyText _drawCommandText;
    Text _drawCommandEchoA;
    Text _drawCommandEchoB;
    RawImage _drawCommandGlow;
    readonly RawImage[] _drawCommandSlashes = new RawImage[4];
    Text _forgeOwner;
    Text _forgeProgress;
    RawImage _revealFlash;
    AudioSource _voiceSource;
    AudioSource _sfxSource;
    float _drawStarted = -1f;
    float _drawCommandStarted = -1f;

    public static ForgeUI Create(DuelManager duel)
    {
        var go = new GameObject("ForgeUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var ui = go.AddComponent<ForgeUI>();
        ui.Init(duel);
        return ui;
    }

    void Init(DuelManager duel)
    {
        _duel = duel;
        _voiceSource = gameObject.AddComponent<AudioSource>();
        _voiceSource.playOnAwake = false;
        _sfxSource = UiSoundPlayer.AddSource(gameObject);

        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;   // 選択画面より前

        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildModeScreen();
        BuildForgeScreen();

        _duel.OnPhaseChanged += HandlePhaseChanged;
        _duel.OnModeChanged += HandleModeChanged;
        _duel.OnForgeStepChanged += HandleForgeStepChanged;

        HandlePhaseChanged(_duel.Current);
    }

    void OnDestroy()
    {
        if (_duel == null) return;
        _duel.OnPhaseChanged -= HandlePhaseChanged;
        _duel.OnModeChanged -= HandleModeChanged;
        _duel.OnForgeStepChanged -= HandleForgeStepChanged;
    }

    // ---------- モード選択 ----------

    void BuildModeScreen()
    {
        _modeRoot = CreatePanel("ModeScreen");
        CreateBackdrop(_modeRoot);

        var title = new GameObject("Title", typeof(RectTransform)).GetComponent<RectTransform>();
        title.SetParent(_modeRoot, false);
        title.anchorMin = new Vector2(0.5f, 1f);
        title.anchorMax = new Vector2(0.5f, 1f);
        title.anchoredPosition = new Vector2(0f, -120f);
        var headline = title.gameObject.AddComponent<WavyText>();
        headline.fontSize = 64;
        headline.waveHeight = 6f;
        headline.revealInterval = 0.04f;
        headline.SetText("剣をどうする？");

        for (int player = 0; player < 2; player++)
        {
            float side = player == 0 ? -1f : 1f;
            var column = new GameObject("P" + (player + 1), typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(_modeRoot, false);
            column.anchorMin = column.anchorMax = new Vector2(0.5f, 0.5f);
            column.anchoredPosition = new Vector2(side * 400f, 20f);

            Text label = CreateText(column, "Label", 40, TextAnchor.MiddleCenter, PlayerColors[player]);
            label.rectTransform.anchoredPosition = new Vector2(0f, 220f);
            label.text = player == 0 ? "1P" : "2P";

            _modeCards[player] = new RectTransform[ModeLabels.Length];

            // カーソルはカードより先に作って背面に置く（枠として縁が見える）
            var cursor = CreateRawImage(column, "Cursor", PlayerColors[player]);
            cursor.rectTransform.sizeDelta = new Vector2(480f, 140f);
            _modeCursor[player] = cursor.rectTransform;

            for (int i = 0; i < ModeLabels.Length; i++)
            {
                var card = new GameObject("Mode" + i, typeof(RectTransform)).GetComponent<RectTransform>();
                card.SetParent(column, false);
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
                card.sizeDelta = new Vector2(468f, 128f);
                card.anchoredPosition = new Vector2(0f, 80f - i * 160f);

                var bg = CreateRawImage(card, "Bg", new Color(0.10f, 0.14f, 0.24f));
                Stretch(bg.rectTransform);

                Text text = CreateText(card, "Text", 38, TextAnchor.MiddleCenter, Color.white);
                Stretch(text.rectTransform);
                text.text = ModeLabels[i];

                _modeCards[player][i] = card;
            }
        }

        _modeStatus = CreateText(_modeRoot, "Status", 28, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
        _modeStatus.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        _modeStatus.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        _modeStatus.rectTransform.anchoredPosition = new Vector2(0f, 90f);

        UpdateModeCursors();
    }

    void UpdateModeCursors()
    {
        for (int player = 0; player < 2; player++)
        {
            if (_modeCursor[player] == null) continue;

            int index = Mathf.Clamp(_modeIndex[player], 0, ModeLabels.Length - 1);
            _modeCursor[player].anchoredPosition = _modeCards[player][index].anchoredPosition;

            bool decided = _duel.GetMode(player) != DuelManager.PlayerMode.Undecided;
            Color color = PlayerColors[player];
            color.a = decided ? 1f : 0.55f;
            _modeCursor[player].GetComponent<RawImage>().color = color;
        }

        if (_modeStatus != null)
        {
            _modeStatus.text =
                $"1P: {ModeStatusText(0)}      2P: {ModeStatusText(1)}\n" +
                "上下=カーソル移動   ×=けってい   ○=決定を戻す\n" +
                "キーボード(代用): 1P = A/D・F   2P = ←/→・.";
        }
    }

    string ModeStatusText(int player)
    {
        DuelManager.PlayerMode mode = _duel.GetMode(player);
        if (mode == DuelManager.PlayerMode.Undecided) return "えらんでいます";
        return mode == DuelManager.PlayerMode.Create ? "新規作成" : "既存の武器";
    }

    // ---------- 錬成 ----------

    void BuildForgeScreen()
    {
        _forgeRoot = CreatePanel("ForgeScreen");
        CreateBackdrop(_forgeRoot);

        _forgeOwner = CreateText(_forgeRoot, "Owner", 34, TextAnchor.MiddleCenter, Color.white);
        _forgeOwner.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        _forgeOwner.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _forgeOwner.rectTransform.anchoredPosition = new Vector2(0f, -70f);

        _forgeProgress = CreateText(_forgeRoot, "Progress", 25, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.28f));
        _forgeProgress.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        _forgeProgress.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        _forgeProgress.rectTransform.anchoredPosition = new Vector2(0f, -118f);

        // --- QR待ち ---
        _qrPanel = CreateSubPanel(_forgeRoot, "QrPanel");

        var qrFrame = CreateRawImage(_qrPanel, "QrFrame", Color.white);
        qrFrame.rectTransform.anchorMin = qrFrame.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        qrFrame.rectTransform.sizeDelta = new Vector2(452f, 452f);
        qrFrame.rectTransform.anchoredPosition = new Vector2(0f, 40f);

        _qrView = CreateRawImage(qrFrame.rectTransform, "Qr", Color.white);
        _qrView.rectTransform.anchorMin = Vector2.zero;
        _qrView.rectTransform.anchorMax = Vector2.one;
        _qrView.rectTransform.offsetMin = new Vector2(14f, 14f);
        _qrView.rectTransform.offsetMax = new Vector2(-14f, -14f);

        // 画像が未配置のとき、白い四角のままだと不具合に見えるので断り書きを出す
        _qrMissingNote = CreateText(_qrView.rectTransform, "Missing", 24, TextAnchor.MiddleCenter, new Color(0.75f, 0.8f, 0.9f));
        Stretch(_qrMissingNote.rectTransform);
        _qrMissingNote.text = "QR画像が未配置\n\nStreamingAssets/qr.png\nに置いてください";

        _qrCaption = CreateText(_qrPanel, "Caption", 32, TextAnchor.UpperCenter, new Color(0.9f, 0.94f, 1f));
        _qrCaption.rectTransform.anchoredPosition = new Vector2(0f, -270f);
        _qrCaption.text = "スマホで読み取って、名前・身長・全身写真・音声を登録\n\n送信が完了すると自動で錬成がはじまる";

        // --- 錬成中 ---
        _forgingPanel = CreateSubPanel(_forgeRoot, "ForgingPanel");

        var ring = CreateRawImage(_forgingPanel, "Ring", new Color(1f, 0.72f, 0.2f, 0.30f));
        ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        ring.rectTransform.sizeDelta = new Vector2(360f, 12f);
        // 見出しと同じ高さに置くと取り消し線に見えるので下にずらす
        ring.rectTransform.anchoredPosition = new Vector2(0f, -90f);
        _forgingRing = ring.rectTransform;

        var headlineGo = new GameObject("Headline", typeof(RectTransform)).GetComponent<RectTransform>();
        headlineGo.SetParent(_forgingPanel, false);
        headlineGo.anchorMin = headlineGo.anchorMax = new Vector2(0.5f, 0.5f);
        headlineGo.anchoredPosition = new Vector2(0f, 20f);
        _forgingText = headlineGo.gameObject.AddComponent<WavyText>();
        _forgingText.fontSize = 76;
        _forgingText.waveHeight = 14f;
        _forgingText.waveSpeed = 5.2f;
        _forgingText.revealInterval = 0.07f;

        // --- 抜刀・ステータス ---
        _revealPanel = CreateSubPanel(_forgeRoot, "RevealPanel");

        _revealFlash = CreateRawImage(_revealPanel, "RevealFlash", new Color(1f, 0.76f, 0.20f, 0f));
        _revealFlash.rectTransform.anchorMin = _revealFlash.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _revealFlash.rectTransform.sizeDelta = new Vector2(1100f, 1100f);

        BuildDrawCommand();

        var revealHeadGo = new GameObject("RevealHeadline", typeof(RectTransform)).GetComponent<RectTransform>();
        revealHeadGo.SetParent(_revealPanel, false);
        revealHeadGo.anchorMin = revealHeadGo.anchorMax = new Vector2(0.5f, 0.5f);
        revealHeadGo.anchoredPosition = new Vector2(0f, 420f);
        _revealHeadline = revealHeadGo.gameObject.AddComponent<WavyText>();
        _revealHeadline.fontSize = 58;
        _revealHeadline.waveHeight = 7f;
        _revealHeadline.revealInterval = 0.05f;

        _revealSword = CreateRawImage(_revealPanel, "Sword", Color.white);
        _revealSword.rectTransform.anchorMin = _revealSword.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _revealSword.rectTransform.sizeDelta = new Vector2(220f, 610f);
        _revealSword.rectTransform.anchoredPosition = new Vector2(0f, -5f);

        _revealName = CreateText(_revealPanel, "Name", 44, TextAnchor.MiddleCenter, Color.white);
        _revealName.rectTransform.anchoredPosition = new Vector2(0f, 340f);

        _revealStats = CreateText(_revealPanel, "Stats", 30, TextAnchor.MiddleCenter, new Color(0.72f, 0.88f, 1f));
        _revealStats.rectTransform.anchoredPosition = new Vector2(0f, -350f);

        _revealPrompt = CreateText(_revealPanel, "Prompt", 34, TextAnchor.MiddleCenter, new Color(1f, 0.86f, 0.4f));
        _revealPrompt.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        _revealPrompt.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        _revealPrompt.rectTransform.anchoredPosition = new Vector2(0f, 110f);
    }

    // ---------- 表示切替 ----------

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        bool mode = phase == DuelManager.Phase.ModeSelect;
        bool forge = phase == DuelManager.Phase.Forge;

        _canvas.enabled = mode || forge;
        _modeRoot.gameObject.SetActive(mode);
        _forgeRoot.gameObject.SetActive(forge);

        if (mode)
        {
            _modeIndex[0] = 0;
            _modeIndex[1] = 0;
            UpdateModeCursors();
        }

        if (forge)
        {
            _forgeOwner.text = (_duel.ForgingPlayer == 0 ? "1P" : "2P") + " の剣をつくる";
            _forgeOwner.color = PlayerColors[Mathf.Clamp(_duel.ForgingPlayer, 0, 1)];
            HandleForgeStepChanged(_duel.CurrentForgeStep);
        }
    }

    void HandleModeChanged(int playerIndex, DuelManager.PlayerMode mode)
    {
        UpdateModeCursors();
    }

    void HandleForgeStepChanged(DuelManager.ForgeStep step)
    {
        _qrPanel.gameObject.SetActive(step == DuelManager.ForgeStep.WaitingUpload);
        _forgingPanel.gameObject.SetActive(step == DuelManager.ForgeStep.Forging);
        _revealPanel.gameObject.SetActive(
            step == DuelManager.ForgeStep.Ready ||
            step == DuelManager.ForgeStep.Drawn ||
            step == DuelManager.ForgeStep.Confirm);

        bool waitingForDraw = step == DuelManager.ForgeStep.Ready;
        _drawCommandRoot.gameObject.SetActive(waitingForDraw);
        if (!waitingForDraw)
        {
            _drawCommandStarted = -1f;
            _revealPrompt.rectTransform.localScale = Vector3.one;
            _revealPrompt.rectTransform.localRotation = Quaternion.identity;
        }

        if (step == DuelManager.ForgeStep.WaitingUpload)
        {
            _forgeProgress.text = "1 / 3　QRで友達を登録";
            Texture2D qr = LoadQrTexture();
            _qrView.texture = qr;
            _qrView.color = qr != null ? Color.white : new Color(0.13f, 0.15f, 0.20f);
            _qrMissingNote.enabled = qr == null;
        }
        else if (step == DuelManager.ForgeStep.Forging)
        {
            UiSoundPlayer.Forge(_sfxSource);
            _forgeProgress.text = "2 / 3　友達の剣を錬成中";
            _forgingText.SetText("剣 を 錬 成 中 . . .");
        }
        else if (step == DuelManager.ForgeStep.Ready)
        {
            _forgeProgress.text = "3 / 3　剣を引き抜け！";
            // まだ剣は見せない。抜くまでが溜め
            _revealHeadline.SetText("錬 成 完 了");
            _drawCommandText.SetText("剣 を 抜 け ！");
            _revealSword.gameObject.SetActive(false);
            _revealName.text = "";
            _revealStats.text = "";
            _revealPrompt.text = "× ボタンで引き抜く";
            _drawStarted = -1f;
            _drawCommandStarted = Time.unscaledTime;
            SetRevealFlash(0f);
        }
        else if (step == DuelManager.ForgeStep.Drawn)
        {
            UiSoundPlayer.DrawSword(_sfxSource);
            _forgeProgress.text = "NEW FRIEND!";
            SwordData sword = _duel.ForgedSword;
            _revealHeadline.SetText("新しい友達の出現！");

            Texture2D texture = _duel.GetTexture(sword);
            _revealSword.gameObject.SetActive(true);
            _revealSword.texture = texture;

            if (texture != null && texture.height > 0)
            {
                float aspect = texture.width / (float)texture.height;
                float height = 610f;
                float width = height * aspect;
                const float maxWidth = 660f;
                if (width > maxWidth)
                {
                    height *= maxWidth / width;
                    width = maxWidth;
                }
                _revealSword.rectTransform.sizeDelta = new Vector2(width, height);
            }

            _revealName.text = sword != null ? sword.name : "";
            _revealStats.text = sword != null && sword.stats != null
                ? $"攻撃力 ATTACK  {sword.stats.attack}    素早さ SPEED  {sword.stats.speed}    サイズ SIZE  {TposeSwordTemplateSettings.ResolveHeightCm(sword):0} cm"
                : "";
            _revealPrompt.text = "×  つぎへ";

            _drawStarted = Time.unscaledTime;
            _revealSword.rectTransform.anchoredPosition = new Vector2(0f, -680f);
            _revealSword.rectTransform.localScale = new Vector3(0.35f, 0.35f, 1f);
            _revealSword.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -10f);

            AudioClip voice = _duel.GetVoice(sword);
            if (voice != null)
            {
                _voiceSource.clip = voice;
                _voiceSource.Play();
            }
        }
        else if (step == DuelManager.ForgeStep.Confirm)
        {
            _forgeProgress.text = "READY TO FIGHT";
            _revealHeadline.SetText("この剣で戦う？");
            _revealPrompt.text = "×  この剣で戦う          ○  既存の武器からえらぶ";
        }
    }

    Texture2D LoadQrTexture()
    {
        if (qrImage != null) return qrImage;

        string path = Path.Combine(Application.streamingAssetsPath, qrFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[ForgeUI] QR画像が無い: {path} / Inspectorの qrImage に差してもよい");
            return null;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Debug.LogWarning("[ForgeUI] QR画像を読めなかった: " + path);
            return null;
        }

        // QRはドットがぼやけると読めなくなるので補間しない
        texture.filterMode = FilterMode.Point;
        qrImage = texture;
        return texture;
    }

    // ---------- 入力 ----------

    void Update()
    {
        if (_duel == null) return;

        if (_duel.Current == DuelManager.Phase.ModeSelect)
        {
            UpdateModeSelectInput();
        }
        else if (_duel.Current == DuelManager.Phase.Forge)
        {
            UpdateForgeInput();
            UpdateForgingAnimation();
            UpdateDrawCommandAnimation();
            UpdateRevealAnimation();
        }
    }

    void BuildDrawCommand()
    {
        var rootObject = new GameObject("DrawCommand", typeof(RectTransform));
        _drawCommandRoot = rootObject.GetComponent<RectTransform>();
        _drawCommandRoot.SetParent(_revealPanel, false);
        _drawCommandRoot.anchorMin = _drawCommandRoot.anchorMax = new Vector2(0.5f, 0.5f);
        _drawCommandRoot.sizeDelta = new Vector2(1500f, 390f);
        _drawCommandRoot.anchoredPosition = new Vector2(0f, 20f);

        _drawCommandGlow = CreateRawImage(_drawCommandRoot, "CommandGlow", new Color(1f, 0.46f, 0.08f, 0.20f));
        _drawCommandGlow.rectTransform.anchorMin = _drawCommandGlow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _drawCommandGlow.rectTransform.sizeDelta = new Vector2(1050f, 185f);
        _drawCommandGlow.rectTransform.anchoredPosition = new Vector2(0f, 30f);

        for (int i = 0; i < _drawCommandSlashes.Length; i++)
        {
            Color color = i % 2 == 0
                ? new Color(1f, 0.76f, 0.16f, 0.72f)
                : new Color(1f, 1f, 1f, 0.56f);
            RawImage slash = CreateRawImage(_drawCommandRoot, "CommandSlash" + i, color);
            RectTransform rect = slash.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1260f - i * 95f, i % 2 == 0 ? 13f : 7f);
            rect.anchoredPosition = new Vector2(0f, 30f + (i - 1.5f) * 28f);
            rect.localEulerAngles = new Vector3(0f, 0f, i % 2 == 0 ? 7f + i * 2f : -9f - i * 2f);
            _drawCommandSlashes[i] = slash;
        }

        _drawCommandEchoB = CreateText(_drawCommandRoot, "CommandEchoB", 112, TextAnchor.MiddleCenter, new Color(1f, 0.18f, 0.05f, 0.10f));
        _drawCommandEchoB.rectTransform.anchoredPosition = new Vector2(0f, 34f);
        _drawCommandEchoB.text = "剣を抜け！";

        _drawCommandEchoA = CreateText(_drawCommandRoot, "CommandEchoA", 112, TextAnchor.MiddleCenter, new Color(1f, 0.74f, 0.12f, 0.16f));
        _drawCommandEchoA.rectTransform.anchoredPosition = new Vector2(0f, 34f);
        _drawCommandEchoA.text = "剣を抜け！";

        var textRoot = new GameObject("CommandText", typeof(RectTransform)).GetComponent<RectTransform>();
        textRoot.SetParent(_drawCommandRoot, false);
        textRoot.anchorMin = textRoot.anchorMax = new Vector2(0.5f, 0.5f);
        textRoot.anchoredPosition = new Vector2(0f, 34f);
        _drawCommandText = textRoot.gameObject.AddComponent<WavyText>();
        _drawCommandText.fontSize = 104;
        _drawCommandText.waveHeight = 18f;
        _drawCommandText.waveSpeed = 7.8f;
        _drawCommandText.wavePhase = 0.62f;
        _drawCommandText.revealInterval = 0.045f;
        _drawCommandText.colorA = Color.white;
        _drawCommandText.colorB = new Color(1f, 0.52f, 0.08f);

        Text button = CreateText(_drawCommandRoot, "DrawButton", 28, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.62f));
        button.rectTransform.anchoredPosition = new Vector2(0f, -116f);
        button.text = "PRESS  ×  BUTTON";

        _drawCommandRoot.gameObject.SetActive(false);
    }

    void UpdateDrawCommandAnimation()
    {
        if (_drawCommandStarted < 0f ||
            _duel.CurrentForgeStep != DuelManager.ForgeStep.Ready ||
            !_drawCommandRoot.gameObject.activeSelf)
        {
            return;
        }

        float elapsed = Time.unscaledTime - _drawCommandStarted;
        float introScale;
        if (elapsed < 0.18f)
        {
            introScale = Mathf.Lerp(2.35f, 0.82f, Mathf.SmoothStep(0f, 1f, elapsed / 0.18f));
        }
        else if (elapsed < 0.36f)
        {
            introScale = Mathf.Lerp(0.82f, 1.16f, Mathf.SmoothStep(0f, 1f, (elapsed - 0.18f) / 0.18f));
        }
        else if (elapsed < 0.54f)
        {
            introScale = Mathf.Lerp(1.16f, 1f, Mathf.SmoothStep(0f, 1f, (elapsed - 0.36f) / 0.18f));
        }
        else
        {
            introScale = 1f + Mathf.Sin(elapsed * 4.8f) * 0.055f;
        }

        float entryShake = Mathf.Exp(-elapsed * 3.6f) * Mathf.Sin(elapsed * 58f) * 7f;
        float liveTilt = Mathf.Sin(elapsed * 2.5f) * 1.35f;
        _drawCommandRoot.localScale = Vector3.one * introScale;
        _drawCommandRoot.localRotation = Quaternion.Euler(0f, 0f, entryShake + liveTilt);
        _drawCommandRoot.anchoredPosition = new Vector2(0f, 20f + Mathf.Sin(elapsed * 3.4f) * 8f);

        float beat = (Mathf.Sin(elapsed * 5.2f) + 1f) * 0.5f;
        _drawCommandGlow.color = new Color(1f, 0.40f, 0.06f, Mathf.Lerp(0.10f, 0.31f, beat));
        _drawCommandGlow.rectTransform.localScale = new Vector3(
            Mathf.Lerp(0.92f, 1.16f, beat),
            Mathf.Lerp(0.86f, 1.08f, beat),
            1f);

        for (int i = 0; i < _drawCommandSlashes.Length; i++)
        {
            float phase = (Mathf.Sin(elapsed * (5.8f + i * 0.35f) + i * 1.4f) + 1f) * 0.5f;
            _drawCommandSlashes[i].rectTransform.localScale = new Vector3(Mathf.Lerp(0.55f, 1.12f, phase), 1f, 1f);
            Color color = _drawCommandSlashes[i].color;
            color.a = (i % 2 == 0 ? 0.76f : 0.58f) * Mathf.Lerp(0.45f, 1f, phase);
            _drawCommandSlashes[i].color = color;
        }

        float echo = Mathf.Repeat(elapsed * 0.78f, 1f);
        _drawCommandEchoA.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.22f, echo);
        _drawCommandEchoB.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.05f, 1.38f, echo);
        _drawCommandEchoA.color = new Color(1f, 0.72f, 0.10f, (1f - echo) * 0.22f);
        _drawCommandEchoB.color = new Color(1f, 0.16f, 0.04f, (1f - echo) * 0.14f);

        _drawCommandText.colorA = Color.Lerp(Color.white, new Color(1f, 0.90f, 0.38f), beat);
        _drawCommandText.colorB = Color.Lerp(new Color(1f, 0.34f, 0.04f), new Color(1f, 0.68f, 0.08f), beat);

        float promptPulse = 1f + Mathf.Sin(elapsed * 5.2f) * 0.08f;
        _revealPrompt.rectTransform.localScale = Vector3.one * promptPulse;
        _revealPrompt.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -liveTilt * 0.45f);
        _revealPrompt.color = Color.Lerp(new Color(1f, 0.70f, 0.18f), Color.white, beat * 0.55f);
    }

    void UpdateRevealAnimation()
    {
        if (_drawStarted < 0f || _duel.CurrentForgeStep != DuelManager.ForgeStep.Drawn) return;

        float elapsed = Time.unscaledTime - _drawStarted;
        float pull = Mathf.Clamp01(elapsed / 1.15f);
        float eased = 1f - Mathf.Pow(1f - pull, 3f);
        float overshoot = Mathf.Sin(pull * Mathf.PI) * 42f;

        _revealSword.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(-680f, -5f, eased) + overshoot);
        float scale = Mathf.Lerp(0.35f, 1f, eased);
        _revealSword.rectTransform.localScale = new Vector3(scale, scale, 1f);
        _revealSword.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-10f, 0f, eased));

        float flash = elapsed < 0.75f
            ? Mathf.Sin(Mathf.Clamp01(elapsed / 0.75f) * Mathf.PI)
            : Mathf.Clamp01(1.6f - elapsed);
        SetRevealFlash(flash * 0.55f);

        if (pull >= 1f && elapsed > 1.6f) SetRevealFlash(0f);
    }

    void SetRevealFlash(float alpha)
    {
        if (_revealFlash == null) return;
        Color color = _revealFlash.color;
        color.a = alpha;
        _revealFlash.color = color;
        float pulse = 0.85f + alpha * 0.35f;
        _revealFlash.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
    }

    void UpdateForgingAnimation()
    {
        if (_forgingRing == null || !_forgingPanel.gameObject.activeSelf) return;

        // 帯を横に伸び縮みさせて、鍛えている感じを出す
        float t = Mathf.PingPong(Time.unscaledTime * 0.9f, 1f);
        float width = Mathf.Lerp(220f, 620f, Mathf.SmoothStep(0f, 1f, t));
        _forgingRing.sizeDelta = new Vector2(width, 12f);

        var image = _forgingRing.GetComponent<RawImage>();
        Color color = image.color;
        color.a = Mathf.Lerp(0.18f, 0.5f, t);
        image.color = color;
    }

    void UpdateModeSelectInput()
    {
        for (int player = 0; player < 2; player++)
        {
            ReadInput(player, out int step, out bool decide, out bool cancel);

            if (cancel && _duel.GetMode(player) != DuelManager.PlayerMode.Undecided)
            {
                UiSoundPlayer.Cancel(_sfxSource);
                _duel.CancelModeSelection(player);
                continue;
            }

            if (_duel.GetMode(player) != DuelManager.PlayerMode.Undecided) continue;

            if (step != 0)
            {
                _modeIndex[player] = Mathf.Clamp(_modeIndex[player] + step, 0, ModeLabels.Length - 1);
                UiSoundPlayer.Move(_sfxSource);
                UpdateModeCursors();
            }

            if (decide)
            {
                UiSoundPlayer.Confirm(_sfxSource);
                _duel.SetMode(player, _modeIndex[player] == 0
                    ? DuelManager.PlayerMode.Existing
                    : DuelManager.PlayerMode.Create);
            }
        }
    }

    void UpdateForgeInput()
    {
        int player = _duel.ForgingPlayer;
        if (player < 0 || player >= 2) return;

        ReadInput(player, out _, out bool decide, out bool cancel);

        switch (_duel.CurrentForgeStep)
        {
            case DuelManager.ForgeStep.Ready:
            case DuelManager.ForgeStep.Drawn:
                if (decide)
                {
                    UiSoundPlayer.Confirm(_sfxSource);
                    _duel.AdvanceForge();
                }
                break;

            case DuelManager.ForgeStep.Confirm:
                if (decide)
                {
                    UiSoundPlayer.Confirm(_sfxSource);
                    _duel.ConfirmForgedSword();
                }
                else if (cancel)
                {
                    UiSoundPlayer.Cancel(_sfxSource);
                    _duel.RejectForgedSword();
                }
                break;
        }
    }

    /// <summary>step は上下（上が -1）。SwordSelectUI と同じ割り当て。</summary>
    void ReadInput(int playerIndex, out int step, out bool decide, out bool cancel)
    {
        step = 0;
        decide = false;
        cancel = false;

        var pads = Gamepad.all;
        if (playerIndex < pads.Count)
        {
            Gamepad pad = pads[playerIndex];

            if (pad.dpad.up.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame) step = -1;
            if (pad.dpad.down.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame) step = 1;

            float y = pad.leftStick.y.ReadValue();
            if (Mathf.Abs(y) > 0.6f)
            {
                if (!_stickLatched[playerIndex])
                {
                    step = y > 0f ? -1 : 1;
                    _stickLatched[playerIndex] = true;
                }
            }
            else if (Mathf.Abs(y) < 0.3f)
            {
                _stickLatched[playerIndex] = false;
            }

            decide = pad.buttonSouth.wasPressedThisFrame;
            cancel = pad.buttonEast.wasPressedThisFrame;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (playerIndex == 0)
        {
            if (kb.aKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) step = -1;
            if (kb.dKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) step = 1;
            decide = kb.fKey.wasPressedThisFrame;
            cancel = kb.gKey.wasPressedThisFrame;
        }
        else if (playerIndex == 1)
        {
            if (kb.leftArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) step = -1;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) step = 1;
            decide = kb.periodKey.wasPressedThisFrame;
            cancel = kb.slashKey.wasPressedThisFrame;
        }
    }

    // ---------- uGUI の小道具 ----------

    RectTransform CreatePanel(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rect = go.GetComponent<RectTransform>();
        Stretch(rect);
        return rect;
    }

    static RectTransform CreateSubPanel(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        Stretch(rect);
        return rect;
    }

    static void CreateBackdrop(Transform parent)
    {
        var background = CreateRawImage(parent, "DigitalForgeBackground", Color.white);
        background.texture = Resources.Load<Texture2D>("UI/DigitalForgeBackground");
        background.uvRect = new Rect(0.062f, 0f, 0.876f, 1f);
        Stretch(background.rectTransform);

        var veil = CreateRawImage(parent, "Dim", new Color(0.006f, 0.012f, 0.025f, 0.52f));
        Stretch(veil.rectTransform);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Image は sprite 未設定だと描画されないので RawImage を使う。</summary>
    static RawImage CreateRawImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1400f, 140f);

        var text = go.AddComponent<Text>();
        text.font = GetFont();
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.60f);
        outline.effectDistance = new Vector2(1f, -1f);

        return text;
    }

    static Font GetFont()
    {
        if (_font == null)
        {
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 48);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }
}
