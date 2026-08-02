using UnityEngine;

/// <summary>人物の身長から選択するTポーズ剣の雛型サイズ。</summary>
public enum TposeSwordHeightClass
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// Tポーズ剣を組み立てるための雛型設定。
/// メッシュそのものをDBへ保存せず、DBの数値からこの設定を毎回選択する。
/// </summary>
public struct TposeSwordTemplateProfile
{
    public TposeSwordHeightClass heightClass;
    public string displayName;
    public string testTextureResourcePath;

    /// <summary>頭頂から頭の中心までを、人物全高に対する割合で表した値。</summary>
    public float headCenterBelowTopRatio;

    /// <summary>頭から足までの長さに対して許可するTポーズ横幅。超えた場合はX方向だけ縮める。</summary>
    public float maxWidthToBladeLength;

    /// <summary>モデル全高に対する奥行き。</summary>
    public float depthToHeightRatio;

    /// <summary>テスト画像およびOpenCV出力で確保したい左右の透明余白。</summary>
    public float requiredHorizontalPadding;

    /// <summary>テスト画像およびOpenCV出力で確保したい上下の透明余白。</summary>
    public float requiredVerticalPadding;
}

/// <summary>
/// 身長ステータスから大・中・小の雛型を選ぶ。
/// </summary>
public static class TposeSwordTemplateSelector
{
    public const float SmallMaximumHeightCm = 160f;
    public const float MediumMaximumHeightCm = 180f;
    public const float DefaultHeightCm = 170f;
    public const float MinimumSupportedHeightCm = 100f;
    public const float MaximumSupportedHeightCm = 250f;

    static readonly TposeSwordTemplateProfile SmallProfile = new TposeSwordTemplateProfile
    {
        heightClass = TposeSwordHeightClass.Small,
        displayName = "小",
        testTextureResourcePath = "FriendSword/Templates/TposeSmall",
        headCenterBelowTopRatio = 0.070f,
        maxWidthToBladeLength = 1.38f,
        depthToHeightRatio = 0.090f,
        requiredHorizontalPadding = 0.12f,
        requiredVerticalPadding = 0.07f,
    };

    static readonly TposeSwordTemplateProfile MediumProfile = new TposeSwordTemplateProfile
    {
        heightClass = TposeSwordHeightClass.Medium,
        displayName = "中",
        testTextureResourcePath = "FriendSword/Templates/TposeMedium",
        headCenterBelowTopRatio = 0.072f,
        maxWidthToBladeLength = 1.42f,
        depthToHeightRatio = 0.105f,
        requiredHorizontalPadding = 0.12f,
        requiredVerticalPadding = 0.07f,
    };

    static readonly TposeSwordTemplateProfile LargeProfile = new TposeSwordTemplateProfile
    {
        heightClass = TposeSwordHeightClass.Large,
        displayName = "大",
        testTextureResourcePath = "FriendSword/Templates/TposeLarge",
        headCenterBelowTopRatio = 0.074f,
        maxWidthToBladeLength = 1.46f,
        depthToHeightRatio = 0.120f,
        requiredHorizontalPadding = 0.12f,
        requiredVerticalPadding = 0.07f,
    };

    public static TposeSwordTemplateProfile Select(SwordData data)
    {
        return SelectFromHeightCm(ResolveHeightCm(data));
    }

    /// <summary>モデル生成に使用する身長。欠損時は標準身長を使い、異常値だけ安全範囲へ収める。</summary>
    public static float ResolveHeightCm(SwordData data)
    {
        SwordStats stats = data != null ? data.stats : null;
        if (stats == null) return DefaultHeightCm;

        float heightCm = stats.height_cm;
        if (heightCm > 0f && !float.IsNaN(heightCm) && !float.IsInfinity(heightCm))
        {
            return Mathf.Clamp(heightCm, MinimumSupportedHeightCm, MaximumSupportedHeightCm);
        }

        return DefaultHeightCm;
    }

    public static TposeSwordTemplateProfile SelectFromHeightCm(float heightCm)
    {
        if (heightCm <= 0f || float.IsNaN(heightCm) || float.IsInfinity(heightCm))
        {
            heightCm = DefaultHeightCm;
        }

        if (heightCm < SmallMaximumHeightCm) return SmallProfile;
        if (heightCm < MediumMaximumHeightCm) return MediumProfile;
        return LargeProfile;
    }

}
