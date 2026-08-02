using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// SQLiteから復元した人物画像を、1P/2Pそれぞれ大きく見せる武器選択画面。
/// 左右で候補を切り替え、決定したSwordDataとTexture2Dを対戦へ渡す。
/// </summary>
public class SwordSelectUI : MonoBehaviour
{
    static readonly Color[] PlayerColors =
    {
        new Color(0.18f, 0.55f, 1f),
        new Color(1f, 0.25f, 0.20f),
    };

    DuelManager _duel;
    Canvas _canvas;
    Text _title;
    Text _footer;
    readonly RectTransform[] _panels = new RectTransform[2];
    readonly RawImage[] _portraits = new RawImage[2];
    readonly AspectRatioFitter[] _portraitFitters = new AspectRatioFitter[2];
    readonly Text[] _names = new Text[2];
    readonly Text[] _states = new Text[2];
    readonly RawImage[,] _barFills = new RawImage[2, 3];
    readonly Text[,] _barValues = new Text[2, 3];
    readonly int[] _cursor = new int[2];
    readonly bool[] _stickLatched = new bool[2];
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
        RawImage dim = CreateRawImage(transform, "Background", new Color(0.025f, 0.035f, 0.075f, 0.99f));
        Stretch(dim.rectTransform);

        _title = CreateText(transform, "Title", 56, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.28f));
        SetRect(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -24f), new Vector2(0f, 74f));
        _title.rectTransform.pivot = new Vector2(0.5f, 1f);

        RawImage divider = CreateRawImage(transform, "Divider", new Color(1f, 0.82f, 0.25f, 0.65f));
        SetRect(divider.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 820f));

        BuildPlayerPanel(0, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
        BuildPlayerPanel(1, new Vector2(0.5f, 0f), new Vector2(1f, 1f));

        _footer = CreateText(transform, "Footer", 25, TextAnchor.MiddleCenter, new Color(0.84f, 0.90f, 1f));
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

        Text playerLabel = CreateText(panel, "PlayerLabel", 38, TextAnchor.MiddleCenter, PlayerColors[player]);
        SetRect(playerLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 54f));
        playerLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        playerLabel.text = $"PLAYER {player + 1}";

        _names[player] = CreateText(panel, "SwordName", 35, TextAnchor.MiddleCenter, Color.white);
        SetRect(_names[player].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), new Vector2(0f, 48f));

        RawImage portraitBg = CreateRawImage(panel, "PortraitBackground", new Color(0.075f, 0.10f, 0.17f));
        SetRect(portraitBg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(650f, 430f));
        portraitBg.rectTransform.pivot = new Vector2(0.5f, 1f);

        RawImage portrait = CreateRawImage(portraitBg.rectTransform, "PersonImage", Color.white);
        StretchWithMargin(portrait.rectTransform, 18f);
        _portraits[player] = portrait;
        AspectRatioFitter fitter = portrait.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _portraitFitters[player] = fitter;

        Text arrows = CreateText(panel, "Arrows", 62, TextAnchor.MiddleCenter, Color.white);
        SetRect(arrows.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -310f), new Vector2(0f, 70f));
        arrows.text = "〈                                      〉";

        string[] labels = { "攻撃力  ATTACK", "素早さ  SPEED", "サイズ  SIZE" };
        Color[] colors =
        {
            new Color(1f, 0.28f, 0.20f),
            new Color(0.22f, 0.95f, 0.48f),
            new Color(1f, 0.78f, 0.20f),
        };

        for (int stat = 0; stat < 3; stat++)
        {
            float y = -635f - stat * 62f;
            Text label = CreateText(panel, labels[stat], 24, TextAnchor.MiddleLeft, Color.white);
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(100f, y), new Vector2(190f, 34f));

            RawImage track = CreateRawImage(panel, labels[stat] + "Track", new Color(0.18f, 0.20f, 0.26f));
            SetRect(track.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(305f, y), new Vector2(272f, 24f));
            track.rectTransform.pivot = new Vector2(0f, 0.5f);

            RawImage fill = CreateRawImage(track.rectTransform, "Fill", colors[stat]);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            _barFills[player, stat] = fill;

            Text value = CreateText(panel, labels[stat] + "Value", 23, TextAnchor.MiddleRight, Color.white);
            SetRect(value.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-72f, y), new Vector2(90f, 34f));
            _barValues[player, stat] = value;
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
            _title.text = "SQLiteから剣を読み込み中...";
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
        float[] values = { sword.stats.attack, sword.stats.speed, sword.stats.reach };
        float[] maxima = { 70f, 70f, 1.5f };
        for (int stat = 0; stat < 3; stat++)
        {
            float ratio = Mathf.Clamp01(values[stat] / maxima[stat]);
            _barFills[player, stat].rectTransform.anchorMax = new Vector2(ratio, 1f);
            _barValues[player, stat].text = stat == 2 ? values[stat].ToString("0.0") : Mathf.RoundToInt(values[stat]).ToString();
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
                RefreshAll();
            }
            if (decide && !_duel.IsSelected(player)) _duel.SelectSword(player, _duel.Swords[_cursor[player]]);
            else if (cancel && _duel.IsSelected(player)) _duel.CancelSelection(player);
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
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
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
