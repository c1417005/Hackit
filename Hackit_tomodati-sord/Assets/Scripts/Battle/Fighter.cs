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

    [Header("プレイヤー")]
    public int playerIndex;
    public int facing = 1;
    public bool keyboardFallback = true;
    public bool useExternalInput;

    [Header("戦闘")]
    public float maxHp = 100f;
    [Range(0.1f, 0.6f)] public float windupRatio = 0.28f;
    [Range(0.1f, 0.6f)] public float activeRatio = 0.34f;
    public float baseAttackDuration = 0.38f;
    public float hitCooldown = 0.18f;
    [Range(0.05f, 0.8f)] public float guardDamageMultiplier = 0.35f;

    [Header("状態（読み取り用）")]
    [SerializeField] float _hp;
    [SerializeField] float _spinRatio = 1f;
    [SerializeField] bool _attacking;
    [SerializeField] bool _guarding;

    public event Action<float, float> OnHpChanged;
    // 既存HUDとの互換用。現在は攻撃準備ゲージ（1で攻撃可能）。
    public event Action<float> OnSpinChanged;
    public event Action<Fighter> OnDefeated;

    public SwordData Sword { get; private set; }
    public bool IsDefeated => _hp <= 0f;
    public float Hp => _hp;
    public float SpinRatio => _spinRatio;

    Rigidbody _rb;
    Transform _handPivot;
    Transform _swordPivot;
    GameObject _swordRoot;
    Renderer _bladeRenderer;
    Color _bladeBaseColor = Color.white;
    SwordBuilder.Metrics _metrics;
    SwordAttackHitbox _hitbox;
    TrailRenderer _trail;

    bool _inputEnabled = true;
    bool _hitThisAttack;
    bool _flashing;
    float _nextAttackTime;
    float _externalInput;
    float _previousExternalInput;
    Coroutine _attackRoutine;

    Quaternion IdleRotation => DirectionRotation(new Vector3(0.16f * facing, 1f, 0.08f));
    Quaternion GuardRotation => DirectionRotation(new Vector3(0.70f * facing, 0.72f, -0.22f));

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

    float AttackDuration
    {
        get
        {
            SwordStats stats = GetStats();
            float speedT = Mathf.InverseLerp(20f, 70f, stats.speed);
            return baseAttackDuration * Mathf.Lerp(1.25f, 0.68f, speedT);
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

        CreatePart("Palm", PrimitiveType.Sphere, _handPivot,
            new Vector3(0f, -0.02f, 0.06f), new Vector3(0.38f, 0.30f, 0.26f),
            new Color(0.86f, 0.60f, 0.42f));
        CreatePart("Thumb", PrimitiveType.Capsule, _handPivot,
            new Vector3(0.14f * facing, 0.08f, -0.01f), new Vector3(0.10f, 0.22f, 0.10f),
            new Color(0.92f, 0.67f, 0.48f));
        CreatePart("Grip", PrimitiveType.Cylinder, _handPivot,
            new Vector3(0f, 0.12f, 0f), new Vector3(0.09f, 0.16f, 0.09f),
            new Color(0.17f, 0.11f, 0.08f));

        var swordPivotObject = new GameObject("SwordPivot");
        _swordPivot = swordPivotObject.transform;
        _swordPivot.SetParent(_handPivot, false);
        _swordPivot.localPosition = new Vector3(0f, 0.22f, 0f);

        ApplyFacing();
        _handPivot.localRotation = IdleRotation;
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
            _handPivot.localRotation = _guarding ? GuardRotation : IdleRotation;
        }
    }

    void ApplyFacing()
    {
        if (_swordPivot != null)
        {
            _swordPivot.localRotation = Quaternion.Euler(0f, facing < 0 ? 180f : 0f, 0f);
        }
    }

    public void Equip(SwordData data, Texture2D texture)
    {
        if (_swordRoot != null) Destroy(_swordRoot);

        Sword = data;
        _metrics = SwordBuilder.GetMetrics(data);
        _swordRoot = SwordBuilder.Build(data, texture, _swordPivot);

        var hitboxObject = new GameObject("Hitbox");
        hitboxObject.transform.SetParent(_swordRoot.transform, false);
        hitboxObject.transform.localPosition = new Vector3(0f, _metrics.bladeCenterY, 0f);

        var box = hitboxObject.AddComponent<BoxCollider>();
        box.size = new Vector3(_metrics.bladeWidth * 0.86f, _metrics.bladeLength * 0.92f, 0.30f);
        box.isTrigger = true;

        _hitbox = hitboxObject.AddComponent<SwordAttackHitbox>();
        _hitbox.owner = this;
        _hitbox.Configure(box);

        var trailObject = new GameObject("TipTrail");
        trailObject.transform.SetParent(_swordRoot.transform, false);
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

        _bladeRenderer = _swordRoot.transform.Find("Blade")?.GetComponent<Renderer>();
        if (_bladeRenderer != null) _bladeBaseColor = GetRendererColor(_bladeRenderer);
    }

    void Update()
    {
        UpdateReadyGauge();

        if (!_inputEnabled || IsDefeated)
        {
            SetGuard(false);
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
        bool guard = (pad != null && pad.leftShoulder.isPressed)
                  || (keyboard != null && GuardKey(keyboard).isPressed);

        if (vertical) TryAttack(AttackKind.Vertical);
        else if (horizontal) TryAttack(AttackKind.Horizontal);
        else SetGuard(guard);
    }

    KeyControl VerticalKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.fKey : keyboard.periodKey;
    KeyControl HorizontalKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.rKey : keyboard.commaKey;
    KeyControl GuardKey(Keyboard keyboard) => playerIndex == 0 ? keyboard.gKey : keyboard.slashKey;

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

    public bool TryAttack(AttackKind kind)
    {
        if (!_inputEnabled || IsDefeated || _attacking || Time.time < _nextAttackTime) return false;
        if (_swordRoot == null) return false;

        SetGuard(false);
        _attackRoutine = StartCoroutine(AttackRoutine(kind));
        return true;
    }

    IEnumerator AttackRoutine(AttackKind kind)
    {
        _attacking = true;
        _hitThisAttack = false;
        SetReadyGauge(0f);

        float duration = AttackDuration;
        float windupTime = duration * windupRatio;
        float strikeTime = duration * activeRatio;
        float recoveryTime = Mathf.Max(0.02f, duration - windupTime - strikeTime);

        Quaternion start = _handPivot.localRotation;
        Quaternion windup;
        Quaternion strike;

        if (kind == AttackKind.Vertical)
        {
            // 上から下へ。X/Zの両方を変え、刃先がカメラ奥から手前へ抜ける。
            windup = DirectionRotation(new Vector3(-0.30f * facing, 1.0f, 0.52f));
            strike = DirectionRotation(new Vector3(0.38f * facing, -0.92f, -0.48f));
        }
        else
        {
            // 横薙ぎ。画面左右だけでなくZ方向にも大きく移動させる。
            windup = DirectionRotation(new Vector3(-1.0f * facing, 0.16f, 0.62f));
            strike = DirectionRotation(new Vector3(1.0f * facing, 0.12f, -0.62f));
        }

        yield return RotateOverTime(start, windup, windupTime, false);
        yield return RotateOverTime(windup, strike, strikeTime, true);
        yield return RotateOverTime(strike, IdleRotation, recoveryTime, false);

        _attacking = false;
        _nextAttackTime = Time.time + hitCooldown;
        _attackRoutine = null;
    }

    IEnumerator RotateOverTime(Quaternion from, Quaternion to, float duration, bool active)
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
            if (active) _hitbox?.SetActiveWindow(true);
            yield return null;
        }

        _handPivot.localRotation = to;
        if (!active)
        {
            _hitbox?.SetActiveWindow(false);
            if (_trail != null) _trail.emitting = false;
        }
    }

    static Quaternion DirectionRotation(Vector3 direction)
    {
        direction.Normalize();
        return Quaternion.FromToRotation(Vector3.up, direction);
    }

    void SetGuard(bool guarding)
    {
        if (_attacking) guarding = false;
        if (_guarding == guarding) return;
        _guarding = guarding;
        if (_handPivot != null)
        {
            _handPivot.localRotation = guarding ? GuardRotation : IdleRotation;
        }
    }

    internal void HandleBladeContact(Collider other)
    {
        if (!_attacking || _hitThisAttack || _hitbox == null || !_hitbox.ActiveWindow) return;

        Fighter target = other.GetComponentInParent<Fighter>();
        if (target == null || target == this || target.IsDefeated) return;

        _hitThisAttack = true;
        SwordStats stats = GetStats();
        float rawDamage = 12f + stats.attack * 0.42f;
        target.TakeImpact(rawDamage, this);
        HitStop.Play(0.065f);
    }

    public void TakeImpact(float rawDamage, Fighter from)
    {
        if (IsDefeated) return;

        SwordStats stats = GetStats();
        float damage = Mathf.Max(1f, rawDamage - stats.defense * 0.18f);
        bool guarded = _guarding && !_attacking;
        if (guarded) damage *= guardDamageMultiplier;

        _hp = Mathf.Max(0f, _hp - damage);
        OnHpChanged?.Invoke(_hp, maxHp);
        StartCoroutine(FlashBlade());
        Vector3 impactPosition = from != null
            ? Vector3.Lerp(TipPosition, from.TipPosition, 0.5f)
            : TipPosition;
        BattleEffects.ShowImpact(impactPosition, damage, guarded, from != null ? from.playerIndex : 0);

        if (_hp <= 0f)
        {
            _inputEnabled = false;
            SetGuard(false);
            OnDefeated?.Invoke(this);
        }
    }

    public void ResetForBattle()
    {
        StopAllCoroutines();
        _attackRoutine = null;
        _attacking = false;
        _guarding = false;
        _hitThisAttack = false;
        _flashing = false;
        _hp = maxHp;
        _inputEnabled = true;
        _nextAttackTime = 0f;
        _externalInput = 0f;
        _previousExternalInput = 0f;

        if (_handPivot != null) _handPivot.localRotation = IdleRotation;
        _hitbox?.SetActiveWindow(false);
        if (_trail != null)
        {
            _trail.Clear();
            _trail.emitting = false;
        }
        if (_bladeRenderer != null) SetRendererColor(_bladeRenderer, _bladeBaseColor);

        SetReadyGauge(1f);
        OnHpChanged?.Invoke(_hp, maxHp);
    }

    public void SetInputEnabled(bool enabled)
    {
        _inputEnabled = enabled;
        if (!enabled) SetGuard(false);
    }

    void UpdateReadyGauge()
    {
        if (_attacking)
        {
            SetReadyGauge(0f);
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
            : new SwordStats(40, 40, 40, 1f);
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
