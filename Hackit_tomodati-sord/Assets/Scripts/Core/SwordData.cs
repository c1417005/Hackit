using System;

/// <summary>Local APIとUnityで共有する剣データ。</summary>
[Serializable]
public class SwordData
{
    public string id;
    public string name;
    public string image_url;
    public string audio_url;
    public SwordStats stats;
    public string created_at;
}

/// <summary>
/// 戦闘ステータスはattack / speed。
/// height_cmだけからTポーズモデル全長と攻撃範囲を決定する。
/// </summary>
[Serializable]
public class SwordStats
{
    public int attack;
    public int speed;
    public float height_cm;

    public SwordStats() { }

    public SwordStats(int attack, int speed, float heightCm)
    {
        this.attack = attack;
        this.speed = speed;
        height_cm = heightCm;
    }
}

/// <summary>JsonUtilityでトップレベル配列を読むためのラッパー。</summary>
[Serializable]
public class SwordListWrapper
{
    public SwordData[] items;

    public static SwordListWrapper FromJsonArray(string jsonArray)
    {
        if (string.IsNullOrEmpty(jsonArray))
        {
            return new SwordListWrapper { items = Array.Empty<SwordData>() };
        }

        return UnityEngine.JsonUtility.FromJson<SwordListWrapper>("{\"items\":" + jsonArray + "}");
    }
}
