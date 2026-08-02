using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 画像 + ステータスから剣の見た目を生成する。
///
/// 階層:
///   SwordRoot(握り = 回転の中心)
///     ├ Blade   … 切り抜き画像を貼った板ポリ(Quad)
///     └ Spine   … 薄い芯。真横から見たときに厚みが出る
///
/// Quad のピボットは中心にあるため、そのまま回すと剣の真ん中を軸に回ってしまう。
/// SwordRoot を握りの位置に置き、その子の Blade を上方向にオフセットしてある。
///
/// 当たり判定はここでは作らない。Fighter側がMetricsを見て3D Triggerを設定する。
/// </summary>
public static class SwordBuilder
{
    /// <summary>握りから刃元までの距離。ここが回転中心からの立ち上がり。</summary>
    const float GripOffset = 0.12f;

    /// <summary>reach = 1.0 のときの刃の長さ。</summary>
    const float BaseBladeLength = 1.15f;

    const float BladeWidth = 0.30f;

    /// <summary>刃の寸法。当たり判定を作る側がこれを見る。</summary>
    public struct Metrics
    {
        public float bladeLength;
        public float bladeWidth;

        /// <summary>SwordRoot から見た刃の中心までの距離。</summary>
        public float bladeCenterY;

        /// <summary>SwordRoot から刃先までの距離。斬撃範囲の半径になる。</summary>
        public float tipDistance;
    }

    public static Metrics GetMetrics(SwordData data)
    {
        SwordStats stats = data != null && data.stats != null ? data.stats : new SwordStats(40, 40, 1f);
        float reach = Mathf.Clamp(stats.reach <= 0f ? 1f : stats.reach, 0.8f, 1.5f);
        float bladeLength = BaseBladeLength * reach;

        return new Metrics
        {
            bladeLength = bladeLength,
            bladeWidth = BladeWidth * reach,
            bladeCenterY = GripOffset + bladeLength * 0.5f,
            tipDistance = GripOffset + bladeLength,
        };
    }

    /// <summary>
    /// 剣の見た目を生成して parent の子にする。生成された SwordRoot を返す。
    /// texture が null でも（画像取得前・失敗時でも）形だけは出る。
    /// </summary>
    public static GameObject Build(SwordData data, Texture2D texture, Transform parent)
    {
        Metrics metrics = GetMetrics(data);

        var root = new GameObject(data != null && !string.IsNullOrEmpty(data.name) ? data.name : "Sword");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        // --- Blade : 板ポリ。カメラが横固定なのでこれで破綻しない ---
        var blade = CreateMeshObject("Blade", PrimitiveType.Quad, root.transform);
        blade.transform.localPosition = new Vector3(0f, metrics.bladeCenterY, 0f);
        blade.transform.localScale = new Vector3(metrics.bladeWidth, metrics.bladeLength, 1f);
        blade.GetComponent<MeshRenderer>().sharedMaterial = CreateSwordMaterial(texture);

        // Spine（薄い芯）は剣だった頃の名残。人のシルエットを縦に貫いて見えるので出さない。
        // 真横から見たときに消える問題は、カメラが横固定なので実際には起きない。

        return root;
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
        if (collider != null) Object.DestroyImmediate(collider);
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
