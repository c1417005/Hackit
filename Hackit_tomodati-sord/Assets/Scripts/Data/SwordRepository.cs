using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// backend/main.py のLocal APIから、完成済み人物モデルの情報を取得する。
/// 通信失敗時はデモを止めず、ローカルモックへフォールバックする。
/// </summary>
public class SwordRepository : MonoBehaviour
{
    public enum Source
    {
        Mock = 0,
        LocalApi = 1,
    }

    [Header("Local API")]
    public Source source = Source.LocalApi;
    public bool useMock;
    public string localApiBaseUrl = "http://127.0.0.1:8000";
    public int fetchLimit = 30;

    [Header("Mock fallback")]
    public int mockCount = 8;

    [Serializable]
    sealed class LocalPerson
    {
        public int id;
        public string name;
        public float height;
        public int attack;
        public int speed;
        public string after_url;
        public string audio_url;
        public string voice_url;
        public string created_at;
    }

    [Serializable]
    sealed class LocalPersonList
    {
        public LocalPerson[] persons;
    }

    public IEnumerator FetchSwords(Action<List<SwordData>> onDone)
    {
        if (useMock || source == Source.Mock)
        {
            onDone?.Invoke(CreateMockSwords(mockCount));
            yield break;
        }

        string url = AssetUrl("/api/persons");
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[LocalAPI] 一覧取得失敗: {request.error} / mockへフォールバック");
                onDone?.Invoke(CreateMockSwords(mockCount));
                yield break;
            }

            List<SwordData> swords = ParsePersons(request.downloadHandler.text);
            if (swords.Count == 0)
            {
                Debug.LogWarning("[LocalAPI] 完成済みモデルが0件 / mockへフォールバック");
                swords = CreateMockSwords(mockCount);
            }
            onDone?.Invoke(swords);
        }
    }

    public IEnumerator FetchTexture(SwordData data, Action<Texture2D> onDone)
    {
        if (data == null || string.IsNullOrEmpty(data.image_url))
        {
            onDone?.Invoke(GenerateSwordTexture(StableHash(data?.id)));
            yield break;
        }

        string url = AssetUrl(data.image_url);
        using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[LocalAPI] 画像取得失敗: {url} / {request.error}");
                onDone?.Invoke(GenerateSwordTexture(StableHash(data.id)));
                yield break;
            }

            Texture2D texture = DownloadHandlerTexture.GetContent(request);
            texture.wrapMode = TextureWrapMode.Clamp;
            onDone?.Invoke(texture);
        }
    }

    /// <summary>
    /// バックエンドが人物ごとに返す音声URLをAudioClip化する。
    /// 音声がない・取得失敗の場合はnullで続行する。
    /// </summary>
    public IEnumerator FetchVoice(SwordData data, Action<AudioClip> onDone)
    {
        if (data == null || string.IsNullOrEmpty(data.audio_url))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string url = AssetUrl(data.audio_url);
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN))
        {
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[LocalAPI] 音声取得失敗: {url} / {request.error}");
                onDone?.Invoke(null);
                yield break;
            }

            onDone?.Invoke(DownloadHandlerAudioClip.GetContent(request));
        }
    }

    public IEnumerator PostMatch(string winnerId, string loserId, Action<bool> onDone = null)
    {
        // 現在のmain.pyに戦績APIはない。対戦フローを止めず成功扱いにする。
        Debug.Log($"[LocalAPI] match finished: winner={winnerId}, loser={loserId}");
        onDone?.Invoke(true);
        yield break;
    }

    List<SwordData> ParsePersons(string json)
    {
        var swords = new List<SwordData>();
        try
        {
            LocalPersonList response = JsonUtility.FromJson<LocalPersonList>(json);
            if (response?.persons == null) return swords;

            // The API returns newest entries first. Repeated submissions of the same
            // person create separate SQLite rows, so keep only the newest matching row
            // in the armory without deleting any source data.
            var seenPeople = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (LocalPerson person in response.persons)
            {
                if (person == null || string.IsNullOrEmpty(person.after_url)) continue;
                if (!seenPeople.Add(PersonIdentityKey(person))) continue;

                string id = person.id.ToString();
                string voice = !string.IsNullOrEmpty(person.audio_url)
                    ? person.audio_url
                    : person.voice_url;
                swords.Add(new SwordData
                {
                    id = id,
                    name = string.IsNullOrEmpty(person.name) ? $"FRIEND {id}" : person.name,
                    image_url = person.after_url,
                    audio_url = voice,
                    stats = new SwordStats(person.attack, person.speed, person.height),
                    created_at = person.created_at,
                });
                count++;
                if (count >= fetchLimit) break;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[LocalAPI] /api/personsの解析失敗: " + e.Message);
        }
        return swords;
    }

    static string PersonIdentityKey(LocalPerson person)
    {
        string normalizedName = (person.name ?? string.Empty).Trim();
        int heightTenths = Mathf.RoundToInt(person.height * 10f);
        return $"{normalizedName}\u001f{heightTenths}\u001f{person.attack}\u001f{person.speed}";
    }

    string AssetUrl(string pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return string.Empty;
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _)) return pathOrUrl;
        string baseUrl = string.IsNullOrEmpty(localApiBaseUrl)
            ? "http://127.0.0.1:8000"
            : localApiBaseUrl.TrimEnd('/');
        return pathOrUrl.StartsWith("/") ? baseUrl + pathOrUrl : baseUrl + "/" + pathOrUrl;
    }

    static readonly string[] MockNames =
    {
        "たけしの剣", "ゆうこの剣", "けんたの剣", "みさきの剣",
        "しょうごの剣", "あやのの剣", "だいちの剣", "りんの剣",
    };

    public static List<SwordData> CreateMockSwords(int count)
    {
        count = Mathf.Clamp(count, 1, MockNames.Length);
        var result = new List<SwordData>(count);
        var random = new System.Random(20260801);
        for (int i = 0; i < count; i++)
        {
            result.Add(new SwordData
            {
                id = $"mock-{i:0000}",
                name = MockNames[i],
                image_url = string.Empty,
                audio_url = string.Empty,
                stats = new SwordStats(
                    random.Next(25, 61),
                    random.Next(25, 71),
                    Mathf.Round(random.Next(1450, 1901)) / 10f),
                created_at = "2026-08-01T12:00:00Z",
            });
        }
        return result;
    }

    /// <summary>通信なしでもTポーズ生成を確認できる簡易人物シルエット。</summary>
    public static Texture2D GenerateSwordTexture(int seed)
    {
        const int width = 96;
        const int height = 256;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[width * height];
        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        var random = new System.Random(seed);
        Color shirt = Color.HSVToRGB((float)random.NextDouble(), 0.48f, 0.92f);
        Color pants = Color.HSVToRGB((float)random.NextDouble(), 0.42f, 0.48f);
        Color skin = new Color(0.94f, 0.72f, 0.58f, 1f);
        PaintRect(pixels, width, height, 11, 165, 85, 182, shirt); // Tポーズの腕
        PaintRect(pixels, width, height, 34, 83, 62, 178, shirt);  // 胴
        PaintRect(pixels, width, height, 35, 35, 47, 91, pants);   // 左脚
        PaintRect(pixels, width, height, 50, 35, 62, 91, pants);   // 右脚
        PaintCircle(pixels, width, height, 48, 209, 16, skin);     // 頭
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static void PaintRect(Color[] pixels, int width, int height, int x0, int y0, int x1, int y1, Color color)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(height, y1); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(width, x1); x++)
                pixels[y * width + x] = color;
    }

    static void PaintCircle(Color[] pixels, int width, int height, int cx, int cy, int radius, Color color)
    {
        int radiusSquared = radius * radius;
        for (int y = Mathf.Max(0, cy - radius); y < Mathf.Min(height, cy + radius); y++)
            for (int x = Mathf.Max(0, cx - radius); x < Mathf.Min(width, cx + radius); x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radiusSquared)
                    pixels[y * width + x] = color;
    }

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
