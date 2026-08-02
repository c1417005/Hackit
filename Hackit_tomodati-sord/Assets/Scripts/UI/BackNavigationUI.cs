using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 初期画面以外に共通表示する戻るボタン。
/// マウスクリック、PS4のOPTIONS、キーボードのEscに対応する。
/// </summary>
public sealed class BackNavigationUI : MonoBehaviour
{
    DuelManager _duel;
    Canvas _canvas;
    RectTransform _buttonRect;
    Button _button;
    Text _label;
    AudioSource _sfxSource;
    float _pressedUntil;

    static Font _font;

    public static BackNavigationUI Create(DuelManager duel)
    {
        var root = new GameObject(
            "BackNavigationUI",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        BackNavigationUI ui = root.AddComponent<BackNavigationUI>();
        ui.Initialize(duel);
        return ui;
    }

    void Initialize(DuelManager duel)
    {
        _duel = duel;
        _sfxSource = UiSoundPlayer.AddSource(gameObject);

        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildButton();

        if (_duel != null)
        {
            _duel.OnPhaseChanged += HandlePhaseChanged;
            _duel.OnModeChanged += HandleModeChanged;
            HandlePhaseChanged(_duel.Current);
        }
    }

    void OnDestroy()
    {
        if (_duel == null) return;
        _duel.OnPhaseChanged -= HandlePhaseChanged;
        _duel.OnModeChanged -= HandleModeChanged;
    }

    void BuildButton()
    {
        var buttonObject = new GameObject(
            "BackButton",
            typeof(RectTransform),
            typeof(RawImage),
            typeof(Button));
        buttonObject.transform.SetParent(transform, false);

        _buttonRect = buttonObject.GetComponent<RectTransform>();
        _buttonRect.anchorMin = _buttonRect.anchorMax = new Vector2(1f, 0f);
        _buttonRect.pivot = new Vector2(1f, 0f);
        _buttonRect.anchoredPosition = new Vector2(-36f, 30f);
        _buttonRect.sizeDelta = new Vector2(390f, 70f);

        RawImage background = buttonObject.GetComponent<RawImage>();
        background.color = new Color(0.025f, 0.045f, 0.075f, 0.95f);
        background.raycastTarget = true;

        _button = buttonObject.GetComponent<Button>();
        _button.targetGraphic = background;
        _button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = _button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.82f, 0.46f);
        colors.pressedColor = new Color(0.80f, 0.58f, 0.20f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        _button.colors = colors;
        _button.onClick.AddListener(GoBack);

        RawImage accent = CreateRawImage(buttonObject.transform, "Accent", new Color(1f, 0.67f, 0.18f));
        RectTransform accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(7f, 0f);

        _label = CreateText(buttonObject.transform, "Label", 25, TextAnchor.MiddleCenter, Color.white);
        Stretch(_label.rectTransform);
        _label.text = "←  戻る　OPTIONS / ESC";
    }

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        bool visible = _duel != null && _duel.CanReturnToModeSelect;
        _canvas.enabled = visible;
        if (!visible || _label == null) return;

        _label.text = phase switch
        {
            DuelManager.Phase.ModeSelect => "←  選択をやり直す　OPTIONS / ESC",
            DuelManager.Phase.Loading => "←  読み込みをやめる　OPTIONS / ESC",
            DuelManager.Phase.Forge => "←  作成をやめて戻る　OPTIONS / ESC",
            DuelManager.Phase.Select => "←  モード選択へ戻る　OPTIONS / ESC",
            DuelManager.Phase.Battle => "←  対戦をやめて戻る　OPTIONS / ESC",
            DuelManager.Phase.Result => "←  最初に戻る　OPTIONS / ESC",
            _ => "←  戻る　OPTIONS / ESC",
        };
    }

    void HandleModeChanged(int playerIndex, DuelManager.PlayerMode mode)
    {
        HandlePhaseChanged(_duel.Current);
    }

    void Update()
    {
        if (_duel == null || !_canvas.enabled || !_duel.CanReturnToModeSelect) return;

        bool pressed = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad.startButton.wasPressedThisFrame)
            {
                pressed = true;
                break;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 screenPosition = mouse.position.ReadValue();
            if (RectTransformUtility.RectangleContainsScreenPoint(_buttonRect, screenPosition, null))
            {
                pressed = true;
            }
        }

        if (pressed) _button.onClick.Invoke();

        float target = Time.unscaledTime < _pressedUntil ? 0.94f : 1f;
        _buttonRect.localScale = Vector3.Lerp(
            _buttonRect.localScale,
            Vector3.one * target,
            1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
    }

    void GoBack()
    {
        if (_duel == null || !_duel.CanReturnToModeSelect) return;
        _pressedUntil = Time.unscaledTime + 0.12f;
        UiSoundPlayer.Cancel(_sfxSource);
        _duel.ReturnToModeSelect();
        HandlePhaseChanged(_duel.Current);
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
        text.font = GetFont();
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Font GetFont()
    {
        if (_font == null)
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 40);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }
}
