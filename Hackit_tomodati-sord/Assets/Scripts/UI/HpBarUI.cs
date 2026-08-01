using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1人分のHPバー。Fighter.OnHpChanged を購読して伸び縮みする。
///
/// 減った分は白い帯（trail）が少し遅れて追いかける。格ゲーでよくあるやつ。
/// 1P は左詰め、2P は右詰めで減る。
/// </summary>
public class HpBarUI : MonoBehaviour
{
    [Header("参照")]
    public RectTransform fill;
    public RectTransform trail;
    public Graphic fillGraphic;
    public Text label;

    [Tooltip("攻撃準備ゲージ。HPバーの下に細く出す")]
    public RectTransform spinFill;

    [Header("挙動")]
    [Tooltip("true で右端を基準に減る（2P用）")]
    public bool rightAligned;

    [Tooltip("白い帯が追いつく速さ（割合/秒）")]
    public float trailSpeed = 0.55f;

    [Tooltip("追従を始めるまでの待ち時間")]
    public float trailDelay = 0.35f;

    Fighter _fighter;
    float _ratio = 1f;
    float _trailRatio = 1f;
    float _trailWaitUntil;

    /// <summary>このバーが表示する Fighter を設定する。付け替え可。</summary>
    public void Bind(Fighter fighter)
    {
        if (_fighter != null)
        {
            _fighter.OnHpChanged -= HandleHpChanged;
            _fighter.OnSpinChanged -= HandleSpinChanged;
            _fighter.OnModeChanged -= HandleModeChanged;
        }

        _fighter = fighter;

        if (_fighter != null)
        {
            _fighter.OnHpChanged += HandleHpChanged;
            _fighter.OnSpinChanged += HandleSpinChanged;
            _fighter.OnModeChanged += HandleModeChanged;
            // Start の発火を待たずに今の値で初期化する
            HandleHpChanged(_fighter.Hp, _fighter.maxHp);
            HandleSpinChanged(_fighter.SpinRatio);
            HandleModeChanged(_fighter.CurrentMode);
            _trailRatio = _ratio;
            ApplyRect(trail, _trailRatio);
        }
    }

    void OnDestroy()
    {
        if (_fighter != null)
        {
            _fighter.OnHpChanged -= HandleHpChanged;
            _fighter.OnSpinChanged -= HandleSpinChanged;
            _fighter.OnModeChanged -= HandleModeChanged;
        }
    }

    void HandleSpinChanged(float ratio)
    {
        ApplyRect(spinFill, Mathf.Clamp01(ratio));
    }

    void HandleModeChanged(Fighter.WeaponMode mode)
    {
        UpdateLabel(_fighter != null ? _fighter.Hp : 0f);
    }

    void HandleHpChanged(float current, float max)
    {
        _ratio = max <= 0f ? 0f : Mathf.Clamp01(current / max);

        // 回復した場合は帯を待たせずに合わせる
        if (_ratio > _trailRatio)
        {
            _trailRatio = _ratio;
            ApplyRect(trail, _trailRatio);
        }
        else
        {
            _trailWaitUntil = Time.unscaledTime + trailDelay;
        }

        ApplyRect(fill, _ratio);

        if (fillGraphic != null)
        {
            fillGraphic.color = Color.Lerp(new Color(1f, 0.10f, 0.08f), new Color(0.16f, 1f, 0.42f), _ratio);
        }

        UpdateLabel(current);
    }

    void UpdateLabel(float current)
    {
        if (label == null) return;
        string swordName = _fighter != null && _fighter.Sword != null ? _fighter.Sword.name : "";
        string mode = _fighter != null && _fighter.CurrentMode == Fighter.WeaponMode.Axe ? "AXE" : "SWORD";
        label.text = $"{swordName}   [{mode}]   {Mathf.CeilToInt(current)}";
    }

    void Update()
    {
        if (_trailRatio <= _ratio) return;
        if (Time.unscaledTime < _trailWaitUntil) return;

        // ヒットストップ中も動いてほしいので unscaled
        _trailRatio = Mathf.Max(_ratio, _trailRatio - trailSpeed * Time.unscaledDeltaTime);
        ApplyRect(trail, _trailRatio);
    }

    void ApplyRect(RectTransform rect, float ratio)
    {
        if (rect == null) return;

        if (rightAligned)
        {
            rect.anchorMin = new Vector2(1f - ratio, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
        }
        else
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(ratio, 1f);
        }

        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
