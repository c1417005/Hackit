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
/// 戦闘ステータスは attack / speed。現在のモデルでは reach を長さに使用する。
/// </summary>
[Serializable]
public class SwordStats
{
    public int attack;
    public int speed;
    public float reach = 1f;

    public SwordStats() { }

    public SwordStats(int attack, int speed, float reach)
    {
        this.attack = attack;
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
