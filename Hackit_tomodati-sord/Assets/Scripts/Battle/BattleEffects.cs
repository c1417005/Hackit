using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレハブや追加アセットなしで、対戦に必要な画面演出と簡易効果音をまとめて生成する。
/// </summary>
public sealed class BattleEffects : MonoBehaviour
{
    static BattleEffects _instance;
    static Font _font;

    Canvas _canvas;
    RectTransform _canvasRect;
    AudioSource _audio;
    AudioSource _music;
    AudioClip _hitSound;

    static BattleEffects Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var root = new GameObject("BattleEffects", typeof(Canvas), typeof(CanvasScaler));
            _instance = root.AddComponent<BattleEffects>();
            _instance.Initialize();
            return _instance;
        }
    }

    void Initialize()
    {
        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        _canvasRect = _canvas.GetComponent<RectTransform>();

        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _audio.volume = 0.72f;
        _hitSound = BuildImpactClip();

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.spatialBlend = 0f;
        _music.loop = true;
        _music.volume = 0.22f;
        _music.clip = BuildBattleMusic();
        _music.Play();
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static void ShowImpact(Vector3 worldPosition, float damage, int attackerIndex)
    {
        Instance.StartCoroutine(Instance.ImpactRoutine(worldPosition, damage, attackerIndex));
        Instance._audio.PlayOneShot(Instance._hitSound);
        BattleCamera.Shake(0.16f, 0.20f);
    }

    public static void PlayCountdown(Action onComplete)
    {
        Instance.StartCoroutine(Instance.CountdownRoutine(onComplete));
    }

    /// <summary>「たけしの剣 の勝ち！」。剣の名前が取れないときだけ 1P/2P に落とす。</summary>
    static string WinnerLabel(Fighter winner)
    {
        if (winner == null) return "";

        string name = winner.Sword != null ? winner.Sword.name : null;
        if (string.IsNullOrEmpty(name))
        {
            name = winner.playerIndex == 0 ? "1P" : "2P";
        }

        return name + " の勝ち！";
    }

    public static void ShowKO(Fighter winner)
    {
        Instance.StartCoroutine(Instance.KoRoutine(winner));
        BattleCamera.Shake(0.28f, 0.42f);
    }

    IEnumerator ImpactRoutine(Vector3 worldPosition, float damage, int attackerIndex)
    {
        Color accent = attackerIndex == 0
            ? new Color(0.25f, 0.80f, 1f)
            : new Color(1f, 0.32f, 0.20f);

        StartCoroutine(FlashRoutine(accent, 0.14f));
        SpawnImpactRing(worldPosition, accent, 0.95f);

        Text text = CreateText("Damage", 56, TextAnchor.MiddleCenter, accent);
        text.text = $"-{Mathf.CeilToInt(damage)}";
        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(480f, 100f);

        Camera camera = Camera.main;
        Vector2 local = Vector2.zero;
        if (camera != null)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local);
        }
        rect.anchoredPosition = local + new Vector2(0f, 44f);

        Color startColor = text.color;
        float elapsed = 0f;
        const float duration = 0.62f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rect.anchoredPosition = local + new Vector2(0f, 44f + 100f * t);
            rect.localScale = Vector3.one * Mathf.Lerp(1.32f, 0.92f, Mathf.SmoothStep(0f, 1f, t));
            text.color = new Color(startColor.r, startColor.g, startColor.b, 1f - t);
            yield return null;
        }
        Destroy(text.gameObject);
    }

    IEnumerator FlashRoutine(Color color, float peakAlpha)
    {
        var flashObject = new GameObject("ImpactFlash", typeof(RectTransform), typeof(RawImage));
        flashObject.transform.SetParent(_canvas.transform, false);
        RectTransform rect = flashObject.GetComponent<RectTransform>();
        Stretch(rect);
        RawImage image = flashObject.GetComponent<RawImage>();
        image.raycastTarget = false;

        float elapsed = 0f;
        const float duration = 0.16f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            image.color = new Color(color.r, color.g, color.b, peakAlpha * (1f - t));
            yield return null;
        }
        Destroy(flashObject);
    }

    void SpawnImpactRing(Vector3 position, Color color, float size)
    {
        var ringObject = new GameObject("ImpactRing");
        ringObject.transform.position = position + Vector3.back * 0.08f;
        var line = ringObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 32;
        line.startWidth = line.endWidth = 0.055f;
        line.numCornerVertices = 3;
        line.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
        line.startColor = line.endColor = color;

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.1f);
        }
        StartCoroutine(RingRoutine(ringObject.transform, line, color, size));
    }

    IEnumerator RingRoutine(Transform ring, LineRenderer line, Color color, float size)
    {
        float elapsed = 0f;
        const float duration = 0.24f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            ring.localScale = Vector3.one * Mathf.Lerp(0.3f, size * 6f, t);
            Color faded = new Color(color.r, color.g, color.b, 1f - t);
            line.startColor = line.endColor = faded;
            yield return null;
        }
        Destroy(ring.gameObject);
    }

    IEnumerator CountdownRoutine(Action onComplete)
    {
        Text text = CreateText("Countdown", 128, TextAnchor.MiddleCenter, Color.white);
        RectTransform rect = text.rectTransform;
        Stretch(rect);

        string[] labels = { "3", "2", "1", "FIGHT!" };
        foreach (string label in labels)
        {
            text.text = label;
            text.color = label == "FIGHT!" ? new Color(1f, 0.78f, 0.16f) : Color.white;
            float segment = label == "FIGHT!" ? 0.48f : 0.42f;
            float elapsed = 0f;
            while (elapsed < segment)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / segment);
                rect.localScale = Vector3.one * Mathf.Lerp(1.55f, 0.86f, t);
                Color c = text.color;
                c.a = 1f - Mathf.Pow(t, 3f);
                text.color = c;
                yield return null;
            }
        }

        Destroy(text.gameObject);
        onComplete?.Invoke();
    }

    IEnumerator KoRoutine(Fighter winner)
    {
        StartCoroutine(FlashRoutine(Color.white, 0.34f));
        Text text = CreateText("KO", 168, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.10f));
        text.text = "K.O.";
        RectTransform rect = text.rectTransform;
        Stretch(rect);

        float elapsed = 0f;
        const float duration = 1.15f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float punch = t < 0.18f ? Mathf.Lerp(2.4f, 0.92f, t / 0.18f) : 0.92f;
            rect.localScale = Vector3.one * punch;
            yield return null;
        }

        text.fontSize = 72;
        text.text = WinnerLabel(winner);
        yield return new WaitForSecondsRealtime(0.8f);
        Destroy(text.gameObject);
    }

    Text CreateText(string name, int fontSize, TextAnchor alignment, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline), typeof(Shadow));
        textObject.transform.SetParent(_canvas.transform, false);
        Text text = textObject.GetComponent<Text>();
        text.font = GetFont();
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.01f, 0.015f, 0.03f, 0.95f);
        outline.effectDistance = new Vector2(4f, -4f);
        Shadow shadow = textObject.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(8f, -8f);
        return text;
    }

    static Font GetFont()
    {
        if (_font != null) return _font;
        _font = Font.CreateDynamicFontFromOSFont(
            new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 64);
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return _font;
    }

    static AudioClip BuildImpactClip()
    {
        const int sampleRate = 44100;
        int samples = Mathf.RoundToInt(sampleRate * 0.11f);
        float[] data = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t * 34f);
            float tone = Mathf.Sin(t * Mathf.PI * 2f * 170f);
            float noise = UnityEngine.Random.Range(-1f, 1f) * 0.48f;
            data[i] = (tone * 0.55f + noise) * envelope;
        }

        AudioClip clip = AudioClip.Create("SwordImpact", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static AudioClip BuildBattleMusic()
    {
        const int sampleRate = 44100;
        const float stepSeconds = 0.25f;
        int[] melody =
        {
            64, 67, 71, 67, 62, 67, 69, 67,
            64, 67, 72, 71, 69, 67, 62, 59,
            64, 67, 71, 74, 72, 71, 67, 64,
            62, 67, 69, 71, 69, 67, 64, 62,
        };
        int[] bass = { 40, 40, 38, 38, 36, 36, 35, 35 };

        int stepSamples = Mathf.RoundToInt(sampleRate * stepSeconds);
        int samples = melody.Length * stepSamples;
        float[] data = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            int step = i / stepSamples;
            float within = (i % stepSamples) / (float)sampleRate;
            float time = i / (float)sampleRate;

            float leadHz = 440f * Mathf.Pow(2f, (melody[step] - 69) / 12f);
            float bassHz = 440f * Mathf.Pow(2f, (bass[(step / 4) % bass.Length] - 69) / 12f);
            float leadEnvelope = Mathf.Clamp01(1f - within / stepSeconds) * Mathf.Min(1f, within * 45f);
            float lead = Mathf.Sign(Mathf.Sin(time * Mathf.PI * 2f * leadHz)) * 0.10f * leadEnvelope;
            float low = Mathf.Sin(time * Mathf.PI * 2f * bassHz) * 0.13f;

            float beatWithin = (i % (stepSamples * 2)) / (float)sampleRate;
            float kickEnvelope = Mathf.Exp(-beatWithin * 18f);
            float kick = Mathf.Sin(time * Mathf.PI * 2f * (58f + 55f * kickEnvelope)) * kickEnvelope * 0.18f;

            data[i] = Mathf.Clamp(lead + low + kick, -0.72f, 0.72f);
        }

        AudioClip clip = AudioClip.Create("GeneratedBattleBGM", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
