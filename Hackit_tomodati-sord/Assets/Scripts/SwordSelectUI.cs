using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 剣の選択画面。DuelManager.Swords をカードで並べ、2人がそれぞれカーソルで選ぶ。
///
/// マリオカートのキャラ選択のイメージ。カーソルは2つあり、
/// 同じカードに重なっても見えるように太さを変えた枠を二重に出す。
///
/// BattleHud と同じくコードから組んでいる（プレハブは作らない）。
/// </summary>
public class SwordSelectUI : MonoBehaviour
{
    const float CardWidth = 200f;
    const float CardHeight = 290f;
    const float Spacing = 18f;
    const int MaxColumns = 5;

    static readonly Color[] PlayerColors =
    {
        new Color(0.30f, 0.55f, 0.95f),
        new Color(0.95f, 0.42f, 0.35f),
    };

    DuelManager _duel;
    Canvas _canvas;
    RectTransform _grid;
    Text _title;
    Text _status;

    readonly List<RectTransform> _cards = new List<RectTransform>();
    readonly RectTransform[] _cursors = new RectTransform[2];
    readonly int[] _cursorIndex = new int[2];
    readonly bool[] _stickLatched = new bool[2];

    static Font _font;

    public static SwordSelectUI Create(DuelManager duel)
    {
        var go = new GameObject("SwordSelectUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var ui = go.AddComponent<SwordSelectUI>();
        ui.Init(duel);
        return ui;
    }

    void Init(DuelManager duel)
    {
        _duel = duel;

        _canvas = GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 10;

        var scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildChrome();

        _duel.OnPhaseChanged += HandlePhaseChanged;
        _duel.OnSelectionChanged += HandleSelectionChanged;

        HandlePhaseChanged(_duel.Current);
    }

    void OnDestroy()
    {
        if (_duel == null) return;
        _duel.OnPhaseChanged -= HandlePhaseChanged;
        _duel.OnSelectionChanged -= HandleSelectionChanged;
    }

    // ---------- 組み立て ----------

    void BuildChrome()
    {
        // 背景を暗く落とす
        var dim = CreateRawImage(transform, "Dim", new Color(0.04f, 0.05f, 0.08f, 0.97f));
        Stretch(dim.rectTransform);

        _title = CreateText(transform, "Title", 68, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0.28f));
        var titleRect = _title.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -50f);
        titleRect.sizeDelta = new Vector2(0f, 70f);
        _title.text = "剣をえらべ";

        var gridGo = new GameObject("Grid", typeof(RectTransform));
        gridGo.transform.SetParent(transform, false);
        _grid = gridGo.GetComponent<RectTransform>();
        _grid.anchorMin = _grid.anchorMax = new Vector2(0.5f, 0.5f);
        _grid.pivot = new Vector2(0.5f, 0.5f);
        _grid.anchoredPosition = new Vector2(0f, -10f);
        _grid.sizeDelta = Vector2.zero;

        _status = CreateText(transform, "Status", 32, TextAnchor.MiddleCenter, new Color(0.90f, 0.94f, 1f));
        var statusRect = _status.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = new Vector2(0f, 40f);
        statusRect.sizeDelta = new Vector2(0f, 90f);
    }

    void BuildCards()
    {
        foreach (var card in _cards)
        {
            if (card != null) Destroy(card.gameObject);
        }
        _cards.Clear();

        for (int i = 0; i < 2; i++)
        {
            if (_cursors[i] != null) Destroy(_cursors[i].gameObject);
            _cursors[i] = null;
        }

        int count = _duel.Swords.Count;
        if (count == 0) return;

        int columns = Mathf.Min(MaxColumns, count);
        int rows = Mathf.CeilToInt(count / (float)columns);

        float totalWidth = columns * CardWidth + (columns - 1) * Spacing;
        float totalHeight = rows * CardHeight + (rows - 1) * Spacing;

        // カードが多いときは全体を縮めて画面に収める
        float scale = Mathf.Min(1f, 1700f / totalWidth, 620f / totalHeight);
        _grid.localScale = new Vector3(scale, scale, 1f);

        // カーソルはカードより先に作って背面に置く（枠として縁が見える）
        for (int i = 0; i < 2; i++)
        {
            float pad = i == 0 ? 7f : 16f;
            var cursor = CreateRawImage(_grid, "Cursor" + (i + 1), PlayerColors[i]);
            cursor.rectTransform.sizeDelta = new Vector2(CardWidth + pad * 2f, CardHeight + pad * 2f);
            _cursors[i] = cursor.rectTransform;
        }

        for (int i = 0; i < count; i++)
        {
            int column = i % columns;
            int row = i / columns;

            float x = -totalWidth * 0.5f + CardWidth * 0.5f + column * (CardWidth + Spacing);
            float y = totalHeight * 0.5f - CardHeight * 0.5f - row * (CardHeight + Spacing);

            RectTransform card = BuildCard(_duel.Swords[i]);
            card.anchoredPosition = new Vector2(x, y);
            _cards.Add(card);
        }

        _cursorIndex[0] = 0;
        _cursorIndex[1] = Mathf.Min(1, count - 1);
        UpdateCursors();
    }

    RectTransform BuildCard(SwordData sword)
    {
        var go = new GameObject("Card_" + sword.name, typeof(RectTransform));
        go.transform.SetParent(_grid, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(CardWidth, CardHeight);

        var bg = CreateRawImage(rect, "Bg", new Color(0.10f, 0.14f, 0.24f));
        Stretch(bg.rectTransform);

        // 剣の画像。縦長なので高さ基準で収める
        Texture2D texture = _duel.GetTexture(sword);
        var image = CreateRawImage(rect, "Image", Color.white);
        image.texture = texture;

        float boxHeight = 180f;
        float aspect = texture != null && texture.height > 0 ? texture.width / (float)texture.height : 0.25f;
        float width = Mathf.Min(CardWidth - 40f, boxHeight * aspect);

        var imageRect = image.rectTransform;
        imageRect.anchorMin = imageRect.anchorMax = new Vector2(0.5f, 1f);
        imageRect.pivot = new Vector2(0.5f, 1f);
        imageRect.anchoredPosition = new Vector2(0f, -14f);
        imageRect.sizeDelta = new Vector2(width, boxHeight);

        var nameLabel = CreateText(rect, "Name", 27, TextAnchor.MiddleCenter, Color.white);
        var nameRect = nameLabel.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.pivot = new Vector2(0.5f, 0f);
        nameRect.anchoredPosition = new Vector2(0f, 62f);
        nameRect.sizeDelta = new Vector2(-12f, 30f);
        nameLabel.text = sword.name;

        var statsLabel = CreateText(rect, "Stats", 20, TextAnchor.UpperCenter, new Color(0.70f, 0.86f, 1f));
        var statsRect = statsLabel.rectTransform;
        statsRect.anchorMin = new Vector2(0f, 0f);
        statsRect.anchorMax = new Vector2(1f, 0f);
        statsRect.pivot = new Vector2(0.5f, 0f);
        statsRect.anchoredPosition = new Vector2(0f, 10f);
        statsRect.sizeDelta = new Vector2(-12f, 52f);
        statsLabel.text = $"ATK {sword.stats.attack}  DEF {sword.stats.defense}\nSPD {sword.stats.speed}  REACH {sword.stats.reach:0.0}";

        return rect;
    }

    // ---------- 進行 ----------

    void HandlePhaseChanged(DuelManager.Phase phase)
    {
        bool visible = phase == DuelManager.Phase.Loading || phase == DuelManager.Phase.Select;
        _canvas.enabled = visible;

        if (phase == DuelManager.Phase.Loading)
        {
            _title.text = "剣をよみこみ中...";
            _status.text = "";
        }
        else if (phase == DuelManager.Phase.Select)
        {
            _title.text = "剣をえらべ";
            BuildCards();
            UpdateStatus();
        }
    }

    void HandleSelectionChanged(int playerIndex, SwordData data)
    {
        UpdateStatus();
        UpdateCursors();
    }

    void UpdateStatus()
    {
        if (_duel.Swords.Count == 0)
        {
            _status.text = "剣がありません";
            return;
        }

        string p1 = _duel.IsSelected(0) ? $"けってい ({_duel.GetSelected(0).name})" : "せんたく中";
        string p2 = _duel.IsSelected(1) ? $"けってい ({_duel.GetSelected(1).name})" : "せんたく中";

        _status.text =
            $"1P: {p1}      2P: {p2}\n" +
            "左右=カーソル移動   ×=けってい   ○=とりけし\n" +
            "キーボード(代用): 1P = A/D・F・G   2P = ←/→・.・/";
    }

    void UpdateCursors()
    {
        for (int i = 0; i < 2; i++)
        {
            if (_cursors[i] == null) continue;

            int index = Mathf.Clamp(_cursorIndex[i], 0, Mathf.Max(0, _cards.Count - 1));
            if (index < _cards.Count)
            {
                _cursors[i].anchoredPosition = _cards[index].anchoredPosition;
            }

            // 決定済みは塗りつぶし、選択中は半透明
            var image = _cursors[i].GetComponent<RawImage>();
            Color color = PlayerColors[i];
            color.a = _duel.IsSelected(i) ? 1f : 0.55f;
            image.color = color;
        }
    }

    void Update()
    {
        if (_duel == null || _duel.Current != DuelManager.Phase.Select) return;
        if (_cards.Count == 0) return;

        for (int player = 0; player < 2; player++)
        {
            ReadInput(player, out int step, out bool decide, out bool cancel);

            if (!_duel.IsSelected(player) && step != 0)
            {
                _cursorIndex[player] = (_cursorIndex[player] + step + _cards.Count) % _cards.Count;
                UpdateCursors();
            }

            if (decide && !_duel.IsSelected(player))
            {
                _duel.SelectSword(player, _duel.Swords[_cursorIndex[player]]);
            }
            else if (cancel && _duel.IsSelected(player))
            {
                _duel.CancelSelection(player);
            }
        }
    }

    void ReadInput(int playerIndex, out int step, out bool decide, out bool cancel)
    {
        step = 0;
        decide = false;
        cancel = false;

        var pads = Gamepad.all;
        if (playerIndex < pads.Count)
        {
            Gamepad pad = pads[playerIndex];

            if (pad.dpad.left.wasPressedThisFrame) step = -1;
            if (pad.dpad.right.wasPressedThisFrame) step = 1;

            // スティックは倒しっぱなしで連続移動しないよう、しきい値を跨いだ瞬間だけ拾う
            float x = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(x) > 0.6f)
            {
                if (!_stickLatched[playerIndex])
                {
                    step = x < 0f ? -1 : 1;
                    _stickLatched[playerIndex] = true;
                }
            }
            else if (Mathf.Abs(x) < 0.3f)
            {
                _stickLatched[playerIndex] = false;
            }

            // □ は対戦中のチャージボタンなので、決定には割り当てない
            // （選択直後に押しっぱなしのまま対戦が始まると勝手にチャージしてしまう）
            decide = pad.buttonSouth.wasPressedThisFrame;
            cancel = pad.buttonEast.wasPressedThisFrame;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (playerIndex == 0)
        {
            if (kb.aKey.wasPressedThisFrame) step = -1;
            if (kb.dKey.wasPressedThisFrame) step = 1;
            decide = kb.fKey.wasPressedThisFrame;
            cancel = kb.gKey.wasPressedThisFrame;
        }
        else if (playerIndex == 1)
        {
            if (kb.leftArrowKey.wasPressedThisFrame) step = -1;
            if (kb.rightArrowKey.wasPressedThisFrame) step = 1;
            decide = kb.periodKey.wasPressedThisFrame;
            cancel = kb.slashKey.wasPressedThisFrame;
        }
    }

    // ---------- uGUI の小道具 ----------

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>Image は sprite 未設定だと描画されないので RawImage を使う。</summary>
    static RawImage CreateRawImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<RawImage>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Text CreateText(Transform parent, string name, int fontSize, TextAnchor alignment, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var text = go.AddComponent<Text>();
        text.font = GetFont();
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.8f, -1.8f);
        return text;
    }

    static Font GetFont()
    {
        if (_font == null)
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo" }, 48);
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        return _font;
    }
}
