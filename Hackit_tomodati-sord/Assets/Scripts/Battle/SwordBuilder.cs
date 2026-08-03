using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 画像 + ステータスから剣の見た目を生成する。
///
/// 階層:
///   SwordRoot(握り = 回転の中心)
///     └ Blade   … 切り抜き画像の輪郭を押し出した3Dメッシュ
///
/// 人物の頭付近がSwordRootの原点になり、胴体と脚が剣先方向へ伸びる。
/// 画像が取得できなかった場合だけ、従来の板ポリへフォールバックする。
///
/// 当たり判定はここでは作らない。Fighter側がMetricsを見て3D Triggerを設定する。
/// </summary>
public static class SwordBuilder
{
    /// <summary>握りから刃元までの距離。ここが回転中心からの立ち上がり。</summary>
    const float GripOffset = 0.12f;

    /// <summary>170cmの人物を現在の標準表示と同じ約1.5 Unity単位にする。</summary>
    public const float HeightToModelLength = 1.5f / 170f;

    const float ReferenceModelLength = 1.5f;

    const float BladeWidth = 0.30f;

    /// <summary>刃の寸法。当たり判定を作る側がこれを見る。</summary>
    public struct Metrics
    {
        public float heightCm;
        public float modelLength;
        public float bladeLength;
        public float bladeWidth;

        /// <summary>SwordRoot から見た刃の中心までの距離。</summary>
        public float bladeCenterY;

        /// <summary>SwordRoot から刃先までの距離。斬撃範囲の半径になる。</summary>
        public float tipDistance;
    }

    public static Metrics GetMetrics(SwordData data)
    {
        float heightCm = TposeSwordTemplateSettings.ResolveHeightCm(data);
        float modelLength = heightCm * HeightToModelLength;
        float bladeLength = Mathf.Max(0.1f, modelLength - GripOffset);
        float modelScale = modelLength / ReferenceModelLength;

        return new Metrics
        {
            heightCm = heightCm,
            modelLength = modelLength,
            bladeLength = bladeLength,
            bladeWidth = BladeWidth * modelScale,
            bladeCenterY = GripOffset + bladeLength * 0.5f,
            tipDistance = modelLength,
        };
    }

    /// <summary>
    /// 剣の見た目を生成して parent の子にする。生成された SwordRoot を返す。
    /// texture が null でも（画像取得前・失敗時でも）形だけは出る。
    /// </summary>
    public static GameObject Build(SwordData data, Texture2D texture, Transform parent)
    {
        Metrics metrics = GetMetrics(data);
        float heightCm = metrics.heightCm;
        TposeSwordTemplateProfile profile = TposeSwordTemplateSettings.Profile;

        var root = new GameObject(data != null && !string.IsNullOrEmpty(data.name) ? data.name : "Sword");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        if (texture != null)
        {
            TposeSwordMeshBuilder.Create(texture, metrics, profile, heightCm, root.transform);
        }
        else
        {
            BuildFlatFallback(metrics, root.transform);
        }

        return root;
    }

    /// <summary>画像取得前・失敗時でも対戦を止めないための従来表示。</summary>
    static void BuildFlatFallback(Metrics metrics, Transform parent)
    {
        var blade = CreateMeshObject("Blade", PrimitiveType.Quad, parent);
        blade.transform.localPosition = new Vector3(0f, metrics.bladeCenterY, 0f);
        blade.transform.localScale = new Vector3(metrics.bladeWidth, metrics.bladeLength, 1f);
        blade.GetComponent<MeshRenderer>().sharedMaterial = CreateSwordMaterial(null);

        // Spine（薄い芯）は剣だった頃の名残。人のシルエットを縦に貫いて見えるので出さない。
        // 真横から見たときに消える問題は、カメラが横固定なので実際には起きない。
    }

    /// <summary>
    /// 見た目だけのメッシュを作る。
    /// CreatePrimitive はコライダー付きで生まれ、Destroy は遅延実行なので
    /// Rigidbody 配下だと1フレームだけ Concave Mesh Collider エラーが出る。それを避ける。
    /// </summary>
    static GameObject CreateMeshObject(string name, PrimitiveType type, Transform parent)
    {
        Mesh mesh = GetBuiltinMesh(type);

        if (mesh != null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        // 組み込みメッシュが取れない環境向けのフォールバック
        var primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        var collider = primitive.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        primitive.transform.SetParent(parent, false);
        return primitive;
    }

    static Mesh GetBuiltinMesh(PrimitiveType type)
    {
        switch (type)
        {
            case PrimitiveType.Quad: return Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            case PrimitiveType.Cube: return Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            default: return null;
        }
    }

    /// <summary>
    /// 剣がマゼンタ（ピンク）になる場合は、使っている描画パイプラインの
    /// Unlit シェーダー名をこの配列の先頭側に足すこと。
    /// </summary>
    static readonly string[] UnlitShaderCandidates =
    {
        "FriendSword/TposeAlphaCutout",
        "Universal Render Pipeline/Unlit",
        "Unlit/Transparent Cutout",
        "Unlit/Transparent",
        "Sprites/Default",
        "Unlit/Texture",
    };

    public static Material CreateSwordMaterial(Texture2D texture)
    {
        Shader shader = FindFirstAvailableShader();
        var mat = new Material(shader);

        SetTexture(mat, texture);
        SetColor(mat, Color.white);

        // 切り抜き PNG なのでアルファクリップ。半透明ソートの事故を避ける。
        if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.AlphaTest;

        // 左向きプレイヤーは剣ごと Y 反転させるので、裏面も描く必要がある
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.doubleSidedGI = true;

        return mat;
    }

    static Material CreateSolidMaterial(Color color)
    {
        var mat = new Material(FindFirstAvailableShader());
        SetColor(mat, color);
        return mat;
    }

    static Shader FindFirstAvailableShader()
    {
        // Resourcesに置いた専用シェーダーを明示的に読み込むことで、
        // 製品ビルドのシェーダー削除対象になって透明切り抜きが失われるのを防ぐ。
        Shader bundledShader = Resources.Load<Shader>("TposeAlphaCutout");
        if (bundledShader != null)
        {
            return bundledShader;
        }

        foreach (string name in UnlitShaderCandidates)
        {
            Shader shader = Shader.Find(name);
            if (shader != null)
            {
                return shader;
            }
        }

        Debug.LogWarning("[SwordBuilder] Unlit シェーダーが見つからない。SwordBuilder.UnlitShaderCandidates に名前を足すこと。");
        return Shader.Find("Standard");
    }

    static void SetTexture(Material mat, Texture2D texture)
    {
        if (texture == null) return;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
    }

    static void SetColor(Material mat, Color color)
    {
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
    }
}
