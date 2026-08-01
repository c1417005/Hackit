using System;

/// <summary>
/// サーバー（Supabase）と共有するデータモデル。
/// フィールド名は JSON のキーと完全に一致させること。勝手に変えるとチーム間の契約が壊れる。
/// </summary>
[Serializable]
public class SwordData
{
    public string id;
    public string name;
    public string image_url;
    public SwordStats stats;
    public string created_at;
}

/// <summary>
/// attack / defense / speed は合計120ポイントの配分値。reach は剣の長さ倍率（0.8〜1.5想定）。
/// </summary>
[Serializable]
public class SwordStats
{
    public int attack;
    public int defense;
    public int speed;
    public float reach = 1f;

    public SwordStats() { }

    public SwordStats(int attack, int defense, int speed, float reach)
    {
        this.attack = attack;
        this.defense = defense;
        this.speed = speed;
        this.reach = reach;
    }
}

/// <summary>
/// JsonUtility はトップレベルが配列の JSON をパースできない。
/// Supabase は [{...}] を返すので、これで包んでからパースする。
/// </summary>
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
