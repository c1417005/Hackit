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

        /// <summary>「既存の武器」か「新規作成」かを2人が同時に選ぶ</summary>
        ModeSelect,

        /// <summary>新規作成。QR → 錬成 → 抜刀。画面を占有するので1人ずつ</summary>
        Forge,

        /// <summary>既存の剣をグリッドから選ぶ。2人同時</summary>
        Select,

        Battle,
        Result,
    }

    public enum PlayerMode
    {
        Undecided,

        /// <summary>既存の武器から選ぶ</summary>
        Existing,

        /// <summary>QRを読んで新しく作る</summary>
        Create,
    }

    /// <summary>新規作成の進み具合。ForgeUI がこれを見て画面を切り替える。</summary>
    public enum ForgeStep
    {
        /// <summary>QRを出してアップロードを待っている</summary>
        WaitingUpload,

        /// <summary>DBに入ったのを検知して錬成中</summary>
        Forging,

        /// <summary>錬成完了。「この剣を抜く」待ち</summary>
        Ready,

        /// <summary>抜刀済み。ステータスを見せている</summary>
        Drawn,

        /// <summary>「この剣で戦う」か「既存の武器」かを選ばせている</summary>
        Confirm,
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

    [Tooltip("新着の剣がDBに入ったかを見に行く間隔（秒）")]
    public float uploadPollInterval = 0.7f;

    [Tooltip("錬成演出を最低これだけは見せる（一瞬で終わると味気ないので）")]
    public float minForgeSeconds = 3.2f;

    public Phase Current { get; private set; } = Phase.Loading;

    /// <summary>取得済みの剣一覧。選択画面はこれを並べる。</summary>
    public IReadOnlyList<SwordData> Swords => _swords;

    /// <summary>勝者・敗者。Result フェーズでのみ有効。</summary>
    public Fighter Winner { get; private set; }
    public Fighter Loser { get; private set; }

    public event Action<Phase> OnPhaseChanged;

    /// <summary>(playerIndex, 選ばれた剣)。決定を取り消した場合は null が来る。</summary>
    public event Action<int, SwordData> OnSelectionChanged;

    /// <summary>(playerIndex, mode)</summary>
    public event Action<int, PlayerMode> OnModeChanged;

    /// <summary>新規作成の進み具合が変わった</summary>
    public event Action<ForgeStep> OnForgeStepChanged;

    /// <summary>今 Forge 画面を使っているプレイヤー。Forge 中でなければ -1</summary>
    public int ForgingPlayer { get; private set; } = -1;

    public ForgeStep CurrentForgeStep { get; private set; } = ForgeStep.WaitingUpload;

    /// <summary>錬成でできた剣。ForgeStep が Ready 以降で有効</summary>
    public SwordData ForgedSword { get; private set; }

    public PlayerMode GetMode(int playerIndex) =>
        playerIndex >= 0 && playerIndex < 2 ? _modes[playerIndex] : PlayerMode.Undecided;

    /// <summary>この人はもう戦う準備ができたか</summary>
    public bool IsReady(int playerIndex) =>
        playerIndex >= 0 && playerIndex < 2 && _selected[playerIndex] != null;

    readonly List<SwordData> _swords = new List<SwordData>();
    readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
    readonly SwordData[] _selected = new SwordData[2];
    readonly PlayerMode[] _modes = new PlayerMode[2];

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

        // 既存武器の一覧は「既存の武器」を選んだ後に取得する。
        // 起動時にDB待ちを挟まないため、最初はモード選択だけを表示する。
        EnterModeSelect();
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

    /// <summary>最初の画面。2人が「既存」か「新規作成」かを同時に選ぶ。</summary>
    public void EnterModeSelect()
    {
        _selected[0] = null;
        _selected[1] = null;
        _modes[0] = PlayerMode.Undecided;
        _modes[1] = PlayerMode.Undecided;
        ForgingPlayer = -1;
        ForgedSword = null;
        Winner = null;
        Loser = null;

        SetFightersActive(false);
        SetPhase(Phase.ModeSelect);

        for (int i = 0; i < 2; i++)
        {
            OnModeChanged?.Invoke(i, PlayerMode.Undecided);
            OnSelectionChanged?.Invoke(i, null);
        }
    }

    /// <summary>モード選択画面から呼ぶ。2人そろったら次へ進む。</summary>
    public void SetMode(int playerIndex, PlayerMode mode)
    {
        if (Current != Phase.ModeSelect) return;
        if (playerIndex < 0 || playerIndex >= 2) return;

        _modes[playerIndex] = mode;
        OnModeChanged?.Invoke(playerIndex, mode);

        if (_modes[0] != PlayerMode.Undecided && _modes[1] != PlayerMode.Undecided)
        {
            Advance();
        }
    }

    /// <summary>
    /// 次にやることを決める。
    /// 新規作成がまだ残っていれば1人ずつ錬成へ、無ければ既存選択へ、
    /// 全員決まっていれば対戦へ。
    /// </summary>
    void Advance()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_modes[i] == PlayerMode.Create && _selected[i] == null)
            {
                StartCoroutine(ForgeRoutine(i));
                return;
            }
        }

        if (_selected[0] != null && _selected[1] != null)
        {
            StartBattle();
            return;
        }

        // 選択画面を開くたびSQLiteを読み直す。Web側で直前に追加された剣も見える。
        StartCoroutine(LoadThenSelect());
    }

    /// <summary>既存の剣を選ぶ画面。まだ決まっていない人だけが操作する。</summary>
    public void EnterSelect()
    {
        ForgingPlayer = -1;
        SetFightersActive(false);
        SetPhase(Phase.Select);

        for (int i = 0; i < 2; i++)
        {
            OnSelectionChanged?.Invoke(i, _selected[i]);
        }
    }

    // ---------- 新規作成（QR → 錬成 → 抜刀） ----------

    IEnumerator ForgeRoutine(int playerIndex)
    {
        ForgingPlayer = playerIndex;
        ForgedSword = null;
        SetForgeStep(ForgeStep.WaitingUpload);
        SetFightersActive(false);
        SetPhase(Phase.Forge);

        // 今ある剣を控えておく。ここに無いidが増えたら、それがこの人の剣。
        var known = new HashSet<string>();
        foreach (SwordData sword in _swords)
        {
            if (sword != null && !string.IsNullOrEmpty(sword.id)) known.Add(sword.id);
        }

        SwordData found = null;
        while (found == null)
        {
            yield return new WaitForSecondsRealtime(uploadPollInterval);

            // 錬成をやめて既存に切り替えた場合はここで抜ける
            if (Current != Phase.Forge || ForgingPlayer != playerIndex) yield break;

            found = repository.FindNewSword(known);
        }

        // 見つかった。ここからが「錬成中」
        ForgedSword = found;
        _swords.Insert(0, found);
        SetForgeStep(ForgeStep.Forging);

        float forgeStarted = Time.unscaledTime;

        yield return repository.FetchTexture(found, tex =>
        {
            if (!string.IsNullOrEmpty(found.id)) _textures[found.id] = tex;
        });

        // 一瞬で終わると演出にならないので最低限は見せる
        while (Time.unscaledTime - forgeStarted < minForgeSeconds)
        {
            yield return null;
        }

        SetForgeStep(ForgeStep.Ready);
    }

    /// <summary>「この剣を抜く」→「次へ」と進める。ForgeUI から呼ぶ。</summary>
    public void AdvanceForge()
    {
        if (Current != Phase.Forge) return;

        if (CurrentForgeStep == ForgeStep.Ready) SetForgeStep(ForgeStep.Drawn);
        else if (CurrentForgeStep == ForgeStep.Drawn) SetForgeStep(ForgeStep.Confirm);
    }

    /// <summary>「この剣で戦う」。これで準備完了。</summary>
    public void ConfirmForgedSword()
    {
        if (Current != Phase.Forge || ForgedSword == null) return;
        if (ForgingPlayer < 0 || ForgingPlayer >= 2) return;

        _selected[ForgingPlayer] = ForgedSword;
        OnSelectionChanged?.Invoke(ForgingPlayer, ForgedSword);

        ForgingPlayer = -1;
        Advance();
    }

    /// <summary>「既存の武器」。錬成した剣は使わず、グリッド選択に回す。</summary>
    public void RejectForgedSword()
    {
        if (Current != Phase.Forge) return;
        if (ForgingPlayer < 0 || ForgingPlayer >= 2) return;

        _modes[ForgingPlayer] = PlayerMode.Existing;
        OnModeChanged?.Invoke(ForgingPlayer, PlayerMode.Existing);

        ForgingPlayer = -1;
        Advance();
    }

    void SetForgeStep(ForgeStep step)
    {
        CurrentForgeStep = step;
        OnForgeStepChanged?.Invoke(step);
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

        // 2人とも準備完了になったら開戦。片方が錬成で決めていても同じ。
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
