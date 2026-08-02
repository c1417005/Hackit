using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 対戦画面のHUD。両プレイヤーのHPバーを組み立てて Fighter に紐づける。
///
/// uGUI をコードから組んでいる。プレハブを作らないのは、ハッカソン中に
/// シーンとプレハブを両方いじると衝突しやすいため。
/// 見た目を詰める段階になったらプレハブ化して Bind() だけ呼ぶ形に移して良い。
/// </summary>
public class BattleHud : MonoBehaviour
{
    public HpBarUI Player1Bar { get; private set; }
    public HpBarUI Player2Bar { get; private set; }

    Canvas _canvas;
    DuelManager _duel;

    const float BarWidth = 720f;
    const float BarHeight = 54f;
    const float Margin = 56f;

    static Font _font;

    /// <summary>HUDを生成する。fighter は後から Bind() でも良い。</summary>
    public static BattleHud Create(Fighter player1, Fighter player2)
    {
        var canvasGo = new GameObject("BattleHud", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var hud = canvasGo.AddComponent<BattleHud>();
        hud._canvas = canvas;
        hud.Player1Bar = hud.BuildBar(canvasGo.transform, "P1Bar", false, new Color(0.30f, 0.55f, 0.95f));
        hud.Player2Bar = hud.BuildBar(canvasGo.transform, "P2Bar", true, new Color(0.95f, 0.42f, 0.35f));

        hud.Player1Bar.Bind(player1);
        hud.Player2Bar.Bind(player2);

        return hud;
    }

    /// <summary>
    /// DuelManager に紐づけると、対戦中とリザルト中だけ表示されるようになる。
    /// 紐づけなければ常に表示（単体テスト用）。
    /// </summary>
    public void Bind(DuelManager duel)
    {
        if (_duel != null) _duel.OnPhaseChanged -= HandlePhaseChanged;

        _duel = duel;

        if (_duel != null)
        {
            _duel.OnPhaseChanged += HandlePhaseChanged;
            HandlePhaseChanged(_duel.Current);
        }
    }

    void OnDestroy()
    {
        if (_duel != null) _duel.OnPhaseChanged -= HandlePhaseChanged;
    }

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        if (_canvas == null) return;
        _canvas.enabled = phase == DuelManager.Phase.Battle || phase == DuelManager.Phase.Result;
    }

    HpBarUI BuildBar(Transform parent, string name, bool rightAligned, Color accent)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);

        var rect = root.GetComponent<RectTransform>();
        // 上端に寄せる。1P は左、2P は右。
        rect.anchorMin = rect.anchorMax = new Vector2(rightAligned ? 1f : 0f, 1f);
        rect.pivot = new Vector2(rightAligned ? 1f : 0f, 1f);
        rect.anchoredPosition = new Vector2(rightAligned ? -Margin : Margin, -Margin);
        rect.sizeDelta = new Vector2(BarWidth, BarHeight);

        // 枠
        var frame = CreateStretchedImage(root.transform, "Frame", Color.Lerp(accent, Color.white, 0.12f));
        frame.raycastTarget = false;

        // 中身の領域（枠から4pxだけ内側）
        var innerGo = new GameObject("Inner", typeof(RectTransform));
        innerGo.transform.SetParent(root.transform, false);
        var inner = innerGo.GetComponent<RectTransform>();
        inner.anchorMin = Vector2.zero;
        inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(5f, 5f);
        inner.offsetMax = new Vector2(-5f, -5f);

        // 遅れて減る白い帯 → その上に本体のバー、の順で重ねる
        var trail = CreateStretchedImage(inner, "Trail", new Color(1f, 1f, 1f, 0.92f));
        var fill = CreateStretchedImage(inner, "Fill", new Color(0.22f, 1f, 0.46f));

        // 攻撃準備ゲージ。HPバーのすぐ下に細く出す
        var spinTrack = new GameObject("SpinTrack", typeof(RectTransform));
        spinTrack.transform.SetParent(root.transform, false);
        var spinTrackRect = spinTrack.GetComponent<RectTransform>();
        spinTrackRect.anchorMin = new Vector2(0f, 0f);
        spinTrackRect.anchorMax = new Vector2(1f, 0f);
        spinTrackRect.pivot = new Vector2(0.5f, 1f);
        spinTrackRect.anchoredPosition = new Vector2(0f, -3f);
        spinTrackRect.sizeDelta = new Vector2(0f, 12f);

        var spinBg = CreateStretchedImage(spinTrackRect, "SpinBg", new Color(0.06f, 0.07f, 0.09f, 0.85f));
        spinBg.raycastTarget = false;
        var spinFill = CreateStretchedImage(spinTrackRect, "SpinFill", new Color(1f, 0.73f, 0.12f));

        // ラベル。バーに重ねると緑地に埋もれるので、すぐ下に出す
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(root.transform, false);
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, -22f);
        labelRect.sizeDelta = new Vector2(0f, 42f);

        var label = labelGo.AddComponent<Text>();
        label.font = GetFont();
        label.fontSize = 30;
        label.fontStyle = FontStyle.Bold;
        label.color = Color.Lerp(accent, Color.white, 0.35f);
        label.raycastTarget = false;
        label.alignment = rightAligned ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        // 空の背景でも読めるように縁取りを入れる
        var outline = labelGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        var bar = root.AddComponent<HpBarUI>();
        bar.rightAligned = rightAligned;
        bar.fill = fill.rectTransform;
        bar.trail = trail.rectTransform;
        bar.fillGraphic = fill;
        bar.spinFill = spinFill.rectTransform;
        bar.label = label;

        return bar;
    }

    /// <summary>
    /// 親いっぱいに広がる単色の板を作る。
    /// RawImage は texture 未設定でも color そのままの矩形として描かれるので、
    /// Sprite を用意しなくて済む（Image は sprite が無いと何も描かない）。
    /// </summary>
    static RawImage CreateStretchedImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.AddComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Font GetFont()
    {
        if (_font == null)
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 48);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }
}
