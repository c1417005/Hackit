using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// 手を支点に剣を保持し、縦斬りと横斬りを行うプレイヤー。
/// 吊り下げ物理は使わず、攻撃中だけTriggerで命中判定を行う。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Fighter : MonoBehaviour
{
    public enum AttackKind
    {
        Vertical,
        Horizontal,
    }

    public enum WeaponMode
    {
        Sword,
        Axe,
    }

    [Header("プレイヤー")]
    public int playerIndex;
    public int facing = 1;
    public bool keyboardFallback = true;
    public bool useExternalInput;

    [Header("戦闘")]
    [Tooltip("対戦が短すぎないよう、従来の100から200へ増量")]
    public float maxHp = 200f;
    [Range(0.1f, 0.6f)] public float windupRatio = 0.28f;
    [Range(0.1f, 0.6f)] public float activeRatio = 0.34f;
    public float baseAttackDuration = 0.38f;
    public float hitCooldown = 0.18f;

    [Header("Charge Attack")]
    public float maxChargeSeconds = 1.2f;
    public float maxChargeDamageMultiplier = 1.9f;
    public float maxChargeRecoveryMultiplier = 2f;

    [Header("Dodge")]
    public float dodgeDistance = 0.62f;
    public float dodgeDuration = 0.30f;
    public float dodgeInvincibleSeconds = 0.22f;
    public float dodgeCooldown = 0.90f;

    [Header("Movement")]
    public float minArenaX = -1.45f;
    public float maxArenaX = 1.45f;
    public float centerGap = 0.20f;

    [Header("Weapon Mode")]
    public float axeDamageMultiplier = 1.35f;
    public float axeDurationMultiplier = 1.25f;
    public float axeMoveMultiplier = 0.78f;

    [Header("Horizontal Swing Visual Depth")]
    [Tooltip("横振りの振りかぶりで、描画モデルだけをカメラから遠ざける距離")]
    public float horizontalWindupDepth = 0.16f;
    [Tooltip("横振りの命中付近で、描画モデルだけをカメラへ近づける距離")]
    public float horizontalStrikeDepth = 0.34f;
    [Tooltip("刃の面の角度を見せるために加えるY軸回転")]
    public float horizontalYawDegrees = 34f;
    [Tooltip("カメラへ最接近した瞬間の見た目の拡大率")]
    public float horizontalPeakScale = 1.16f;
    [Tooltip("横斬りで手・モデル・当たり判定を真横へ移動させる距離")]
    public float horizontalSweepDistance = 0.22f;

    [Header("状態（読み取り用）")]
    [SerializeField] float _hp;
    [SerializeField] float _spinRatio = 1f;
    [SerializeField] bool _attacking;
    [SerializeField] bool _charging;
    [SerializeField] bool _dodging;

    public event Action<float, float> OnHpChanged;
    // 既存HUDとの互換用。現在は攻撃準備ゲージ（1で攻撃可能）。
    public event Action<float> OnSpinChanged;
    public event Action<WeaponMode> OnModeChanged;
    public event Action<Fighter> OnDefeated;

    public SwordData Sword { get; private set; }
    public bool IsDefeated => _hp <= 0f;
    public float Hp => _hp;
    public float SpinRatio => _spinRatio;
    public WeaponMode CurrentMode { get; private set; } = WeaponMode.Sword;

    Rigidbody _rb;
    Transform _handPivot;
    Transform _swordPivot;
    Transform _swordVisualPivot;
    GameObject _swordRoot;
    Renderer _bladeRenderer;
    Color _bladeBaseColor = Color.white;
    SwordBuilder.Metrics _metrics;
    SwordAttackHitbox _hitbox;
    BoxCollider _attackBox;
    GameObject _axeHead;
    TrailRenderer _trail;
    AudioSource _modelVoiceSource;
    AudioClip _modelMotionVoice;

    bool _inputEnabled = true;
    bool _hitThisAttack;
    bool _flashing;
    float _nextAttackTime;
    float _externalInput;
    float _previousExternalInput;
    Coroutine _attackRoutine;
    AttackKind _chargeKind;
    float _chargeStartedAt;
    float _nextDodgeTime;
    float _invincibleUntil;
    float _currentDamageMultiplier = 1f;

    struct VisualPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;

        public static VisualPose Identity => new VisualPose
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,
            scale = Vector3.one,
        };
    }

    Quaternion IdleRotation => DirectionRotation(new Vector3(0.16f * facing, 1f, 0f));

    public Vector3 TipPosition
    {
        get
        {
            if (_swordRoot == null) return transform.position;
            return _swordRoot.transform.TransformPoint(Vector3.up * _metrics.tipDistance);
        }
    }

    public float TipRadius => 0.78f + _metrics.tipDistance;
    public float TipSpeed => _attacking ? TipRadius / Mathf.Max(0.01f, AttackDuration) : 0f;
    public float SwingAngle => _handPivot == null ? 0f : _handPivot.localEulerAngles.z;
    public float ChargeRatio => !_charging ? 0f : Mathf.Clamp01((Time.time - _chargeStartedAt) / Mathf.Max(0.01f, maxChargeSeconds));

    float AttackDuration
    {
        get
        {
            SwordStats stats = GetStats();
            float speedT = Mathf.InverseLerp(20f, 70f, stats.speed);
            float modeMultiplier = CurrentMode == WeaponMode.Axe ? axeDurationMultiplier : 1f;
            return baseAttackDuration * Mathf.Lerp(1.48f, 0.52f, speedT) * modeMultiplier;
        }
    }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // 旧振り子版のColliderがシーンに残っていても物理衝突させない。
        foreach (Collider oldCollider in GetComponents<Collider>())
        {
            Destroy(oldCollider);
        }

        BuildHandRig();
        _modelVoiceSource = gameObject.AddComponent<AudioSource>();
        _modelVoiceSource.playOnAwake = false;
        _modelVoiceSource.spatialBlend = 0.35f;
        _modelVoiceSource.volume = 0.85f;
        _hp = maxHp;
    }

    void Start()
    {
        OnHpChanged?.Invoke(_hp, maxHp);
        SetReadyGauge(1f);
    }

    void BuildHandRig()
    {
        // この原点が手首＝全攻撃モーションの支点。
        var pivotObject = new GameObject("HandPivot");
        _handPivot = pivotObject.transform;
        _handPivot.SetParent(transform, false);

        BuildFist();
        // The weapon image itself is the target. A separate oversized hurtbox made
        // attacks connect in empty space, so the weapon collider now serves both roles.

        var swordPivotObject = new GameObject("SwordPivot");
        _swordPivot = swordPivotObject.transform;
        _swordPivot.SetParent(_handPivot, false);
        _swordPivot.localPosition = new Vector3(0f, 0.22f, 0f);

        ApplyFacing();
        _handPivot.localRotation = IdleRotation;
    }

    /// <summary>
    /// 被弾判定。**これが無いと「相手の剣に自分の剣を当てる」ことでしか
    /// ダメージが出ず、カウンター専用のゲームになる。**
    ///
    /// 手そのものを的にする。Fighter の原点に置いて攻撃モーションで動かさないので、
    /// 棒立ちの相手にも普通に攻撃が当たる。
    /// </summary>
    void BuildHurtbox()
    {
        var go = new GameObject("Hurtbox");
        go.transform.SetParent(transform, false);

        // 的は手そのものではなく「掲げている人」。手の上に大きく取る。
        // ここが小さいと、振り抜いた刃が届く範囲(x≈0.5)より奥になり永久に当たらない。
        go.transform.localPosition = new Vector3(0f, 0.55f, 0f);

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(0.95f, 1.70f, 0.75f);
    }

    /// <summary>
    /// 握りこぶし。球ひとつだと肉団子に見えるので、
    /// 甲・指4本・親指・袖口を分けて「握っている」形にする。
    /// 外部モデルは持ってこない（出所とライセンスが確認できないものを混ぜたくないため）。
    /// </summary>
    void BuildFist()
    {
        var skin = new Color(0.93f, 0.74f, 0.60f);
        var skinShade = new Color(0.82f, 0.62f, 0.49f);
        var cuff = new Color(0.16f, 0.19f, 0.28f);

        // 手の甲。縦に潰した箱で、丸い塊感を消す
        CreatePart("Back", PrimitiveType.Cube, _handPivot,
            new Vector3(0f, -0.01f, 0.02f), new Vector3(0.21f, 0.20f, 0.15f), skin);

        // 指4本。握りに巻きつくように少しずつずらす
        for (int i = 0; i < 4; i++)
        {
            float z = 0.055f - i * 0.037f;
            CreatePart("Finger" + i, PrimitiveType.Capsule, _handPivot,
                new Vector3(0.005f * facing, 0.075f, z),
                new Vector3(0.075f, 0.052f, 0.075f), i % 2 == 0 ? skin : skinShade);
        }

        // 親指は反対側から被せる
        CreatePart("Thumb", PrimitiveType.Capsule, _handPivot,
            new Vector3(-0.085f * facing, 0.045f, 0.015f),
            new Vector3(0.062f, 0.085f, 0.062f), skin);

        // 袖口。手首の切断面を隠す
        CreatePart("Cuff", PrimitiveType.Cylinder, _handPivot,
            new Vector3(0f, -0.13f, 0f), new Vector3(0.20f, 0.09f, 0.18f), cuff);

        // 握りの棒
        CreatePart("Grip", PrimitiveType.Cylinder, _handPivot,
            new Vector3(0f, 0.13f, 0f), new Vector3(0.075f, 0.14f, 0.075f),
            new Color(0.17f, 0.11f, 0.08f));
    }

    static GameObject CreatePart(
        string name, PrimitiveType primitive, Transform parent,
        Vector3 localPosition, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null) DestroyImmediate(collider);

        Renderer renderer = part.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", 0.55f);
        renderer.sharedMaterial = material;
        return part;
    }

    public void SetFacing(int newFacing)
    {
        facing = newFacing >= 0 ? 1 : -1;
        ApplyFacing();
        if (_handPivot != null && !_attacking)
        {
            _handPivot.localRotation = IdleRotation;
        }
    }

    void ApplyFacing()
    {
        if (_swordPivot != null)
        {
            _swordPivot.localRotation = Quaternion.Euler(0f, facing < 0 ? 180f : 0f, 0f);
        }
    }

    public void Equip(SwordData data, Texture2D texture, AudioClip modelMotionVoice = null)
    {
        if (_swordRoot != null) Destroy(_swordRoot);

        Sword = data;
        _modelMotionVoice = modelMotionVoice;
        _metrics = SwordBuilder.GetMetrics(data);
        _swordRoot = SwordBuilder.Build(data, texture, _swordPivot);

        // 描画モデルだけを動かす支点。HitboxはSwordRoot直下に残すため、
        // 奥行き演出を加えてもゲーム上の攻撃範囲は2Dのまま変化しない。
        var visualPivotObject = new GameObject("VisualDepthPivot");
        visualPivotObject.transform.SetParent(_swordRoot.transform, false);
        _swordVisualPivot = visualPivotObject.transform;
        Transform blade = _swordRoot.transform.Find("Blade");
        if (blade != null) blade.SetParent(_swordVisualPivot, false);

        var hitboxObject = new GameObject("Hitbox");
        hitboxObject.transform.SetParent(_swordRoot.transform, false);

        // 判定は握りから刃先までの**全長**を覆う。
        // 以前は刃の一部（bladeLength * 0.92）しか無く、振り抜いても
        // 判定の先端が x=0.03 までしか出ないため相手に永久に届かなかった。
        // ここを全長にすることで、身長から生成されたモデルの長さと
        // 実際の攻撃範囲を一致させる。
        float length = _metrics.tipDistance;
        hitboxObject.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);

        var box = hitboxObject.AddComponent<BoxCollider>();
        box.size = new Vector3(Mathf.Max(0.22f, _metrics.bladeWidth), length, 0.34f);
        box.isTrigger = true;
        _attackBox = box;

        _hitbox = hitboxObject.AddComponent<SwordAttackHitbox>();
        _hitbox.owner = this;
        _hitbox.Configure(box);

        _axeHead = new GameObject("AxeHead");
        _axeHead.transform.SetParent(_swordVisualPivot, false);
        _axeHead.transform.localPosition = new Vector3(0f, _metrics.tipDistance * 0.78f, 0.025f);
        CreatePart("AxeBladeLeft", PrimitiveType.Cube, _axeHead.transform,
            new Vector3(-_metrics.bladeWidth * 0.72f, 0f, 0f),
            new Vector3(_metrics.bladeWidth * 1.35f, _metrics.bladeLength * 0.22f, 0.08f),
            new Color(1f, 0.62f, 0.08f));
        CreatePart("AxeBladeRight", PrimitiveType.Cube, _axeHead.transform,
            new Vector3(_metrics.bladeWidth * 0.72f, 0f, 0f),
            new Vector3(_metrics.bladeWidth * 1.35f, _metrics.bladeLength * 0.22f, 0.08f),
            new Color(1f, 0.78f, 0.18f));
        _axeHead.SetActive(false);

        var trailObject = new GameObject("TipTrail");
        trailObject.transform.SetParent(_swordVisualPivot, false);
        trailObject.transform.localPosition = new Vector3(0f, _metrics.tipDistance, 0f);
        _trail = trailObject.AddComponent<TrailRenderer>();
        _trail.time = 0.14f;
        _trail.minVertexDistance = 0.025f;
        _trail.startWidth = Mathf.Max(0.08f, _metrics.bladeWidth * 0.55f);
        _trail.endWidth = 0f;
        _trail.emitting = false;
        _trail.numCornerVertices = 4;
        _trail.numCapVertices = 3;
        _trail.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit"));
        _trail.startColor = playerIndex == 0
            ? new Color(0.30f, 0.78f, 1f, 0.85f)
            : new Color(1f, 0.34f, 0.24f, 0.85f);
        _trail.endColor = new Color(_trail.startColor.r, _trail.startColor.g, _trail.startColor.b, 0f);
        ApplyModeVisual();

        _bladeRenderer = _swordVisualPivot.Find("Blade")?.GetComponent<Renderer>();
        if (_bladeRenderer != null) _bladeBaseColor = GetRendererColor(_bladeRenderer);
    }

    void Update()
    {
        UpdateReadyGauge();

        if (!_inputEnabled || IsDefeated)
        {
            return;
        }

        if (useExternalInput)
        {
            ReadExternalAttack();
            return;
        }

        Gamepad pad = GetPad();
        Keyboard keyboard = keyboardFallback ? Keyboard.current : null;

        bool vertical = (pad != null && pad.buttonWest.wasPressedThisFrame)
                     || (keyboard != null && VerticalKey(keyboard).wasPressedThisFrame);
        bool horizontal = (pad != null && pad.buttonNorth.wasPressedThisFrame)
                       || (keyboard != null && HorizontalKey(keyboard).wasPressedThisFrame);
        bool verticalHeld = (pad != null && pad.buttonWest.isPressed)
                         || (keyboard != null && VerticalKey(keyboard).isPressed);
        bool horizontalHeld = (pad != null && pad.buttonNorth.isPressed)
                           || (keyboard != null && HorizontalKey(keyboard).isPressed);
        bool dodge = (pad != null && pad.buttonEast.wasPressedThisFrame)
                  || (keyboard != null && DodgeKey(keyboard).wasPressedThisFrame);
        bool changeMode = (pad != null && pad.rightShoulder.wasPressedThisFrame)
                       || (keyboard != null && ModeKey(keyboard).wasPressedThisFrame);

        float move = 0f;
        if (pad != null)
        {
            move = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(move) < 0.15f) move = pad.dpad.x.ReadValue();
        }
        else if (keyboard != null)
        {
            if (MoveLeftKey(keyboard).isPressed) move -= 1f;
            if (MoveRightKey(keyboard).isPressed) move += 1f;
        }

        if (changeMode) TryChangeMode();
        ApplyMovement(move);

        if (dodge && TryDodge()) return;

        if (_charging)
        {
            bool stillHeld = _chargeKind == AttackKind.Vertical ? verticalHeld : horizontalHeld;
            UpdateChargeVisual();
            if (!stillHeld) ReleaseCharge();
            return;
        }

        if (vertical) BeginCharge(AttackKind.Vertical);
        else if (horizontal) BeginCharge(AttackKind.Horizontal);
    }

    KeyControl VerticalKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.fKey : keyboard.periodKey;
    KeyControl HorizontalKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.rKey : keyboard.commaKey;
    KeyControl DodgeKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.tKey : keyboard.semicolonKey;
    KeyControl ModeKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.vKey : keyboard.rightShiftKey;
    KeyControl MoveLeftKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.aKey : keyboard.leftArrowKey;
    KeyControl MoveRightKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.dKey : keyboard.rightArrowKey;

    Gamepad GetPad()
    {
        var pads = Gamepad.all;
        return playerIndex >= 0 && playerIndex < pads.Count ? pads[playerIndex] : null;
    }

    /// <summary>
    /// CPU操作との互換用。正で縦斬り、負で横斬り。閾値を跨いだ瞬間だけ攻撃する。
    /// </summary>
    public void SetSwingInput(float input)
    {
        _externalInput = Mathf.Clamp(input, -1f, 1f);
    }

    void ReadExternalAttack()
    {
        if (_externalInput > 0.5f && _previousExternalInput <= 0.5f)
            TryAttack(AttackKind.Vertical);
        else if (_externalInput < -0.5f && _previousExternalInput >= -0.5f)
            TryAttack(AttackKind.Horizontal);

        _previousExternalInput = _externalInput;
    }

    void ApplyMovement(float input)
    {
        if (Mathf.Abs(input) < 0.05f || _attacking || _charging || _dodging) return;

        SwordStats stats = GetStats();
        float speedT = Mathf.InverseLerp(20f, 70f, stats.speed);
        float moveSpeed = Mathf.Lerp(0.62f, 2.25f, speedT);
        if (CurrentMode == WeaponMode.Axe) moveSpeed *= axeMoveMultiplier;

        Vector3 position = transform.position;
        position.x += Mathf.Clamp(input, -1f, 1f) * moveSpeed * Time.deltaTime;

        if (playerIndex == 0)
            position.x = Mathf.Clamp(position.x, minArenaX, -centerGap);
        else
            position.x = Mathf.Clamp(position.x, centerGap, maxArenaX);

        transform.position = position;
    }

    public bool TryChangeMode()
    {
        if (!_inputEnabled || IsDefeated || _attacking || _charging || _dodging) return false;

        CurrentMode = CurrentMode == WeaponMode.Sword ? WeaponMode.Axe : WeaponMode.Sword;
        ApplyModeVisual();
        OnModeChanged?.Invoke(CurrentMode);
        return true;
    }

    void ApplyModeVisual()
    {
        if (_axeHead != null) _axeHead.SetActive(CurrentMode == WeaponMode.Axe);

        if (_attackBox != null)
        {
            if (CurrentMode == WeaponMode.Axe)
            {
                _attackBox.transform.localPosition = new Vector3(0f, _metrics.tipDistance * 0.78f, 0f);
                _attackBox.size = new Vector3(
                    Mathf.Max(0.52f, _metrics.bladeWidth * 2.7f),
                    _metrics.bladeLength * 0.25f,
                    0.38f);
            }
            else
            {
                float length = _metrics.tipDistance;
                _attackBox.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
                _attackBox.size = new Vector3(Mathf.Max(0.22f, _metrics.bladeWidth), length, 0.34f);
            }
        }

        if (_trail == null) return;
        Color color = CurrentMode == WeaponMode.Sword
            ? (playerIndex == 0 ? new Color(0.30f, 0.78f, 1f, 0.85f) : new Color(1f, 0.34f, 0.24f, 0.85f))
            : new Color(1f, 0.72f, 0.12f, 0.95f);
        _trail.startColor = color;
        _trail.endColor = new Color(color.r, color.g, color.b, 0f);
        _trail.startWidth = CurrentMode == WeaponMode.Axe
            ? Mathf.Max(0.14f, _metrics.bladeWidth * 0.82f)
            : Mathf.Max(0.08f, _metrics.bladeWidth * 0.55f);
    }

    public bool TryAttack(AttackKind kind)
    {
        return TryAttack(kind, 0f);
    }

    public bool TryAttack(AttackKind kind, float chargeRatio)
    {
        if (!_inputEnabled || IsDefeated || _attacking || _dodging || Time.time < _nextAttackTime) return false;
        if (_swordRoot == null) return false;

        _attackRoutine = StartCoroutine(AttackRoutine(kind, Mathf.Clamp01(chargeRatio)));
        return true;
    }

    void BeginCharge(AttackKind kind)
    {
        if (!_inputEnabled || IsDefeated || _attacking || _dodging || _charging || Time.time < _nextAttackTime) return;
        if (_swordRoot == null) return;

        _charging = true;
        _chargeKind = kind;
        _chargeStartedAt = Time.time;
        _handPivot.localRotation = GetAttackRotations(kind).windup;
        UpdateChargeVisual();
    }

    void ReleaseCharge()
    {
        if (!_charging) return;
        float ratio = ChargeRatio;
        AttackKind kind = _chargeKind;
        _charging = false;
        RestoreBladeColor();
        if (!TryAttack(kind, ratio)) _handPivot.localRotation = IdleRotation;
    }

    void CancelCharge()
    {
        if (!_charging) return;
        _charging = false;
        RestoreBladeColor();
        if (_handPivot != null) _handPivot.localRotation = IdleRotation;
    }

    void UpdateChargeVisual()
    {
        if (_bladeRenderer == null) return;
        float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 13f) * 0.22f;
        Color charged = Color.Lerp(_bladeBaseColor, new Color(1f, 0.28f, 0.08f), ChargeRatio * pulse);
        SetRendererColor(_bladeRenderer, charged);
    }

    (Quaternion windup, Quaternion strike) GetAttackRotations(AttackKind kind)
    {
        if (kind == AttackKind.Vertical)
        {
            return (
                DirectionRotation(new Vector3(-0.30f * facing, 1.0f, 0f)),
                DirectionRotation(new Vector3(0.38f * facing, -0.92f, 0f)));
        }

        return (
            DirectionRotation(new Vector3(1f * facing, 0f, 0f)),
            DirectionRotation(new Vector3(1f * facing, 0f, 0f)));
    }

    IEnumerator AttackRoutine(AttackKind kind, float chargeRatio)
    {
        _attacking = true;
        _hitThisAttack = false;
        SetReadyGauge(0f);
        PlayModelMotionVoice(kind);

        float duration = AttackDuration;
        float windupTime = duration * windupRatio;
        float strikeTime = duration * activeRatio;
        float recoveryMultiplier = Mathf.Lerp(1f, maxChargeRecoveryMultiplier, chargeRatio);
        float recoveryTime = Mathf.Max(0.02f, duration - windupTime - strikeTime) * recoveryMultiplier;
        _currentDamageMultiplier = Mathf.Lerp(1f, maxChargeDamageMultiplier, chargeRatio);

        Quaternion start = _handPivot.localRotation;
        Quaternion windup;
        Quaternion strike;
        Vector3 startHandPosition = _handPivot.localPosition;
        Vector3 windupHandPosition = Vector3.zero;
        Vector3 strikeHandPosition = Vector3.zero;

        if (kind == AttackKind.Vertical)
        {
            // 上から下へ。X/Zの両方を変え、刃先がカメラ奥から手前へ抜ける。
            windup = DirectionRotation(new Vector3(-0.30f * facing, 1.0f, 0f));
            strike = DirectionRotation(new Vector3(0.38f * facing, -0.92f, 0f));
        }
        else
        {
            // 横薙ぎ中はモデルを完全な水平に固定する。
            // 回転弧で振ると途中で縦になるため、手・モデル・判定をまとめてX方向へ直線移動させる。
            windup = DirectionRotation(new Vector3(1f * facing, 0f, 0f));
            strike = windup;
            windupHandPosition = new Vector3(-horizontalSweepDistance * facing, 0f, 0f);
            strikeHandPosition = new Vector3(horizontalSweepDistance * facing, 0f, 0f);
        }

        VisualPose idleVisual = VisualPose.Identity;
        VisualPose currentVisual = ReadVisualPose();
        VisualPose windupVisual = idleVisual;
        VisualPose strikeVisual = idleVisual;
        if (kind == AttackKind.Horizontal)
        {
            // カメラは-Z側。振りかぶりでは+Z（奥）、命中付近では-Z（手前）へ描画だけ動かす。
            // 2P側はSwordPivotがY軸180度反転しているため、local Zもfacingで補正する。
            windupVisual.position = new Vector3(0f, 0f, horizontalWindupDepth * facing);
            windupVisual.rotation = Quaternion.Euler(0f, -horizontalYawDegrees * 0.55f * facing, 0f);
            windupVisual.scale = Vector3.one * 0.94f;

            strikeVisual.position = new Vector3(0f, 0f, -horizontalStrikeDepth * facing);
            strikeVisual.rotation = Quaternion.Euler(0f, horizontalYawDegrees * facing, 0f);
            strikeVisual.scale = Vector3.one * horizontalPeakScale;
        }

        yield return RotateOverTime(
            start, windup, windupTime, false,
            startHandPosition, windupHandPosition,
            currentVisual, windupVisual);
        yield return RotateOverTime(
            windup, strike, strikeTime, true,
            windupHandPosition, strikeHandPosition,
            windupVisual, strikeVisual);
        yield return RotateOverTime(
            strike, IdleRotation, recoveryTime, false,
            strikeHandPosition, Vector3.zero,
            strikeVisual, idleVisual);
        _handPivot.localPosition = Vector3.zero;
        ApplyVisualPose(idleVisual);

        _attacking = false;
        _currentDamageMultiplier = 1f;
        _nextAttackTime = Time.time + hitCooldown;
        _attackRoutine = null;
    }

    void PlayModelMotionVoice(AttackKind kind)
    {
        if (_modelVoiceSource == null || _modelMotionVoice == null) return;
        _modelVoiceSource.pitch = kind == AttackKind.Horizontal ? 0.96f : 1.04f;
        _modelVoiceSource.Stop();
        _modelVoiceSource.PlayOneShot(_modelMotionVoice);
    }

    IEnumerator RotateOverTime(
        Quaternion from,
        Quaternion to,
        float duration,
        bool active,
        Vector3 handPositionFrom,
        Vector3 handPositionTo,
        VisualPose visualFrom,
        VisualPose visualTo)
    {
        _hitbox?.SetActiveWindow(active);
        if (_trail != null) _trail.emitting = active;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, duration));
            t = t * t * (3f - 2f * t);
            _handPivot.localRotation = Quaternion.Slerp(from, to, t);
            _handPivot.localPosition = Vector3.Lerp(handPositionFrom, handPositionTo, t);
            ApplyVisualPose(LerpVisualPose(visualFrom, visualTo, t));
            if (active) _hitbox?.SetActiveWindow(true);
            yield return null;
        }

        _handPivot.localRotation = to;
        _handPivot.localPosition = handPositionTo;
        ApplyVisualPose(visualTo);
        if (!active)
        {
            _hitbox?.SetActiveWindow(false);
            if (_trail != null) _trail.emitting = false;
        }
    }

    VisualPose ReadVisualPose()
    {
        if (_swordVisualPivot == null) return VisualPose.Identity;
        return new VisualPose
        {
            position = _swordVisualPivot.localPosition,
            rotation = _swordVisualPivot.localRotation,
            scale = _swordVisualPivot.localScale,
        };
    }

    static VisualPose LerpVisualPose(VisualPose from, VisualPose to, float t)
    {
        return new VisualPose
        {
            position = Vector3.Lerp(from.position, to.position, t),
            rotation = Quaternion.Slerp(from.rotation, to.rotation, t),
            scale = Vector3.Lerp(from.scale, to.scale, t),
        };
    }

    void ApplyVisualPose(VisualPose pose)
    {
        if (_swordVisualPivot == null) return;
        _swordVisualPivot.localPosition = pose.position;
        _swordVisualPivot.localRotation = pose.rotation;
        _swordVisualPivot.localScale = pose.scale;
    }

    static Quaternion DirectionRotation(Vector3 direction)
    {
        direction.Normalize();
        return Quaternion.FromToRotation(Vector3.up, direction);
    }

    internal void HandleBladeContact(Collider other)
    {
        if (!_attacking || _hitThisAttack || _hitbox == null || !_hitbox.ActiveWindow) return;

        Fighter target = other.GetComponentInParent<Fighter>();
        if (target == null || target == this || target.IsDefeated) return;

        _hitThisAttack = true;
        SwordStats stats = GetStats();
        float modeDamage = CurrentMode == WeaponMode.Axe ? axeDamageMultiplier : 1f;
        // 防御ステータス廃止後も従来と近い決着速度になるよう基礎威力を調整。
        float rawDamage = (7f + stats.attack * 0.38f) * _currentDamageMultiplier * modeDamage;
        target.TakeImpact(rawDamage, this);
        HitStop.Play(0.065f);
    }

    public void TakeImpact(float rawDamage, Fighter from)
    {
        if (IsDefeated || Time.time < _invincibleUntil) return;

        float damage = Mathf.Max(1f, rawDamage);

        _hp = Mathf.Max(0f, _hp - damage);
        OnHpChanged?.Invoke(_hp, maxHp);
        StartCoroutine(FlashBlade());
        Vector3 impactPosition = from != null
            ? Vector3.Lerp(TipPosition, from.TipPosition, 0.5f)
            : TipPosition;
        BattleEffects.ShowImpact(impactPosition, damage, from != null ? from.playerIndex : 0);

        if (_hp <= 0f)
        {
            _inputEnabled = false;
            OnDefeated?.Invoke(this);
        }
    }

    public bool TryDodge()
    {
        if (!_inputEnabled || IsDefeated || _attacking || _dodging || Time.time < _nextDodgeTime) return false;

        CancelCharge();
        StartCoroutine(DodgeRoutine());
        return true;
    }

    IEnumerator DodgeRoutine()
    {
        _dodging = true;
        _invincibleUntil = Time.time + dodgeInvincibleSeconds;
        _nextDodgeTime = Time.time + dodgeCooldown;

        Vector3 start = transform.position;
        Vector3 away = start + Vector3.left * facing * dodgeDistance;
        float elapsed = 0f;
        float outwardEnd = dodgeDuration * 0.42f;

        while (elapsed < dodgeDuration)
        {
            elapsed += Time.deltaTime;
            if (elapsed < outwardEnd)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(0.01f, outwardEnd));
                transform.position = Vector3.Lerp(start, away, t);
            }
            else
            {
                float t = Mathf.SmoothStep(0f, 1f, (elapsed - outwardEnd) / Mathf.Max(0.01f, dodgeDuration - outwardEnd));
                transform.position = Vector3.Lerp(away, start, t);
            }
            yield return null;
        }

        transform.position = start;
        _dodging = false;
    }

    public void ResetForBattle()
    {
        StopAllCoroutines();
        _attackRoutine = null;
        _attacking = false;
        _charging = false;
        _dodging = false;
        _hitThisAttack = false;
        _flashing = false;
        _hp = maxHp;
        _inputEnabled = true;
        _nextAttackTime = 0f;
        _nextDodgeTime = 0f;
        _invincibleUntil = 0f;
        _currentDamageMultiplier = 1f;
        _externalInput = 0f;
        _previousExternalInput = 0f;
        CurrentMode = WeaponMode.Sword;
        ApplyModeVisual();

        if (_handPivot != null)
        {
            _handPivot.localRotation = IdleRotation;
            _handPivot.localPosition = Vector3.zero;
        }
        ApplyVisualPose(VisualPose.Identity);
        _hitbox?.SetActiveWindow(false);
        if (_trail != null)
        {
            _trail.Clear();
            _trail.emitting = false;
        }
        if (_bladeRenderer != null) SetRendererColor(_bladeRenderer, _bladeBaseColor);

        SetReadyGauge(1f);
        OnHpChanged?.Invoke(_hp, maxHp);
        OnModeChanged?.Invoke(CurrentMode);
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled)
        {
            CancelCharge();
        }
    }

    void UpdateReadyGauge()
    {
        if (_attacking || _dodging)
        {
            SetReadyGauge(0f);
            return;
        }

        if (_charging)
        {
            SetReadyGauge(ChargeRatio);
            return;
        }

        float remaining = _nextAttackTime - Time.time;
        float ratio = remaining <= 0f ? 1f : 1f - Mathf.Clamp01(remaining / Mathf.Max(0.01f, hitCooldown));
        SetReadyGauge(ratio);
    }

    void SetReadyGauge(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Abs(value - _spinRatio) < 0.01f) return;
        _spinRatio = value;
        OnSpinChanged?.Invoke(value);
    }

    SwordStats GetStats()
    {
        return Sword != null && Sword.stats != null
            ? Sword.stats
            : new SwordStats(40, 40, TposeSwordTemplateSettings.DefaultHeightCm);
    }

    IEnumerator FlashBlade()
    {
        if (_bladeRenderer == null || _flashing) yield break;
        _flashing = true;
        SetRendererColor(_bladeRenderer, new Color(1f, 0.42f, 0.32f));
        yield return new WaitForSeconds(0.10f);
        SetRendererColor(_bladeRenderer, _bladeBaseColor);
        _flashing = false;
    }

    void RestoreBladeColor()
    {
        if (_bladeRenderer != null && !_flashing) SetRendererColor(_bladeRenderer, _bladeBaseColor);
    }

    static Color GetRendererColor(Renderer renderer)
    {
        Material material = renderer.material;
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    static void SetRendererColor(Renderer renderer, Color color)
    {
        Material material = renderer.material;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }
}

/// <summary>
/// 剣の3D Triggerを所有Fighterへ中継する。
/// TransformアニメーションがFixedUpdate間を飛び越えても抜けないよう、
/// 前フレームから現在位置までBoxCastも行う。
/// </summary>
public sealed class SwordAttackHitbox : MonoBehaviour
{
    public Fighter owner;
    public bool ActiveWindow { get; private set; }

    BoxCollider _box;
    Vector3 _previousCenter;
    bool _hasPreviousCenter;

    public void Configure(BoxCollider box)
    {
        _box = box;
        _previousCenter = WorldCenter;
    }

    public void SetActiveWindow(bool active)
    {
        if (active && !ActiveWindow)
        {
            _previousCenter = WorldCenter;
            _hasPreviousCenter = true;
        }

        ActiveWindow = active;
        if (!active) _hasPreviousCenter = false;
    }

    Vector3 WorldCenter => _box == null ? transform.position : transform.TransformPoint(_box.center);

    Vector3 WorldHalfExtents
    {
        get
        {
            if (_box == null) return Vector3.one * 0.1f;
            Vector3 scale = transform.lossyScale;
            return new Vector3(
                Mathf.Abs(_box.size.x * scale.x) * 0.5f,
                Mathf.Abs(_box.size.y * scale.y) * 0.5f,
                Mathf.Abs(_box.size.z * scale.z) * 0.5f);
        }
    }

    void LateUpdate()
    {
        if (!ActiveWindow || owner == null || _box == null) return;

        Vector3 center = WorldCenter;
        Vector3 halfExtents = WorldHalfExtents;

        Collider[] overlaps = Physics.OverlapBox(
            center, halfExtents, transform.rotation, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider other in overlaps) owner.HandleBladeContact(other);

        if (_hasPreviousCenter)
        {
            Vector3 movement = center - _previousCenter;
            float distance = movement.magnitude;
            if (distance > 0.001f)
            {
                RaycastHit[] hits = Physics.BoxCastAll(
                    _previousCenter, halfExtents, movement / distance,
                    transform.rotation, distance, ~0, QueryTriggerInteraction.Collide);
                foreach (RaycastHit hit in hits) owner.HandleBladeContact(hit.collider);
            }
        }

        _previousCenter = center;
        _hasPreviousCenter = true;
    }

    void OnTriggerEnter(Collider other)
    {
        owner?.HandleBladeContact(other);
    }

    void OnTriggerStay(Collider other)
    {
        owner?.HandleBladeContact(other);
    }
}
