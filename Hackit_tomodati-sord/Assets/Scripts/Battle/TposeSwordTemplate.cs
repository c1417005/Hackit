using UnityEngine;

/// <summary>
/// すべての身長で共通使用するTポーズ剣の生成設定。
/// 縦の長さだけは身長から連続的に計算し、ここでは形状比率を定義する。
/// </summary>
public struct TposeSwordTemplateProfile
{
    /// <summary>頭頂から頭の中心までを、人物全高に対する割合で表した値。</summary>
    public float headCenterBelowTopRatio;

    /// <summary>頭から足までの長さに対して許可するTポーズ横幅。超えた場合はX方向だけ縮める。</summary>
    public float maxWidthToModelLength;

    /// <summary>モデル全高に対する奥行き。</summary>
    public float depthToHeightRatio;

    /// <summary>テスト画像およびOpenCV出力で確保したい左右の透明余白。</summary>
    public float requiredHorizontalPadding;

    /// <summary>テスト画像およびOpenCV出力で確保したい上下の透明余白。</summary>
    public float requiredVerticalPadding;
}

/// <summary>身長の正規化と共通生成設定を提供する。</summary>
public static class TposeSwordTemplateSettings
{
    public const float DefaultHeightCm = 170f;
    public const float MinimumSupportedHeightCm = 100f;
    public const float MaximumSupportedHeightCm = 250f;

    static readonly TposeSwordTemplateProfile SharedProfile = new TposeSwordTemplateProfile
    {
        headCenterBelowTopRatio = 0.072f,
        maxWidthToModelLength = 1.42f,
        depthToHeightRatio = 0.105f,
        requiredHorizontalPadding = 0.12f,
        requiredVerticalPadding = 0.07f,
    };

    public static TposeSwordTemplateProfile Profile => SharedProfile;

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
}
