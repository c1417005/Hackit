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
                "□ = 縦斬り   △ = 横斬り\n" +
                "キーボード: 1P = F / R   2P = . / ,　　OPTIONS / Esc = 戻る",
                help);
            return;
        }

        // Result表示は専用のVictoryScreenが担当する。
    }
}
