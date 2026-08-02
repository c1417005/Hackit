using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 確殺演出の後に表示する専用の勝利画面。
/// 勝者の戦闘モデルから見た目だけを複製し、専用カメラで大きく回転表示する。
/// </summary>
public sealed class VictoryScreen : MonoBehaviour
{
    static readonly Vector3 PresentationOrigin = new Vector3(10000f, 10000f, 10000f);

    DuelManager _duel;
    Canvas _canvas;
    GameObject _panel;
    CanvasGroup _group;
    RawImage _backgroundAccent;
    RawImage _modelImage;
    RawImage _modelFrame;
    Text _winnerText;
    Text _playerText;
    Text _promptText;

    GameObject _presentationWorld;
    Transform _modelPivot;
    GameObject _model;
    Camera _presentationCamera;
    RenderTexture _renderTexture;

    float _shownAt;
    float _spinAngle;

    static Font _font;

    public static VictoryScreen Create(DuelManager duel)
    {
        var go = new GameObject(
            "VictoryScreen",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        var screen = go.AddComponent<VictoryScreen>();
        screen.Initialize(duel);
        return screen;
    }

    void Initialize(DuelManager duel)
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 300;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildUi();
        Bind(duel);
        _panel.SetActive(false);
    }

    void Bind(DuelManager duel)
    {
        if (_duel != null)
        {
            _duel.OnVictoryPresentation -= Show;
            _duel.OnPhaseChanged -= HandlePhaseChanged;
        }

        _duel = duel;
        if (_duel != null)
        {
            _duel.OnVictoryPresentation += Show;
            _duel.OnPhaseChanged += HandlePhaseChanged;
        }
    }

    void OnDestroy()
    {
        Bind(null);
        DestroyPresentation();
    }

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        if (phase != DuelManager.Phase.Result)
        {
            Hide();
        }
    }

    void Update()
    {
        if (_panel == null || !_panel.activeSelf) return;

        float elapsed = Time.unscaledTime - _shownAt;
        float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.48f));
        _group.alpha = reveal;

        if (_modelImage != null)
        {
            float scale = Mathf.Lerp(0.72f, 1f, reveal);
            _modelImage.rectTransform.localScale = Vector3.one * scale;
        }

        if (_modelPivot != null)
        {
            _spinAngle += 34f * Time.unscaledDeltaTime;
            float tilt = Mathf.Sin(Time.unscaledTime * 1.7f) * 2.2f;
            _modelPivot.localRotation = Quaternion.Euler(tilt, _spinAngle, 0f);
        }

        if (_promptText != null)
        {
            bool ready = _duel != null && _duel.CanLeaveResult;
            _promptText.gameObject.SetActive(ready);
            if (ready)
            {
                Color color = _promptText.color;
                color.a = 0.58f + Mathf.Sin(Time.unscaledTime * 4f) * 0.22f;
                _promptText.color = color;
            }
        }
    }

    void Show(Fighter winner)
    {
        if (winner == null) return;

        Color accent = winner.playerIndex == 0
            ? new Color(0.12f, 0.68f, 1f)
            : new Color(1f, 0.25f, 0.14f);

        string name = winner.Sword != null ? winner.Sword.name : null;
        if (string.IsNullOrEmpty(name))
        {
            name = winner.playerIndex == 0 ? "1P" : "2P";
        }

        _backgroundAccent.color = new Color(accent.r, accent.g, accent.b, 0.34f);
        _modelFrame.color = new Color(accent.r, accent.g, accent.b, 0.30f);
        _winnerText.text = name + " の勝ち！";
        _winnerText.color = Color.Lerp(accent, Color.white, 0.62f);
        _playerText.text = winner.playerIndex == 0 ? "PLAYER 1 WINNER" : "PLAYER 2 WINNER";
        _playerText.color = accent;
        _promptText.gameObject.SetActive(false);

        CreatePresentation(winner);
        _shownAt = Time.unscaledTime;
        _spinAngle = -24f;
        _group.alpha = 0f;
        _panel.SetActive(true);
    }

    void Hide()
    {
        if (_panel != null) _panel.SetActive(false);
        DestroyPresentation();
    }

    void BuildUi()
    {
        _panel = new GameObject("VictoryPanel", typeof(RectTransform), typeof(CanvasGroup));
        _panel.transform.SetParent(transform, false);
        Stretch(_panel.GetComponent<RectTransform>());
        _group = _panel.GetComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        RawImage background = CreateImage(_panel.transform, "Background", new Color(0.012f, 0.018f, 0.050f, 1f));
        Stretch(background.rectTransform);

        _backgroundAccent = CreateImage(_panel.transform, "AccentWash", new Color(0.12f, 0.68f, 1f, 0.34f));
        SetAnchors(_backgroundAccent.rectTransform, new Vector2(0f, 0f), new Vector2(0.52f, 1f));
        _backgroundAccent.rectTransform.localEulerAngles = new Vector3(0f, 0f, -7f);
        _backgroundAccent.rectTransform.localScale = new Vector3(1.25f, 1.25f, 1f);

        BuildBackgroundRays(_panel.transform);

        _modelFrame = CreateImage(_panel.transform, "ModelFrame", new Color(0.12f, 0.68f, 1f, 0.30f));
        SetAnchors(_modelFrame.rectTransform, new Vector2(0.19f, 0.17f), new Vector2(0.81f, 0.78f), 10f);

        RawImage modelBackdrop = CreateImage(_modelFrame.transform, "ModelBackdrop", new Color(0.006f, 0.010f, 0.028f, 0.93f));
        Stretch(modelBackdrop.rectTransform, 8f);

        _modelImage = CreateImage(_modelFrame.transform, "WinnerModel", Color.white);
        Stretch(_modelImage.rectTransform, 20f);

        Text victory = CreateText(_panel.transform, "Victory", "VICTORY", 116, TextAnchor.MiddleCenter, Color.white);
        SetAnchors(victory.rectTransform, new Vector2(0.08f, 0.79f), new Vector2(0.92f, 0.97f));
        AddOutline(victory, new Color(0f, 0f, 0f, 0.92f), 5f);

        _playerText = CreateText(_panel.transform, "PlayerWinner", "PLAYER 1 WINNER", 30, TextAnchor.MiddleCenter, Color.white);
        SetAnchors(_playerText.rectTransform, new Vector2(0.20f, 0.73f), new Vector2(0.80f, 0.80f));

        _winnerText = CreateText(_panel.transform, "WinnerName", "1P の勝ち！", 68, TextAnchor.MiddleCenter, Color.white);
        SetAnchors(_winnerText.rectTransform, new Vector2(0.08f, 0.055f), new Vector2(0.92f, 0.18f));
        AddOutline(_winnerText, new Color(0f, 0f, 0f, 0.94f), 4f);

        _promptText = CreateText(
            _panel.transform,
            "ReturnPrompt",
            "OPTIONS / Esc / 戻るボタンで最初に戻る",
            24,
            TextAnchor.MiddleCenter,
            new Color(1f, 1f, 1f, 0.78f));
        SetAnchors(_promptText.rectTransform, new Vector2(0.12f, 0.01f), new Vector2(0.88f, 0.065f));
    }

    void BuildBackgroundRays(Transform parent)
    {
        const int rayCount = 18;
        for (int i = 0; i < rayCount; i++)
        {
            RawImage ray = CreateImage(parent, "VictoryRay", new Color(1f, 1f, 1f, i % 3 == 0 ? 0.075f : 0.035f));
            RectTransform rect = ray.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.48f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(1250f, i % 3 == 0 ? 16f : 7f);
            rect.localEulerAngles = new Vector3(0f, 0f, i * (360f / rayCount));
        }
    }

    void CreatePresentation(Fighter winner)
    {
        DestroyPresentation();

        _renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32)
        {
            name = "VictoryModelTexture",
            antiAliasing = 4,
            filterMode = FilterMode.Bilinear,
        };
        _renderTexture.Create();
        _modelImage.texture = _renderTexture;

        _presentationWorld = new GameObject("~VictoryPresentation");
        _presentationWorld.transform.position = PresentationOrigin;

        var pivotObject = new GameObject("WinnerSpinPivot");
        _modelPivot = pivotObject.transform;
        _modelPivot.SetParent(_presentationWorld.transform, false);

        _model = winner.CreatePresentationModel(_modelPivot);
        if (_model == null) return;

        Bounds bounds;
        if (!TryGetBounds(_model, out bounds))
        {
            bounds = new Bounds(PresentationOrigin, new Vector3(1.2f, 1.8f, 0.4f));
        }

        Vector3 centerInPivot = _modelPivot.InverseTransformPoint(bounds.center);
        _model.transform.localPosition -= centerInPivot;

        if (!TryGetBounds(_model, out bounds))
        {
            bounds = new Bounds(PresentationOrigin, new Vector3(1.2f, 1.8f, 0.4f));
        }

        var cameraObject = new GameObject("VictoryModelCamera");
        cameraObject.transform.SetParent(_presentationWorld.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
        cameraObject.transform.localRotation = Quaternion.identity;
        _presentationCamera = cameraObject.AddComponent<Camera>();
        _presentationCamera.clearFlags = CameraClearFlags.SolidColor;
        _presentationCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _presentationCamera.orthographic = true;
        _presentationCamera.nearClipPlane = 0.1f;
        _presentationCamera.farClipPlane = 30f;
        _presentationCamera.allowHDR = false;
        _presentationCamera.targetTexture = _renderTexture;

        const float textureAspect = 16f / 9f;
        float halfHeight = Mathf.Max(0.3f, bounds.extents.y);
        float halfWidth = Mathf.Max(0.3f, bounds.extents.x);
        _presentationCamera.orthographicSize = Mathf.Max(halfHeight, halfWidth / textureAspect) * 1.18f;
    }

    void DestroyPresentation()
    {
        if (_presentationCamera != null)
        {
            _presentationCamera.targetTexture = null;
            _presentationCamera.enabled = false;
        }

        if (_presentationWorld != null)
        {
            Destroy(_presentationWorld);
        }

        _presentationWorld = null;
        _presentationCamera = null;
        _modelPivot = null;
        _model = null;

        if (_renderTexture != null)
        {
            if (_modelImage != null && _modelImage.texture == _renderTexture)
            {
                _modelImage.texture = null;
            }

            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }
    }

    static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return true;
    }

    static RawImage CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RawImage image = go.GetComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(Transform parent, string name, string value, int size, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text text = go.GetComponent<Text>();
        text.font = GetFont();
        text.text = value;
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    static void AddOutline(Text text, Color color, float distance)
    {
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    static Font GetFont()
    {
        if (_font != null) return _font;
        _font = Font.CreateDynamicFontFromOSFont(
            new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 72);
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _font;
    }

    static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, float inset = 0f)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    static void Stretch(RectTransform rect, float inset = 0f)
    {
        SetAnchors(rect, Vector2.zero, Vector2.one, inset);
    }
}
