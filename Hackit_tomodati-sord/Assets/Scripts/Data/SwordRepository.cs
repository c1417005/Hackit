using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 剣データの取得元。
///
/// `source` で3つを切り替える。どれで失敗しても最後はモックに落ちるので、
/// デモが止まることはない。
/// </summary>
public class SwordRepository : MonoBehaviour
{
    public enum Source
    {
        /// <summary>通信もファイルアクセスもしない。手続き生成のモック</summary>
        Mock,

        /// <summary>StreamingAssets のローカル SQLite</summary>
        Sqlite,

        /// <summary>Supabase REST API</summary>
        Supabase,
    }

    [Header("接続先")]
    public Source source = Source.Sqlite;

    [Tooltip("後方互換。ONだと source を無視して Mock になる")]
    public bool useMock;

    [Header("SQLite")]
    [Tooltip("StreamingAssets からの相対パス")]
    public string sqliteFileName = "tomodachi_sword.db";

    [Header("Supabase")]
    [Tooltip("例: https://xxxxx.supabase.co")]
    public string supabaseUrl = "";

    [Tooltip("anon key")]
    public string anonKey = "";

    public int fetchLimit = 30;

    [Header("モック")]
    public int mockCount = 8;

    /// <summary>StreamingAssets 内の DB の絶対パス。</summary>
    public string SqlitePath => Path.Combine(Application.streamingAssetsPath, sqliteFileName);

    Source ResolvedSource
    {
        get
        {
            if (useMock) return Source.Mock;

#if !UNITY_EDITOR
            // Windows標準のwinsqlite3をMonoから直接呼ぶと、読込完了後に
            // ネイティブクラッシュする環境がある。展示用PlayerではSQLiteを避け、
            // Supabase設定時だけ通信を使い、それまでは安全なモックで起動する。
            if (source == Source.Sqlite) return Source.Mock;
#endif

            return source;
        }
    }

    /// <summary>剣一覧を取得する。失敗してもモックを返すので onDone は必ず呼ばれる。</summary>
    public IEnumerator FetchSwords(Action<List<SwordData>> onDone)
    {
        if (ResolvedSource == Source.Mock)
        {
            onDone?.Invoke(CreateMockSwords(mockCount));
            yield break;
        }

        if (ResolvedSource == Source.Sqlite)
        {
            // SQLite は同期で読める。件数が知れているので待たせない。
            onDone?.Invoke(FetchSwordsFromSqlite());
            yield break;
        }

        if (string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(anonKey))
        {
            Debug.LogWarning("[SwordRepository] 接続先が未設定なのでモックを使う");
            onDone?.Invoke(CreateMockSwords(mockCount));
            yield break;
        }

        string url = $"{supabaseUrl}/rest/v1/swords?select=*&order=created_at.desc&limit={fetchLimit}";

        using (var request = UnityWebRequest.Get(url))
        {
            ApplyHeaders(request);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SwordRepository] 剣一覧の取得に失敗: {request.error} / モックにフォールバックする");
                onDone?.Invoke(CreateMockSwords(mockCount));
                yield break;
            }

            List<SwordData> swords = ParseSwordArray(request.downloadHandler.text);

            if (swords.Count == 0)
            {
                Debug.LogWarning("[SwordRepository] 剣が0件だったのでモックにフォールバックする");
                swords = CreateMockSwords(mockCount);
            }

            onDone?.Invoke(swords);
        }
    }

    /// <summary>
    /// 剣の画像を取得する。取れなければ手続き生成の剣を返すので、onDone は必ず非nullで呼ばれる。
    /// </summary>
    public IEnumerator FetchTexture(SwordData data, Action<Texture2D> onDone)
    {
        if (data == null)
        {
            onDone?.Invoke(GenerateSwordTexture(0));
            yield break;
        }

        // DBに画像が入っていればそれを最優先で使う
        Texture2D fromBlob = TakeBlobTexture(data.id);
        if (fromBlob != null)
        {
            onDone?.Invoke(fromBlob);
            yield break;
        }

        if (useMock || string.IsNullOrEmpty(data.image_url))
        {
            onDone?.Invoke(GenerateSwordTexture(StableHash(data.id)));
            yield break;
        }

        using (var request = UnityWebRequestTexture.GetTexture(data.image_url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SwordRepository] 画像の取得に失敗: {data.image_url} / {request.error}");
                onDone?.Invoke(GenerateSwordTexture(StableHash(data.id)));
                yield break;
            }

            var texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            onDone?.Invoke(texture);
        }
    }

    /// <summary>戦績を送る。失敗しても対戦の進行は止めない。</summary>
    public IEnumerator PostMatch(string winnerId, string loserId, Action<bool> onDone = null)
    {
        if (ResolvedSource == Source.Sqlite)
        {
            onDone?.Invoke(WriteMatchToSqlite(winnerId, loserId));
            yield break;
        }

        if (ResolvedSource == Source.Mock || string.IsNullOrEmpty(supabaseUrl) || string.IsNullOrEmpty(anonKey))
        {
            Debug.Log($"[SwordRepository] (mock) 戦績送信: winner={winnerId} loser={loserId}");
            onDone?.Invoke(true);
            yield break;
        }

        string url = $"{supabaseUrl}/rest/v1/matches";
        string body = $"{{\"winner_id\":\"{winnerId}\",\"loser_id\":\"{loserId}\"}}";

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            ApplyHeaders(request);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Prefer", "return=minimal");

            yield return request.SendWebRequest();

            bool ok = request.result == UnityWebRequest.Result.Success;
            if (!ok)
            {
                Debug.LogWarning($"[SwordRepository] 戦績送信に失敗: {request.error}");
            }

            onDone?.Invoke(ok);
        }
    }

    // ---------- SQLite ----------

    /// <summary>id -> 画像PNGのバイト列。FetchSwordsFromSqlite で拾ったぶん。</summary>
    readonly Dictionary<string, byte[]> _blobCache = new Dictionary<string, byte[]>();
    // audio_url は現行の必須契約外だが、Web側が列を追加した場合に利用する任意メタデータ。
    readonly Dictionary<string, string> _audioUrlCache = new Dictionary<string, string>();

    public string GetAudioUrl(string swordId)
    {
        return !string.IsNullOrEmpty(swordId) && _audioUrlCache.TryGetValue(swordId, out string url)
            ? url
            : string.Empty;
    }

    /// <summary>登録された音声URLがあれば取得する。音声なし・失敗はnullで続行する。</summary>
    public IEnumerator FetchVoice(SwordData data, Action<AudioClip> onDone)
    {
        string url = data != null ? GetAudioUrl(data.id) : string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SwordRepository] 音声の取得に失敗: {url} / {request.error}");
                onDone?.Invoke(null);
                yield break;
            }

            onDone?.Invoke(DownloadHandlerAudioClip.GetContent(request));
        }
    }

    /// <summary>DBに入っていた画像をテクスチャにする。無ければ null。</summary>
    public Texture2D TakeBlobTexture(string swordId)
    {
        if (string.IsNullOrEmpty(swordId)) return null;
        if (!_blobCache.TryGetValue(swordId, out byte[] png) || png == null) return null;

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(png))
        {
            Debug.LogWarning($"[SwordRepository] 画像として読めなかった: {swordId}");
            Destroy(texture);
            return null;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    /// <summary>
    /// knownIds に無い剣が入っていれば返す。Webからのアップロードを待つのに使う。
    /// 複数増えていたら一番新しいものを返す。
    /// </summary>
    public SwordData FindNewSword(HashSet<string> knownIds)
    {
        if (ResolvedSource != Source.Sqlite) return null;

        try
        {
            List<SwordData> all = FetchSwordsFromSqlite();
            foreach (SwordData sword in all)   // created_at DESC で並んでいる
            {
                if (sword != null && !string.IsNullOrEmpty(sword.id) && !knownIds.Contains(sword.id))
                {
                    return sword;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SwordRepository] 新着の確認に失敗: {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// ローカルの SQLite から剣を読む。
    /// 開けない・0件・例外、どの場合もモックに落として onDone は必ず埋める。
    /// </summary>
    public List<SwordData> FetchSwordsFromSqlite()
    {
        string path = SqlitePath;

        if (!File.Exists(path))
        {
            Debug.LogWarning($"[SwordRepository] DBが無い: {path} / モックにフォールバックする");
            return CreateMockSwords(mockCount);
        }

        try
        {
            using (var db = new Sqlite(path))
            {
                // image は無いDBもあり得るので SELECT * で拾う
                List<Sqlite.Row> rows = db.Query(
                    "SELECT * FROM swords ORDER BY created_at DESC LIMIT ?", fetchLimit);

                var swords = new List<SwordData>(rows.Count);
                foreach (Sqlite.Row row in rows)
                {
                    var sword = new SwordData
                    {
                        id = row.GetString("id"),
                        name = row.GetString("name"),
                        image_url = row.GetString("image_url"),
                        created_at = row.GetString("created_at"),
                        stats = new SwordStats(
                            row.GetInt("attack", 40),
                            row.GetInt("defense", 40),
                            row.GetInt("speed", 40),
                            row.GetFloat("reach", 1f)),
                    };

                    // 画像がBLOBで入っていればここで持って帰る
                    byte[] png = row.GetBlob("image");
                    if (png != null && png.Length > 0)
                    {
                        _blobCache[sword.id] = png;
                    }

                    string audioUrl = row.GetString("audio_url");
                    if (!string.IsNullOrEmpty(audioUrl)) _audioUrlCache[sword.id] = audioUrl;

                    swords.Add(sword);
                }

                if (swords.Count == 0)
                {
                    Debug.LogWarning("[SwordRepository] SQLiteが0件だったのでモックにフォールバックする");
                    return CreateMockSwords(mockCount);
                }

                Debug.Log($"[SwordRepository] SQLiteから{swords.Count}件読んだ ({Sqlite.BackendName})");
                return swords;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SwordRepository] SQLiteの読み取りに失敗: {e.Message} / モックにフォールバックする");
            return CreateMockSwords(mockCount);
        }
    }

    /// <summary>戦績を SQLite に書く。失敗しても false を返すだけで進行は止めない。</summary>
    public bool WriteMatchToSqlite(string winnerId, string loserId)
    {
        try
        {
            using (var db = new Sqlite(SqlitePath))
            {
                db.Execute("INSERT INTO matches (winner_id, loser_id) VALUES (?, ?)", winnerId, loserId);
                Debug.Log($"[SwordRepository] 戦績を記録: winner={winnerId} loser={loserId}");
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SwordRepository] 戦績の記録に失敗: {e.Message}");
            return false;
        }
    }

    void ApplyHeaders(UnityWebRequest request)
    {
        request.SetRequestHeader("apikey", anonKey);
        request.SetRequestHeader("Authorization", "Bearer " + anonKey);
    }

    /// <summary>
    /// JsonUtility はトップレベルが配列のJSONをパースできないので、
    /// SwordListWrapper で包んでから読む。
    /// </summary>
    static List<SwordData> ParseSwordArray(string json)
    {
        var list = new List<SwordData>();

        try
        {
            SwordListWrapper wrapper = SwordListWrapper.FromJsonArray(json);
            if (wrapper?.items != null)
            {
                foreach (var sword in wrapper.items)
                {
                    if (sword != null) list.Add(sword);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SwordRepository] JSONのパースに失敗: " + e.Message);
        }

        return list;
    }

    // ---------- モック ----------

    static readonly string[] MockNames =
    {
        "たけしの剣", "ゆうこの剣", "けんたの剣", "みさきの剣",
        "しょうごの剣", "あやのの剣", "だいちの剣", "りんの剣",
        "そうまの剣", "なぎさの剣",
    };

    /// <summary>ステータスは実際のデータ契約どおり attack+defense+speed = 120 になるよう配る。</summary>
    public static List<SwordData> CreateMockSwords(int count)
    {
        var list = new List<SwordData>();
        count = Mathf.Clamp(count, 1, MockNames.Length);

        // 毎回同じ並びになるよう固定シードを使う
        var random = new System.Random(20260801);

        for (int i = 0; i < count; i++)
        {
            int attack = random.Next(25, 61);
            int defense = random.Next(25, Mathf.Max(26, 121 - attack - 25));
            int speed = 120 - attack - defense;

            list.Add(new SwordData
            {
                id = $"mock-{i:0000}",
                name = MockNames[i],
                image_url = "",
                stats = new SwordStats(attack, defense, speed, Mathf.Round(random.Next(80, 151)) / 100f),
                created_at = "2026-08-01T12:00:00Z",
            });
        }

        return list;
    }

    /// <summary>
    /// 剣のシルエットを手続き的に生成する。切り抜きPNGと同じく外側はアルファ0。
    /// 本番では Supabase から落とした画像がここに入る。
    /// seed で刃と柄の色が変わる。
    /// </summary>
    public static Texture2D GenerateSwordTexture(int seed)
    {
        const int w = 64;
        const int h = 256;

        var random = new System.Random(seed);
        var bladeColor = Color.HSVToRGB((float)random.NextDouble(), 0.25f, 0.95f);
        var gripColor = Color.HSVToRGB((float)random.NextDouble(), 0.55f, 0.45f);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var clear = new Color(0f, 0f, 0f, 0f);
        var pixels = new Color[w * h];

        for (int y = 0; y < h; y++)
        {
            float t = y / (float)(h - 1);
            float halfWidth;
            Color color;

            // 剣ではなく「人」のシルエットを描く。
            // このゲームは人の全身写真がそのまま武器になるので、
            // モックも剣の形にしてしまうとコンセプトが伝わらない。
            // 下(t=0)が足、上(t=1)が頭。
            if (t < 0.42f)                      // 脚
            {
                halfWidth = 0.11f * w;
                color = gripColor;              // ズボン
            }
            else if (t < 0.50f)                 // 腰
            {
                halfWidth = 0.15f * w;
                color = gripColor;
            }
            else if (t < 0.74f)                 // 胴と腕
            {
                halfWidth = 0.26f * w;
                color = bladeColor;             // 服
            }
            else if (t < 0.80f)                 // 肩から首
            {
                float shoulderT = Mathf.InverseLerp(0.74f, 0.80f, t);
                halfWidth = Mathf.Lerp(0.24f, 0.07f, shoulderT) * w;
                color = bladeColor;
            }
            else                                // 頭
            {
                float headT = Mathf.InverseLerp(0.80f, 1f, t);
                // 上下が細い楕円にして頭らしく見せる
                halfWidth = 0.16f * Mathf.Sin(headT * Mathf.PI) * w + 0.02f * w;
                color = Color.Lerp(new Color(0.95f, 0.78f, 0.64f), new Color(0.35f, 0.24f, 0.18f), headT * headT);
            }

            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x - (w - 1) * 0.5f);
                int index = y * w + x;

                if (dx > halfWidth)
                {
                    pixels[index] = clear;
                    continue;
                }

                // 中心を明るく、縁を暗くして立体感を出す
                float edge = halfWidth <= 0.001f ? 0f : dx / halfWidth;
                pixels[index] = Color.Lerp(color, color * 0.55f, edge * edge);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>実行ごとに変わらないハッシュ。string.GetHashCode は保証がないので自前で持つ。</summary>
    static int StableHash(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        unchecked
        {
            int hash = 17;
            foreach (char c in value) hash = hash * 31 + c;
            return hash & 0x7fffffff;
        }
    }
}
