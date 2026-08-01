using UnityEngine;

/// <summary>
/// 操作説明と決着表示の仮表示。IMGUI なので使い捨て。
/// ちゃんとしたリザルト画面ができたら、このファイルごと消して
/// UiInstaller の1行を削れば良い。
/// </summary>
public class DebugResultOverlay : MonoBehaviour
{
    DuelManager _duel;

    public static DebugResultOverlay Create(DuelManager duel)
    {
        var go = new GameObject("DebugResultOverlay");
        var overlay = go.AddComponent<DebugResultOverlay>();
        overlay._duel = duel;
        return overlay;
    }

    void OnGUI()
    {
        if (_duel == null) return;

        if (_duel.Current == DuelManager.Phase.Battle)
        {
            var help = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            GUI.Label(new Rect(20, 120, 900, 60),
                "□ = 縦斬り   △ = 横斬り   L1 = ガード\n" +
                "キーボード: 1P = F / R / G   2P = . / , / /",
                help);
            return;
        }

        if (_duel.Current != DuelManager.Phase.Result || _duel.Winner == null) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
        };
        string name = _duel.Winner.Sword != null ? _duel.Winner.Sword.name : null;
        if (string.IsNullOrEmpty(name)) name = _duel.Winner.playerIndex == 0 ? "1P" : "2P";
        string label = name + " の勝ち！";
        GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 80), label, style);

        if (_duel.CanLeaveResult)
        {
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.32f + 80, Screen.width, 40), "R キーで剣えらびに戻る", sub);
        }
    }
}
