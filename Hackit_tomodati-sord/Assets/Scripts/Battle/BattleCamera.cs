using UnityEngine;

/// <summary>
/// 両者の剣が常に画面に収まる最小限まで自動で寄る横固定カメラ。
///
/// 静止しているときはぐっと寄り、振り回すと引く。
/// 回転が速いほど「引かれている」ので、勢いがそのまま画面に出る。
/// </summary>
[RequireComponent(typeof(Camera))]
public class BattleCamera : MonoBehaviour
{
    static BattleCamera _instance;

    [Header("対象")]
    public Fighter player1;
    public Fighter player2;

    [Header("画角")]
    [Tooltip("対象の外側に取る余白")]
    public float margin = 0.45f;

    [Tooltip("これ以上は寄らない（近づきすぎ防止）")]
    public float minHalfHeight = 1.15f;

    [Tooltip("これ以上は引かない")]
    public float maxHalfHeight = 2.9f;

    [Tooltip("左右の追従の強さ。0で中央固定")]
    [Range(0f, 1f)]
    public float horizontalFollow = 0.35f;

    [Tooltip("寄り引きの追従速度")]
    public float followSpeed = 3.5f;

    Camera _camera;
    float _shakeUntil;
    float _shakeAmount;

    void Awake()
    {
        _instance = this;
        _camera = GetComponent<Camera>();
        transform.rotation = Quaternion.identity;   // 横固定
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    public static void Shake(float amount, float duration)
    {
        if (_instance == null) return;
        _instance._shakeAmount = Mathf.Max(_instance._shakeAmount, amount);
        _instance._shakeUntil = Mathf.Max(_instance._shakeUntil, Time.unscaledTime + duration);
    }

    void LateUpdate()
    {
        if (player1 == null || player2 == null) return;

        // 手の支点と刃先。この4点が入っていれば剣は全部映る
        Bounds bounds = new Bounds(player1.transform.position, Vector3.zero);
        bounds.Encapsulate(player1.TipPosition);
        bounds.Encapsulate(player2.transform.position);
        bounds.Encapsulate(player2.TipPosition);

        float halfHeight = bounds.extents.y + margin;
        float halfWidth = bounds.extents.x + margin;

        // 横長画面では高さが効く。横に広がったぶんも高さに換算して比較する
        float aspect = _camera.aspect <= 0f ? 1.77f : _camera.aspect;
        float needed = Mathf.Max(halfHeight, halfWidth / aspect);
        needed = Mathf.Clamp(needed, minHalfHeight, maxHalfHeight);

        float distance = needed / Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

        var desired = new Vector3(
            bounds.center.x * horizontalFollow,
            bounds.center.y,
            -distance);

        float t = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
        Vector3 position = Vector3.Lerp(transform.position, desired, t);
        if (Time.unscaledTime < _shakeUntil)
        {
            float remaining = Mathf.Clamp01((_shakeUntil - Time.unscaledTime) / 0.45f);
            Vector2 offset = Random.insideUnitCircle * _shakeAmount * Mathf.Sqrt(remaining);
            position += new Vector3(offset.x, offset.y, 0f);
        }
        else
        {
            _shakeAmount = 0f;
        }

        transform.position = position;
    }
}
