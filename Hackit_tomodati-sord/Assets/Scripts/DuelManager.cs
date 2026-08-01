using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 対戦全体の進行役。
/// 剣一覧取得 → 選択 → 装備 → 対戦 → 決着 → 戦績送信 までを持つ。
///
/// 画面の見た目は持たない。SwordSelectUI / BattleHud がこれを購読して描く。
/// </summary>
public class DuelManager : MonoBehaviour
{
    public enum Phase
    {
        Loading,
        Select,
        Battle,
        Result,
    }

    [Header("参照")]
    public SwordRepository repository;
    public Fighter player1;
    public Fighter player2;

    [Header("配置")]
    [Tooltip("中央から手の支点までの距離。広げすぎると剣が相手に届かない")]
    public float spawnDistance = 1.15f;

    [Tooltip("手の支点の高さ")]
    public float anchorHeight = 3.4f;

    [Header("進行")]
    [Tooltip("決着してからリザルト操作を受け付けるまでの待ち")]
    public float resultInputDelay = 1.2f;

    public Phase Current { get; private set; } = Phase.Loading;

    /// <summary>取得済みの剣一覧。選択画面はこれを並べる。</summary>
    public IReadOnlyList<SwordData> Swords => _swords;

    /// <summary>勝者・敗者。Result フェーズでのみ有効。</summary>
    public Fighter Winner { get; private set; }
    public Fighter Loser { get; private set; }

    public event Action<Phase> OnPhaseChanged;

    /// <summary>(playerIndex, 選ばれた剣)。決定を取り消した場合は null が来る。</summary>
    public event Action<int, SwordData> OnSelectionChanged;

    readonly List<SwordData> _swords = new List<SwordData>();
    readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
    readonly SwordData[] _selected = new SwordData[2];

    bool _resultInputReady;

    void Start()
    {
        // OnEnable の時点では player1/player2 がまだ代入されていないことがある
        // （AddComponent した直後に外から差し込む場合）。ここで確実に張り直す。
        SubscribeFighters(true);

        if (repository == null)
        {
            repository = GetComponent<SwordRepository>();
            if (repository == null)
            {
                repository = gameObject.AddComponent<SwordRepository>();
            }
        }

        StartCoroutine(LoadThenSelect());
    }

    void OnEnable()
    {
        SubscribeFighters(true);
    }

    void OnDisable()
    {
        SubscribeFighters(false);
    }

    void SubscribeFighters(bool subscribe)
    {
        foreach (var fighter in new[] { player1, player2 })
        {
            if (fighter == null) continue;
            fighter.OnDefeated -= HandleDefeated;
            if (subscribe) fighter.OnDefeated += HandleDefeated;
        }
    }

    IEnumerator LoadThenSelect()
    {
        SetPhase(Phase.Loading);
        SetFightersActive(false);

        yield return repository.FetchSwords(list =>
        {
            _swords.Clear();
            if (list != null) _swords.AddRange(list);
        });

        // 画像は選択画面に入る前に全部そろえておく。対戦開始で待たされないように。
        foreach (var sword in _swords)
        {
            SwordData captured = sword;
            yield return repository.FetchTexture(captured, tex =>
            {
                if (captured != null && !string.IsNullOrEmpty(captured.id))
                {
                    _textures[captured.id] = tex;
                }
            });
        }

        Debug.Log($"[DuelManager] 剣を{_swords.Count}本読み込んだ");
        EnterSelect();
    }

    public void EnterSelect()
    {
        _selected[0] = null;
        _selected[1] = null;
        Winner = null;
        Loser = null;

        SetFightersActive(false);
        SetPhase(Phase.Select);

        OnSelectionChanged?.Invoke(0, null);
        OnSelectionChanged?.Invoke(1, null);
    }

    public Texture2D GetTexture(SwordData sword)
    {
        if (sword == null || string.IsNullOrEmpty(sword.id)) return null;
        return _textures.TryGetValue(sword.id, out Texture2D tex) ? tex : null;
    }

    public SwordData GetSelected(int playerIndex)
    {
        return playerIndex >= 0 && playerIndex < 2 ? _selected[playerIndex] : null;
    }

    public bool IsSelected(int playerIndex) => GetSelected(playerIndex) != null;

    /// <summary>選択画面から呼ぶ。2人そろったら自動で対戦に入る。</summary>
    public void SelectSword(int playerIndex, SwordData data)
    {
        if (Current != Phase.Select) return;
        if (playerIndex < 0 || playerIndex >= 2) return;

        _selected[playerIndex] = data;
        OnSelectionChanged?.Invoke(playerIndex, data);

        if (_selected[0] != null && _selected[1] != null)
        {
            StartBattle();
        }
    }

    /// <summary>選択を取り消す。</summary>
    public void CancelSelection(int playerIndex)
    {
        if (Current != Phase.Select) return;
        if (playerIndex < 0 || playerIndex >= 2) return;

        _selected[playerIndex] = null;
        OnSelectionChanged?.Invoke(playerIndex, null);
    }

    public void StartBattle()
    {
        if (player1 == null || player2 == null)
        {
            Debug.LogError("[DuelManager] Fighter が設定されていない");
            return;
        }

        // 選択されていなければ先頭の剣で埋める。デモが止まらないように。
        SwordData sword1 = _selected[0] ?? (_swords.Count > 0 ? _swords[0] : null);
        SwordData sword2 = _selected[1] ?? (_swords.Count > 1 ? _swords[1] : sword1);

        SetFightersActive(true);

        player1.transform.position = new Vector3(-spawnDistance, anchorHeight, 0f);
        player2.transform.position = new Vector3(spawnDistance, anchorHeight, 0f);
        player1.SetFacing(1);
        player2.SetFacing(-1);

        player1.Equip(sword1, GetTexture(sword1));
        player2.Equip(sword2, GetTexture(sword2));

        // Equip で剣の長さが変わるので、手の支点の取り直しは装備のあとにやる
        player1.ResetForBattle();
        player2.ResetForBattle();

        player1.SetInputEnabled(false);
        player2.SetInputEnabled(false);
        SetPhase(Phase.Battle);

        BattleEffects.PlayCountdown(() =>
        {
            if (Current != Phase.Battle) return;
            player1.SetInputEnabled(true);
            player2.SetInputEnabled(true);
        });
    }

    void HandleDefeated(Fighter loser)
    {
        if (Current != Phase.Battle) return;

        Loser = loser;
        Winner = loser == player1 ? player2 : player1;

        player1.SetInputEnabled(false);
        player2.SetInputEnabled(false);

        SetPhase(Phase.Result);
        BattleEffects.ShowKO(Winner);
        StartCoroutine(ResultRoutine());
    }

    IEnumerator ResultRoutine()
    {
        _resultInputReady = false;

        string winnerId = Winner != null && Winner.Sword != null ? Winner.Sword.id : null;
        string loserId = Loser != null && Loser.Sword != null ? Loser.Sword.id : null;

        if (!string.IsNullOrEmpty(winnerId) && !string.IsNullOrEmpty(loserId))
        {
            yield return repository.PostMatch(winnerId, loserId);
        }

        yield return new WaitForSecondsRealtime(resultInputDelay);
        _resultInputReady = true;
    }

    /// <summary>リザルトから選択画面に戻れる状態か。</summary>
    public bool CanLeaveResult => Current == Phase.Result && _resultInputReady;

    void SetFightersActive(bool active)
    {
        foreach (var fighter in new[] { player1, player2 })
        {
            if (fighter == null) continue;
            fighter.gameObject.SetActive(active);
            fighter.SetInputEnabled(active);
        }
    }

    void SetPhase(Phase phase)
    {
        if (Current == phase) return;
        Current = phase;
        OnPhaseChanged?.Invoke(phase);
    }
}
