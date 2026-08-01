using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1文字ずつ別の Text にして波打たせる。錬成中の見出し用。
///
/// TextMeshPro を使えば1コンポーネントで済むが、初回に Essentials のインポートを
/// 求められてハッカソン中に詰まるので、Legacy Text を並べて自前で動かしている。
/// 文字幅は Font.GetCharacterInfo で実測するので日本語でも崩れない。
/// </summary>
public class WavyText : MonoBehaviour
{
    [Header("見た目")]
    public int fontSize = 64;
    public Color colorA = new Color(1f, 0.86f, 0.35f);
    public Color colorB = new Color(1f, 0.45f, 0.15f);

    [Header("動き")]
    [Tooltip("縦揺れの高さ(px)")]
    public float waveHeight = 10f;

    [Tooltip("縦揺れの速さ")]
    public float waveSpeed = 4.5f;

    [Tooltip("隣の文字とどれだけずらすか")]
    public float wavePhase = 0.45f;

    [Tooltip("1文字ずつ出てくる間隔(秒)。0で一斉に出る")]
    public float revealInterval = 0.06f;

    readonly List<RectTransform> _chars = new List<RectTransform>();
    readonly List<Text> _texts = new List<Text>();
    readonly List<float> _baseX = new List<float>();

    float _startedAt;
    static Font _font;

    public string Content { get; private set; } = "";

    /// <summary>文字列を差し替える。毎フレーム呼ぶものではない。</summary>
    public void SetText(string value)
    {
        if (Content == value) return;
        Content = value ?? "";

        foreach (RectTransform child in _chars)
        {
            if (child != null) Destroy(child.gameObject);
        }
        _chars.Clear();
        _texts.Clear();
        _baseX.Clear();

        Build();
        _startedAt = Time.unscaledTime;
    }

    void Build()
    {
        if (string.IsNullOrEmpty(Content)) return;

        Font font = GetFont();

        // 文字幅を測る前に、その文字をフォントアトラスに載せてもらう必要がある
        font.RequestCharactersInTexture(Content, fontSize, FontStyle.Bold);

        var advances = new float[Content.Length];
        float total = 0f;
        for (int i = 0; i < Content.Length; i++)
        {
            float advance = fontSize * 0.6f;   // 取れなかったときの目安
            if (font.GetCharacterInfo(Content[i], out CharacterInfo info, fontSize, FontStyle.Bold))
            {
                if (info.advance > 0) advance = info.advance;
            }
            advances[i] = advance;
            total += advance;
        }

        float x = -total * 0.5f;
        for (int i = 0; i < Content.Length; i++)
        {
            var go = new GameObject("c" + i, typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(advances[i] + 8f, fontSize * 1.6f);
            rect.anchoredPosition = new Vector2(x + advances[i] * 0.5f, 0f);

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = Content[i].ToString();

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.12f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);

            _chars.Add(rect);
            _texts.Add(text);
            _baseX.Add(rect.anchoredPosition.x);

            x += advances[i];
        }
    }

    void Update()
    {
        // ヒットストップや一時停止に引きずられたくないので unscaled
        float elapsed = Time.unscaledTime - _startedAt;

        for (int i = 0; i < _chars.Count; i++)
        {
            RectTransform rect = _chars[i];
            Text text = _texts[i];
            if (rect == null || text == null) continue;

            // 出現。まだ順番が来ていない文字は隠しておく
            float appearAt = revealInterval * i;
            float appear = revealInterval <= 0f ? 1f : Mathf.Clamp01((elapsed - appearAt) / 0.18f);

            float phase = elapsed * waveSpeed - i * wavePhase;
            float wave = Mathf.Sin(phase);

            rect.anchoredPosition = new Vector2(_baseX[i], wave * waveHeight * appear);

            // 出現時に大きく → 通常サイズへ
            float scale = Mathf.Lerp(1.7f, 1f, appear);
            rect.localScale = new Vector3(scale, scale, 1f);

            Color color = Color.Lerp(colorA, colorB, (wave + 1f) * 0.5f);
            color.a = appear;
            text.color = color;
        }
    }

    static Font GetFont()
    {
        if (_font == null)
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }
}
