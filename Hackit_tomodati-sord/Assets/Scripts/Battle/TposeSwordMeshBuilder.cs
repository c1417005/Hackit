using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 背景透過済みTポーズ画像の輪郭を奥行き方向へ押し出し、
/// SampleSceneの対戦で使用する厚み付き3D剣を生成する。
/// </summary>
public static class TposeSwordMeshBuilder
{
    const int SilhouetteResolution = 160;
    const int AlphaThreshold = 128;
    const int BoundsAlphaThreshold = 16;

    struct AlphaBounds
    {
        public float minU;
        public float maxU;
        public float minV;
        public float maxV;

        public float width => maxU - minU;
        public float height => maxV - minV;
    }

    public static GameObject Create(
        Texture2D texture,
        SwordBuilder.Metrics metrics,
        TposeSwordTemplateProfile profile,
        float heightCm,
        Transform parent)
    {
        if (texture == null)
        {
            return null;
        }

        Color32[] pixels = GetReadablePixels(texture, out int pixelWidth, out int pixelHeight);
        Mesh mesh = BuildMesh(pixels, pixelWidth, pixelHeight, metrics, profile);
        mesh.name = "TposeSword3DMesh";

        var blade = new GameObject("Blade");
        blade.transform.SetParent(parent, false);

        // 元画像では頭から脚が下へ伸びる。剣では頭を握りにして脚を+Y（剣先）へ向ける。
        blade.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

        var filter = blade.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        Material frontMaterial = SwordBuilder.CreateSwordMaterial(texture);
        Material sideMaterial = CreateSideMaterial(texture);

        var renderer = blade.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { frontMaterial, sideMaterial };
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        var resources = blade.AddComponent<TposeSwordRuntimeResources>();
        resources.Configure(mesh, frontMaterial, sideMaterial);

        var modelInstance = blade.AddComponent<TposeSwordModelInstance>();
        modelInstance.Configure(heightCm);

        return blade;
    }

    static Color32[] GetReadablePixels(Texture2D texture, out int width, out int height)
    {
        width = texture.width;
        height = texture.height;

        if (texture.isReadable)
        {
            return texture.GetPixels32();
        }

        RenderTexture previous = RenderTexture.active;
        RenderTexture temporary = RenderTexture.GetTemporary(
            width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        try
        {
            Graphics.Blit(texture, temporary);
            RenderTexture.active = temporary;

            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readable.Apply(false, false);

            Color32[] pixels = readable.GetPixels32();
            Object.Destroy(readable);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    static Mesh BuildMesh(
        Color32[] pixels,
        int pixelWidth,
        int pixelHeight,
        SwordBuilder.Metrics metrics,
        TposeSwordTemplateProfile profile)
    {
        float aspect = pixelHeight > 0 ? (float)pixelWidth / pixelHeight : 1f;
        AlphaBounds contentBounds = FindAlphaBounds(pixels, pixelWidth, pixelHeight);
        ValidateSafePadding(contentBounds, profile);

        // 頭の中心を原点にし、足先が既存のtipDistanceへ届くようにYを正規化する。
        // 入力PNGごとに透明余白の量が違っても、人物本体の大きさは変わらない。
        float headV = contentBounds.maxV - contentBounds.height * profile.headCenterBelowTopRatio;
        float belowHead = Mathf.Max(0.2f, headV - contentBounds.minV);
        float imageWorldHeight = metrics.tipDistance / belowHead;
        float imageWorldWidth = imageWorldHeight * aspect;

        // 腕が極端に長い・横幅の大きい人物はX方向だけ安全幅へ収める。
        // 身長方向と顔の縦横比は維持し、横幅が必要な時だけ体と腕を圧縮する。
        float visibleWorldWidth = contentBounds.width * imageWorldWidth;
        float maximumVisibleWidth = metrics.tipDistance * profile.maxWidthToModelLength;
        if (visibleWorldWidth > maximumVisibleWidth)
        {
            imageWorldWidth *= maximumVisibleWidth / visibleWorldWidth;
        }

        float contentCenterU = (contentBounds.minU + contentBounds.maxU) * 0.5f;
        float modelDepth = Mathf.Max(0.08f, metrics.tipDistance * profile.depthToHeightRatio);
        int gridWidth;
        int gridHeight;

        if (aspect >= 1f)
        {
            gridWidth = SilhouetteResolution;
            gridHeight = Mathf.Max(1, Mathf.RoundToInt(SilhouetteResolution / aspect));
        }
        else
        {
            gridHeight = SilhouetteResolution;
            gridWidth = Mathf.Max(1, Mathf.RoundToInt(SilhouetteResolution * aspect));
        }

        bool[,] occupied = ReadSilhouette(pixels, pixelWidth, pixelHeight, gridWidth, gridHeight);

        float halfDepth = modelDepth * 0.5f;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var frontBackTriangles = new List<int>();
        var sideTriangles = new List<int>();

        float left = -contentCenterU * imageWorldWidth;
        float right = (1f - contentCenterU) * imageWorldWidth;
        float bottom = -headV * imageWorldHeight;
        float top = (1f - headV) * imageWorldHeight;

        AddFace(vertices, normals, uvs, frontBackTriangles,
            new Vector3(left, bottom, -halfDepth),
            new Vector3(left, top, -halfDepth),
            new Vector3(right, top, -halfDepth),
            new Vector3(right, bottom, -halfDepth),
            Vector3.back,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f));

        AddFace(vertices, normals, uvs, frontBackTriangles,
            new Vector3(left, bottom, halfDepth),
            new Vector3(right, bottom, halfDepth),
            new Vector3(right, top, halfDepth),
            new Vector3(left, top, halfDepth),
            Vector3.forward,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));

        for (int y = 0; y < gridHeight; y++)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                if (!occupied[x, y]) continue;

                float u0 = (float)x / gridWidth;
                float u1 = (float)(x + 1) / gridWidth;
                float v0 = (float)y / gridHeight;
                float v1 = (float)(y + 1) / gridHeight;
                Vector2 sampleUv = new Vector2((x + 0.5f) / gridWidth, (y + 0.5f) / gridHeight);

                float x0 = (u0 - contentCenterU) * imageWorldWidth;
                float x1 = (u1 - contentCenterU) * imageWorldWidth;
                float y0 = (v0 - headV) * imageWorldHeight;
                float y1 = (v1 - headV) * imageWorldHeight;

                if (!IsOccupied(occupied, x - 1, y, gridWidth, gridHeight))
                    AddSide(vertices, normals, uvs, sideTriangles,
                        new Vector2(x0, y1), new Vector2(x0, y0), halfDepth, Vector3.left, sampleUv);

                if (!IsOccupied(occupied, x + 1, y, gridWidth, gridHeight))
                    AddSide(vertices, normals, uvs, sideTriangles,
                        new Vector2(x1, y0), new Vector2(x1, y1), halfDepth, Vector3.right, sampleUv);

                if (!IsOccupied(occupied, x, y - 1, gridWidth, gridHeight))
                    AddSide(vertices, normals, uvs, sideTriangles,
                        new Vector2(x0, y0), new Vector2(x1, y0), halfDepth, Vector3.down, sampleUv);

                if (!IsOccupied(occupied, x, y + 1, gridWidth, gridHeight))
                    AddSide(vertices, normals, uvs, sideTriangles,
                        new Vector2(x1, y1), new Vector2(x0, y1), halfDepth, Vector3.up, sampleUv);
            }
        }

        var mesh = new Mesh
        {
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frontBackTriangles, 0);
        mesh.SetTriangles(sideTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    static AlphaBounds FindAlphaBounds(Color32[] pixels, int width, int height)
    {
        int minX = width;
        int maxX = -1;
        int minY = height;
        int maxY = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a < BoundsAlphaThreshold) continue;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            Debug.LogWarning("[TposeSword] 人物のアルファ輪郭が空です。画像全体を雛型として扱います。");
            return new AlphaBounds { minU = 0f, maxU = 1f, minV = 0f, maxV = 1f };
        }

        return new AlphaBounds
        {
            minU = (float)minX / width,
            maxU = (float)(maxX + 1) / width,
            minV = (float)minY / height,
            maxV = (float)(maxY + 1) / height,
        };
    }

    static void ValidateSafePadding(AlphaBounds bounds, TposeSwordTemplateProfile profile)
    {
        bool horizontalShortage = bounds.minU < profile.requiredHorizontalPadding ||
            1f - bounds.maxU < profile.requiredHorizontalPadding;
        bool verticalShortage = bounds.minV < profile.requiredVerticalPadding ||
            1f - bounds.maxV < profile.requiredVerticalPadding;

        if (horizontalShortage || verticalShortage)
        {
            Debug.LogWarning(
                "[TposeSword] 共通設定の推奨透明余白より人物が外側です。" +
                $" 実データは自動的に収めますが、OpenCV出力は左右{profile.requiredHorizontalPadding:P0}、" +
                $"上下{profile.requiredVerticalPadding:P0}を推奨します。");
        }
    }

    static bool[,] ReadSilhouette(
        Color32[] pixels,
        int pixelWidth,
        int pixelHeight,
        int gridWidth,
        int gridHeight)
    {
        var occupied = new bool[gridWidth, gridHeight];

        for (int y = 0; y < gridHeight; y++)
        {
            int pixelY = Mathf.Clamp(
                Mathf.FloorToInt((y + 0.5f) * pixelHeight / gridHeight), 0, pixelHeight - 1);

            for (int x = 0; x < gridWidth; x++)
            {
                int pixelX = Mathf.Clamp(
                    Mathf.FloorToInt((x + 0.5f) * pixelWidth / gridWidth), 0, pixelWidth - 1);

                occupied[x, y] = pixels[pixelY * pixelWidth + pixelX].a >= AlphaThreshold;
            }
        }

        return occupied;
    }

    static bool IsOccupied(bool[,] occupied, int x, int y, int width, int height)
    {
        return x >= 0 && x < width && y >= 0 && y < height && occupied[x, y];
    }

    static void AddFace(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector3 normal,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        int start = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        uvs.Add(uvA); uvs.Add(uvB); uvs.Add(uvC); uvs.Add(uvD);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    static void AddSide(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<int> triangles,
        Vector2 from,
        Vector2 to,
        float halfDepth,
        Vector3 normal,
        Vector2 sampleUv)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(from.x, from.y, -halfDepth));
        vertices.Add(new Vector3(to.x, to.y, -halfDepth));
        vertices.Add(new Vector3(to.x, to.y, halfDepth));
        vertices.Add(new Vector3(from.x, from.y, halfDepth));
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        uvs.Add(sampleUv); uvs.Add(sampleUv); uvs.Add(sampleUv); uvs.Add(sampleUv);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    static Material CreateSideMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard");
        var material = new Material(shader);

        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 1f);
        if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.5f);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.16f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        return material;
    }
}

/// <summary>実行時生成したMesh/Materialを剣の破棄と同時に解放する。</summary>
sealed class TposeSwordRuntimeResources : MonoBehaviour
{
    Mesh _mesh;
    Material[] _materials;

    public void Configure(Mesh mesh, params Material[] materials)
    {
        _mesh = mesh;
        _materials = materials;
    }

    void OnDestroy()
    {
        if (_mesh != null) DestroyRuntimeObject(_mesh);
        if (_materials == null) return;

        foreach (Material material in _materials)
        {
            if (material != null) DestroyRuntimeObject(material);
        }
    }

    static void DestroyRuntimeObject(Object target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(target);
            return;
        }
#endif
        Destroy(target);
    }
}

/// <summary>生成済みモデルに使用した身長をInspectorで確認するための情報。</summary>
sealed class TposeSwordModelInstance : MonoBehaviour
{
    [SerializeField] float heightCm;

    public void Configure(float heightCm)
    {
        this.heightCm = heightCm;
    }
}
