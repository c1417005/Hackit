using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 組み立ての起点。シーンには空の GameObject にこれを1つ貼るだけ。
///
/// **ここは共有ファイル。触るのは最小限にすること。**
/// 実際の組み立ては各モジュールの Installer が持っているので、
/// 画面を足したいなら UiInstaller、場を変えたいなら BattleStageInstaller を触る。
/// このファイルは3つを順に呼ぶだけで、誰の作業でも変更が要らないのが理想。
///
/// シーンに物を置かず実行時に組み立てているのは .unity ファイルの競合を避けるため。
/// この方針は崩さないこと。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("中央から手の支点までの距離。広げすぎると剣が相手に届かない")]
    public float spawnDistance = 1.15f;

    [Tooltip("手の支点の高さ")]
    public float anchorHeight = 3.4f;

    // 手が y=3.4。斬撃中の刃先が上下へ振れても画面に入る位置。
    public Vector3 cameraPosition = new Vector3(0f, 2.7f, -6.2f);

    [Header("デバッグ")]
    [Tooltip("操作説明と決着表示の仮オーバーレイを出す")]
    public bool showDebugHud = true;

    [Tooltip("選択画面を飛ばして、いきなり先頭2本で対戦を始める（戦闘だけ試したい時用）")]
    public bool skipSelect;

    DuelManager _duel;

    void Start()
    {
        var stage = BattleStageInstaller.Install(new BattleStageInstaller.Config
        {
            spawnDistance = spawnDistance,
            anchorHeight = anchorHeight,
            cameraPosition = cameraPosition,
        });

        // Installer が射程の成立する範囲へ丸めているので、その値をそのまま渡す。
        // ここで生の spawnDistance を渡すと、対戦開始時に DuelManager が
        // 別の位置へ置き直してしまい、攻撃が届かなくなる。
        _duel = FlowInstaller.Install(stage.Player1, stage.Player2, stage.SpawnDistance, anchorHeight);

        UiInstaller.Install(_duel, stage.Player1, stage.Player2, showDebugHud);

        if (skipSelect)
        {
            _duel.OnPhaseChanged += StartImmediatelyOnce;
        }
    }

    /// <summary>選択画面に入った瞬間に対戦へ飛ばす。戦闘だけ確認したい時用。</summary>
    void StartImmediatelyOnce(DuelManager.Phase phase)
    {
        if (phase != DuelManager.Phase.Select) return;
        _duel.OnPhaseChanged -= StartImmediatelyOnce;
        _duel.StartBattle();
    }

    void Update()
    {
        if (_duel == null) return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 決着後、R で最初のモード選択まで戻す（EnterSelect は選択を保持してしまう）
        if (_duel.CanLeaveResult && keyboard.rKey.wasPressedThisFrame)
        {
            _duel.EnterModeSelect();
        }
    }
}
