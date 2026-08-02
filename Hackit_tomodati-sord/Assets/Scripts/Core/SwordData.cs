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
/// attack / defense / speed は合計120ポイントの配分値。
/// height_cm は撮影した人物の身長で、モデルの長さと攻撃範囲の基準になる。
/// </summary>
[Serializable]
public class SwordStats
{
    public int attack;
    public int defense;
    public int speed;
    public float height_cm;

    public SwordStats() { }

    public SwordStats(int attack, int defense, int speed, float heightCm)
    {
        this.attack = attack;
        this.defense = defense;
        this.speed = speed;
        height_cm = heightCm;
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
