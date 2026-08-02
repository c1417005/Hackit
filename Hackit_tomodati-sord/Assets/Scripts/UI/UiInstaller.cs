using UnityEngine;

/// <summary>
/// 画面をまとめて組み立てる。
///
/// **UI・演出担当のファイル。** 画面を追加するときはここに1行足すだけで済むようにしてある。
/// App 側を触らなくてよいので、他の担当と衝突しない。
/// </summary>
public static class UiInstaller
{
    public static void Install(DuelManager duel, Fighter player1, Fighter player2, bool showDebugOverlay)
    {
        SwordSelectUI.Create(duel);
        ForgeUI.Create(duel);
        BattleHud.Create(player1, player2).Bind(duel);

        if (showDebugOverlay)
        {
            DebugResultOverlay.Create(duel);
        }
    }
}
