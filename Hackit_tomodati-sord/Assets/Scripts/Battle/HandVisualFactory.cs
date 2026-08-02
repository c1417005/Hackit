using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 戦闘ロジックから独立した、手の見た目だけを生成する。
/// Resources/Battle/Hand/HandVisual があればPrefabを使い、無ければ安全な簡易モデルを生成する。
/// </summary>
public static class HandVisualFactory
{
    const string PrefabResourcePath = "Battle/Hand/HandVisual";

    public static bool TryCreate(Transform handPivot)
    {
        if (handPivot == null) return false;

        GameObject visualRoot = null;
        try
        {
            visualRoot = new GameObject("HandVisualRoot");
            visualRoot.transform.SetParent(handPivot, false);

            var facing = visualRoot.AddComponent<HandVisualFacing>();
            facing.Configure(handPivot.GetComponentInParent<Fighter>());

            GameObject prefab = Resources.Load<GameObject>(PrefabResourcePath);
            if (prefab != null)
            {
                GameObject model = UnityEngine.Object.Instantiate(prefab, visualRoot.transform, false);
                model.name = "HandVisual";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                RemoveGameplayComponents(model);
                return true;
            }

            BuildProceduralHand(visualRoot.transform);
            return true;
        }
        catch (Exception exception)
        {
            if (visualRoot != null) UnityEngine.Object.DestroyImmediate(visualRoot);
            Debug.LogWarning($"[HandVisual] 見た目の生成に失敗したため従来表示へ戻します: {exception.Message}");
            return false;
        }
    }

    static void BuildProceduralHand(Transform root)
    {
        Material skin = CreateMaterial("HandSkin", new Color(0.93f, 0.72f, 0.57f), 0.38f);
        Material skinShade = CreateMaterial("HandSkinShade", new Color(0.78f, 0.53f, 0.40f), 0.32f);
        Material cuff = CreateMaterial("HandCuff", new Color(0.10f, 0.14f, 0.24f), 0.28f);
        Material cuffTrim = CreateMaterial("HandCuffTrim", new Color(0.27f, 0.38f, 0.58f), 0.34f);
        Material grip = CreateMaterial("HandGrip", new Color(0.16f, 0.085f, 0.045f), 0.22f);
        Material gripRing = CreateMaterial("HandGripRing", new Color(0.38f, 0.31f, 0.24f), 0.50f);

        var resources = root.gameObject.AddComponent<HandVisualRuntimeResources>();
        resources.Configure(new[] { skin, skinShade, cuff, cuffTrim, grip, gripRing });

        // 袖と手首。HandPivotの原点とSwordPivotの接続位置は変更しない。
        CreatePart("Sleeve", PrimitiveType.Cylinder, root,
            new Vector3(0f, -0.21f, 0.025f), Quaternion.identity,
            new Vector3(0.145f, 0.105f, 0.135f), cuff);
        CreatePart("CuffRing", PrimitiveType.Cylinder, root,
            new Vector3(0f, -0.10f, 0.025f), Quaternion.identity,
            new Vector3(0.17f, 0.040f, 0.15f), cuffTrim);

        // 角張ったCubeの代わりに、丸みのある掌を重ねて拳の輪郭を作る。
        CreatePart("Palm", PrimitiveType.Sphere, root,
            new Vector3(0f, -0.005f, 0.035f), Quaternion.identity,
            new Vector3(0.205f, 0.185f, 0.150f), skin);
        CreatePart("PalmPad", PrimitiveType.Sphere, root,
            new Vector3(-0.035f, 0.055f, -0.020f), Quaternion.identity,
            new Vector3(0.145f, 0.135f, 0.115f), skinShade);

        // 柄は従来と同じ中心線上に置く。SwordPivotはFighter側の(0, 0.22, 0)のまま。
        CreatePart("Grip", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.13f, 0f), Quaternion.identity,
            new Vector3(0.055f, 0.14f, 0.055f), grip);
        CreatePart("GripRingLower", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.005f, 0f), Quaternion.identity,
            new Vector3(0.070f, 0.016f, 0.070f), gripRing);
        CreatePart("GripRingUpper", PrimitiveType.Cylinder, root,
            new Vector3(0f, 0.245f, 0f), Quaternion.identity,
            new Vector3(0.070f, 0.016f, 0.070f), gripRing);

        // 4本の指を柄の手前へ重ねる。少しずつ寸法を変え、機械的な同形状感を減らす。
        float[] fingerY = { 0.155f, 0.105f, 0.055f, 0.008f };
        float[] fingerLength = { 0.078f, 0.082f, 0.077f, 0.067f };
        for (int i = 0; i < fingerY.Length; i++)
        {
            CreatePart("Finger" + i, PrimitiveType.Capsule, root,
                new Vector3(0.005f, fingerY[i], -0.076f),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(0.050f, fingerLength[i], 0.052f),
                i == 0 || i == 3 ? skinShade : skin);

            CreatePart("Knuckle" + i, PrimitiveType.Sphere, root,
                new Vector3(-0.078f, fingerY[i] + 0.002f, -0.020f), Quaternion.identity,
                new Vector3(0.062f, 0.048f, 0.060f), skinShade);
        }

        // 親指は手前を斜めに横切らせ、柄を押さえている形にする。
        CreatePart("Thumb", PrimitiveType.Capsule, root,
            new Vector3(-0.050f, 0.105f, -0.112f),
            Quaternion.Euler(0f, 0f, -38f),
            new Vector3(0.060f, 0.092f, 0.060f), skin);
        CreatePart("ThumbTip", PrimitiveType.Sphere, root,
            new Vector3(0.015f, 0.155f, -0.115f), Quaternion.identity,
            new Vector3(0.065f, 0.054f, 0.060f), skinShade);
    }

    static GameObject CreatePart(
        string name,
        PrimitiveType primitive,
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localRotation = localRotation;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return part;
    }

    static Material CreateMaterial(string name, Color color, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    static void RemoveGameplayComponents(GameObject model)
    {
        foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
        foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
            UnityEngine.Object.DestroyImmediate(body);
        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }
}

/// <summary>負のScaleを使わず、プレイヤーの向きに合わせて見た目だけを反転する。</summary>
sealed class HandVisualFacing : MonoBehaviour
{
    Fighter _fighter;
    int _appliedFacing;

    public void Configure(Fighter fighter)
    {
        _fighter = fighter;
        _appliedFacing = 0;
        ApplyIfNeeded();
    }

    void LateUpdate()
    {
        ApplyIfNeeded();
    }

    void ApplyIfNeeded()
    {
        int facing = _fighter == null || _fighter.facing >= 0 ? 1 : -1;
        if (facing == _appliedFacing) return;
        _appliedFacing = facing;
        transform.localRotation = Quaternion.Euler(0f, facing < 0 ? 180f : 0f, 0f);
    }
}

/// <summary>実行時に作ったMaterialだけを、手の破棄時に片付ける。</summary>
sealed class HandVisualRuntimeResources : MonoBehaviour
{
    Material[] _materials;

    public void Configure(Material[] materials)
    {
        _materials = materials;
    }

    void OnDestroy()
    {
        if (_materials == null) return;
        foreach (Material material in _materials)
        {
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
