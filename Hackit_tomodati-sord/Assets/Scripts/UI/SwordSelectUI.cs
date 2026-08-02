using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Local APIから取得した人物画像を、1P/2Pそれぞれ大きく見せる武器選択画面。
/// 左右で候補を切り替え、決定したSwordDataとTexture2Dを対戦へ渡す。
/// </summary>
public class SwordSelectUI : MonoBehaviour
{
    static readonly Color[] PlayerColors =
    {
        new Color(0.18f, 0.55f, 1f),
        new Color(1f, 0.25f, 0.20f),
    };

    static readonly string[] StatLabels = { "ATTACK", "SPEED", "SIZE" };

    DuelManager _duel;
    Canvas _canvas;
    Text _title;
    Text _footer;
    readonly RectTransform[] _panels = new RectTransform[2];
    readonly RawImage[] _panelBackgrounds = new RawImage[2];
    readonly RawImage[] _portraitBackgrounds = new RawImage[2];
    readonly RawImage[] _portraits = new RawImage[2];
    readonly AspectRatioFitter[] _portraitFitters = new AspectRatioFitter[2];
    readonly Text[] _names = new Text[2];
    readonly Text[] _states = new Text[2];
    readonly RawImage[,] _barFills = new RawImage[2, 3];
    readonly Text[,] _barLabels = new Text[2, 3];
    readonly float[,] _targetBarRatios = new float[2, 3];
    readonly int[] _cursor = new int[2];
    readonly bool[] _stickLatched = new bool[2];
    readonly float[] _selectionBumpUntil = new float[2];
    AudioSource _sfxSource;
    static Font _font;

    public static SwordSelectUI Create(DuelManager duel)
    {
        var go = new GameObject("SwordSelectUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var ui = go.AddComponent<SwordSelectUI>();
        ui.Init(duel);
        return ui;
    }

    void Init(DuelManager duel)
    {
        _duel = duel;
        _sfxSource = UiSoundPlayer.AddSource(gameObject);
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;

        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildScreen();
        _duel.OnPhaseChanged += HandlePhaseChanged;
        _duel.OnSelectionChanged += HandleSelectionChanged;
        HandlePhaseChanged(_duel.Current);
    }

    void OnDestroy()
    {
        if (_duel == null) return;
        _duel.OnPhaseChanged -= HandlePhaseChanged;
        _duel.OnSelectionChanged -= HandleSelectionChanged;
    }

    void BuildScreen()
    {
        RawImage background = CreateRawImage(transform, "DigitalForgeBackground", Color.white);
        background.texture = Resources.Load<Texture2D>("UI/DigitalForgeBackground");
        background.uvRect = new Rect(0.062f, 0f, 0.876f, 1f);
        Stretch(background.rectTransform);

        RawImage veil = CreateRawImage(transform, "BackgroundVeil", new Color(0.008f, 0.015f, 0.03f, 0.42f));
        Stretch(veil.rectTransform);

        RawImage titlePlate = CreateRawImage(transform, "TitlePlate", new Color(0.018f, 0.028f, 0.048f, 0.90f));
        SetRect(titlePlate.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 105f));
        titlePlate.rectTransform.pivot = new Vector2(0.5f, 1f);

        Text section = CreateText(transform, "Section", 17, TextAnchor.MiddleCenter, new Color(0.54f, 0.64f, 0.74f));
        SetRect(section.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -9f), new Vector2(0f, 26f));
        section.rectTransform.pivot = new Vector2(0.5f, 1f);
        section.text = "TOMODACHI SWORD  //  ARMORY";

        _title = CreateText(transform, "Title", 48, TextAnchor.MiddleCenter, new Color(0.94f, 0.96f, 1f));
        SetRect(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -31f), new Vector2(0f, 64f));
        _title.rectTransform.pivot = new Vector2(0.5f, 1f);

        RawImage divider = CreateRawImage(transform, "Divider", new Color(0.85f, 0.55f, 0.20f, 0.34f));
        SetRect(divider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(2f, 790f));

        BuildPlayerPanel(0, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
        BuildPlayerPanel(1, new Vector2(0.5f, 0f), new Vector2(1f, 1f));

        RawImage footerPlate = CreateRawImage(transform, "FooterPlate", new Color(0.012f, 0.022f, 0.04f, 0.92f));
        SetRect(footerPlate.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 74f));
        footerPlate.rectTransform.pivot = new Vector2(0.5f, 0f);

        _footer = CreateText(transform, "Footer", 22, TextAnchor.MiddleCenter, new Color(0.68f, 0.76f, 0.84f));
        SetRect(_footer.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 14f), new Vector2(0f, 58f));
        _footer.rectTransform.pivot = new Vector2(0.5f, 0f);
    }

    void BuildPlayerPanel(int player, Vector2 anchorMin, Vector2 anchorMax)
    {
        var panelGo = new GameObject($"Player{player + 1}", typeof(RectTransform));
        panelGo.transform.SetParent(transform, false);
        RectTransform panel = panelGo.GetComponent<RectTransform>();
        panel.anchorMin = anchorMin;
        panel.anchorMax = anchorMax;
        panel.offsetMin = new Vector2(42f, 95f);
        panel.offsetMax = new Vector2(-42f, -115f);
        _panels[player] = panel;

        RawImage panelFrame = CreateRawImage(panel, "PanelFrame", new Color(PlayerColors[player].r, PlayerColors[player].g, PlayerColors[player].b, 0.48f));
        Stretch(panelFrame.rectTransform);
        panelFrame.transform.SetAsFirstSibling();

        RawImage panelBg = CreateRawImage(panel, "PanelBackground", new Color(0.018f, 0.032f, 0.055f, 0.91f));
        StretchWithMargin(panelBg.rectTransform, 2f);
        panelBg.transform.SetSiblingIndex(1);
        _panelBackgrounds[player] = panelBg;

        RawImage sideAccent = CreateRawImage(panel, "PlayerAccent", PlayerColors[player]);
        sideAccent.rectTransform.anchorMin = new Vector2(0f, 1f);
        sideAccent.rectTransform.anchorMax = new Vector2(1f, 1f);
        sideAccent.rectTransform.pivot = new Vector2(0.5f, 1f);
        sideAccent.rectTransform.anchoredPosition = Vector2.zero;
        sideAccent.rectTransform.sizeDelta = new Vector2(0f, 7f);

        Text playerLabel = CreateText(panel, "PlayerLabel", 38, TextAnchor.MiddleCenter, PlayerColors[player]);
        SetRect(playerLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 54f));
        playerLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        playerLabel.text = $"PLAYER {player + 1}";

        _names[player] = CreateText(panel, "SwordName", 35, TextAnchor.MiddleCenter, Color.white);
        SetRect(_names[player].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), new Vector2(0f, 48f));

        RawImage portraitBg = CreateRawImage(panel, "PortraitBackground", new Color(0.025f, 0.045f, 0.075f, 0.96f));
        SetRect(portraitBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(650f, 430f));
        portraitBg.rectTransform.pivot = new Vector2(0.5f, 1f);
        _portraitBackgrounds[player] = portraitBg;

        RawImage portrait = CreateRawImage(portraitBg.rectTransform, "PersonImage", Color.white);
        StretchWithMargin(portrait.rectTransform, 18f);
        _portraits[player] = portrait;
        AspectRatioFitter fitter = portrait.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _portraitFitters[player] = fitter;

        Text arrows = CreateText(panel, "Arrows", 62, TextAnchor.MiddleCenter, Color.white);
        SetRect(arrows.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -310f), new Vector2(0f, 70f));
        arrows.text = "〈                                      〉";

        for (int stat = 0; stat < 3; stat++)
        {
            float y = -590f - stat * 82f;
            Text label = CreateText(panel, StatLabels[stat], 27, TextAnchor.MiddleLeft, new Color(0.84f, 0.89f, 0.95f));
            SetRect(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(560f, 38f));
            _barLabels[player, stat] = label;

            RawImage track = CreateRawImage(panel, StatLabels[stat] + "Track", new Color(0.12f, 0.16f, 0.21f, 0.92f));
            SetRect(track.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y - 32f), new Vector2(560f, 17f));

            RawImage fill = CreateRawImage(track.rectTransform, "Fill", PlayerColors[player]);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            _barFills[player, stat] = fill;
        }

        _states[player] = CreateText(panel, "State", 27, TextAnchor.MiddleCenter, PlayerColors[player]);
        SetRect(_states[player].rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 40f));
        _states[player].rectTransform.pivot = new Vector2(0.5f, 0f);
    }

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        _canvas.enabled = phase == DuelManager.Phase.Loading || phase == DuelManager.Phase.Select;
        if (phase == DuelManager.Phase.Loading)
        {
            _title.text = "ともだちを読み込み中...";
            _footer.text = "しばらくお待ちください";
            return;
        }

        if (phase != DuelManager.Phase.Select) return;
        _title.text = "使用する剣をえらべ";
        int count = _duel.Swords.Count;
        _cursor[0] = 0;
        _cursor[1] = Mathf.Min(1, Mathf.Max(0, count - 1));
        RefreshAll();
    }

    void HandleSelectionChanged(int playerIndex, SwordData data) => RefreshAll();

    void RefreshAll()
    {
        int count = _duel.Swords.Count;
        if (count == 0)
        {
            _footer.text = "使用できる剣がありません";
            return;
        }

        for (int player = 0; player < 2; player++)
        {
            _cursor[player] = Mathf.Clamp(_cursor[player], 0, count - 1);
            SwordData sword = _duel.IsSelected(player) ? _duel.GetSelected(player) : _duel.Swords[_cursor[player]];
            ShowSword(player, sword);
            _states[player].text = _duel.IsSelected(player) ? "READY!　○で選び直す" : $"{_cursor[player] + 1} / {count}　×で決定";
            _states[player].color = _duel.IsSelected(player) ? new Color(1f, 0.84f, 0.25f) : PlayerColors[player];

            Color panelColor = _duel.IsSelected(player)
                ? new Color(0.12f, 0.105f, 0.045f, 0.98f)
                : new Color(0.045f, 0.065f, 0.11f, 0.96f);
            _panelBackgrounds[player].color = panelColor;
        }

        _footer.text = "十字キー ← →：選択　　×：決定　　○：取り消し";
    }

    void ShowSword(int player, SwordData sword)
    {
        if (sword == null || sword.stats == null) return;
        _portraits[player].texture = _duel.GetTexture(sword);
        Texture texture = _portraits[player].texture;
        _portraitFitters[player].aspectRatio = texture != null && texture.height > 0
            ? texture.width / (float)texture.height
            : 0.5f;
        _names[player].text = sword.name;
        float[] values = { sword.stats.attack, sword.stats.speed, TposeSwordTemplateSettings.ResolveHeightCm(sword) };
        float[] maxima = { 70f, 70f, TposeSwordTemplateSettings.MaximumSupportedHeightCm };
        for (int stat = 0; stat < 3; stat++)
        {
            float ratio = Mathf.Clamp01(values[stat] / maxima[stat]);
            _targetBarRatios[player, stat] = ratio;
            string value = stat == 2 ? $"{values[stat]:0} cm" : Mathf.RoundToInt(values[stat]).ToString();
            _barLabels[player, stat].text = $"{StatLabels[stat]}    {value}";
        }
    }

    void Update()
    {
        if (_duel == null || _duel.Current != DuelManager.Phase.Select || _duel.Swords.Count == 0) return;
        for (int player = 0; player < 2; player++)
        {
            ReadInput(player, out int step, out bool decide, out bool cancel);
            if (!_duel.IsSelected(player) && step != 0)
            {
                int count = _duel.Swords.Count;
                _cursor[player] = (_cursor[player] + step + count) % count;
                _selectionBumpUntil[player] = Time.unscaledTime + 0.16f;
                UiSoundPlayer.Move(_sfxSource);
                RefreshAll();
            }
            if (decide && !_duel.IsSelected(player))
            {
                UiSoundPlayer.Confirm(_sfxSource);
                _duel.SelectSword(player, _duel.Swords[_cursor[player]]);
            }
            else if (cancel && _duel.IsSelected(player))
            {
                UiSoundPlayer.Cancel(_sfxSource);
                _duel.CancelSelection(player);
            }
        }

        AnimateFeedback();
    }

    void AnimateFeedback()
    {
        float dt = Time.unscaledDeltaTime;
        for (int player = 0; player < 2; player++)
        {
            bool bumped = Time.unscaledTime < _selectionBumpUntil[player];
            bool ready = _duel.IsSelected(player);
            float pulse = ready ? 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.008f : 1f;
            float targetScale = bumped ? 1.025f : pulse;
            _panels[player].localScale = Vector3.Lerp(
                _panels[player].localScale,
                new Vector3(targetScale, targetScale, 1f),
                1f - Mathf.Exp(-18f * dt));

            Color portraitColor = ready
                ? new Color(0.24f, 0.20f, 0.07f)
                : Color.Lerp(new Color(0.065f, 0.085f, 0.14f), PlayerColors[player] * 0.34f, 0.28f);
            _portraitBackgrounds[player].color = Color.Lerp(
                _portraitBackgrounds[player].color,
                portraitColor,
                1f - Mathf.Exp(-10f * dt));

            for (int stat = 0; stat < 3; stat++)
            {
                RectTransform fill = _barFills[player, stat].rectTransform;
                float shown = Mathf.Lerp(fill.anchorMax.x, _targetBarRatios[player, stat], 1f - Mathf.Exp(-12f * dt));
                fill.anchorMax = new Vector2(shown, 1f);
            }
        }
    }

    void ReadInput(int player, out int step, out bool decide, out bool cancel)
    {
        step = 0; decide = false; cancel = false;
        if (player < Gamepad.all.Count)
        {
            Gamepad pad = Gamepad.all[player];
            if (pad.dpad.left.wasPressedThisFrame) step = -1;
            if (pad.dpad.right.wasPressedThisFrame) step = 1;
            float axis = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(axis) > 0.6f && !_stickLatched[player])
            {
                step = axis < 0f ? -1 : 1;
                _stickLatched[player] = true;
            }
            else if (Mathf.Abs(axis) < 0.3f) _stickLatched[player] = false;
            decide = pad.buttonSouth.wasPressedThisFrame;
            cancel = pad.buttonEast.wasPressedThisFrame;
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (player == 0)
        {
            if (kb.aKey.wasPressedThisFrame) step = -1;
            if (kb.dKey.wasPressedThisFrame) step = 1;
            decide = kb.fKey.wasPressedThisFrame;
            cancel = kb.gKey.wasPressedThisFrame;
        }
        else
        {
            if (kb.leftArrowKey.wasPressedThisFrame) step = -1;
            if (kb.rightArrowKey.wasPressedThisFrame) step = 1;
            decide = kb.periodKey.wasPressedThisFrame;
            cancel = kb.slashKey.wasPressedThisFrame;
        }
    }

    static RawImage CreateRawImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RawImage image = go.GetComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(Transform parent, string name, int size, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = GetFont(); text.fontSize = size; text.fontStyle = FontStyle.Bold;
        text.alignment = alignment; text.color = color; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.58f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
    }

    static void StretchWithMargin(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(margin, margin); rect.offsetMax = new Vector2(-margin, -margin);
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
