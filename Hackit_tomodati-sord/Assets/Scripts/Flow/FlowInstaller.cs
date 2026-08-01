using UnityEngine;

/// <summary>
/// 進行役（SwordRepository + DuelManager）を組み立てる。
///
/// **データ・進行担当のファイル。** 取得元の差し替えや進行の初期値はここで完結させる。
/// </summary>
public static class FlowInstaller
{
    public static DuelManager Install(Fighter player1, Fighter player2, float spawnDistance, float anchorHeight)
    {
        var go = new GameObject("DuelFlow");

        var repository = go.AddComponent<SwordRepository>();

        var duel = go.AddComponent<DuelManager>();
        duel.repository = repository;
        duel.player1 = player1;
        duel.player2 = player2;
        duel.spawnDistance = spawnDistance;
        duel.anchorHeight = anchorHeight;

        return duel;
    }
}
