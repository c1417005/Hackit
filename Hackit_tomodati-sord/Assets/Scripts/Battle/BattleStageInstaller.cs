using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 対戦する場を丸ごと組み立てて返す。地面・背景・カメラ・ライト・後処理・ファイター2体。
///
/// **戦闘担当のファイル。** 場の見た目と物理まわりはここで完結させる。
/// 進行やUIのことは知らないので、他の担当と衝突しない。
/// </summary>
public static class BattleStageInstaller
{
    /// <summary>組み上がった対戦の場。App がこれを他モジュールに配る。</summary>
    public sealed class Rig
    {
        public Fighter Player1;
        public Fighter Player2;
        public Camera Camera;
    }

    [System.Serializable]
    public struct Config
    {
        [Tooltip("中央から手の支点までの距離。広げすぎると剣が相手に届かない")]
        public float spawnDistance;

        [Tooltip("手の支点の高さ")]
        public float anchorHeight;

        public Vector3 cameraPosition;

        public static Config Default => new Config
        {
            spawnDistance = 1.15f,
            anchorHeight = 3.4f,
            cameraPosition = new Vector3(0f, 2.7f, -6.2f),
        };
    }

    public static Rig Install(Config config)
    {
        // 旧振り子版のシーンには 0.5 が Serialize 済みなので、斬撃の間合いを最低値として保証する
        float spawnDistance = Mathf.Max(config.spawnDistance, 1.15f);

        BuildGround();
        BuildBackdrop();

        Camera camera = SetupCamera(config.cameraPosition);
        SetupLight();
        SetupVisualQuality(camera);

        var rig = new Rig
        {
            Camera = camera,
            Player1 = BuildFighter("Player1", 0, +1, -spawnDistance, config.anchorHeight),
            Player2 = BuildFighter("Player2", 1, -1, +spawnDistance, config.anchorHeight),
        };

        BattleCamera battleCamera = camera.gameObject.GetComponent<BattleCamera>();
        if (battleCamera == null) battleCamera = camera.gameObject.AddComponent<BattleCamera>();
        battleCamera.player1 = rig.Player1;
        battleCamera.player2 = rig.Player2;

        return rig;
    }

    static void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(24f, 1f, 4f);
        ApplyColor(ground.GetComponent<Renderer>(), new Color(0.28f, 0.30f, 0.34f));
    }

    static void BuildBackdrop()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ArenaBackdrop";
        Object.DestroyImmediate(wall.GetComponent<Collider>());
        wall.transform.position = new Vector3(0f, 2.7f, 2.2f);
        wall.transform.localScale = new Vector3(13f, 7.5f, 0.2f);
        ApplyColor(wall.GetComponent<Renderer>(), new Color(0.025f, 0.035f, 0.075f));

        BuildGlowPillar("P1Glow", -4.7f, new Color(0.08f, 0.42f, 1f));
        BuildGlowPillar("P2Glow", 4.7f, new Color(1f, 0.16f, 0.08f));
        BuildGlowPillar("CenterGlow", 0f, new Color(1f, 0.68f, 0.12f), 0.035f);
    }

    static void BuildGlowPillar(string name, float x, Color color, float width = 0.10f)
    {
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillar.name = name;
        Object.DestroyImmediate(pillar.GetComponent<Collider>());
        pillar.transform.position = new Vector3(x, 2.7f, 2.05f);
        pillar.transform.localScale = new Vector3(width, 5.6f, 0.08f);

        Renderer renderer = pillar.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var material = new Material(shader);
        Color bright = color * 2.2f;
        bright.a = 1f;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", bright);
        if (material.HasProperty("_Color")) material.SetColor("_Color", bright);
        renderer.sharedMaterial = material;
    }

    static Camera SetupCamera(Vector3 position)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }

        // 3D空間だがカメラは横固定（実質2.5D）
        cam.transform.position = position;
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 45f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.012f, 0.018f, 0.045f);
        cam.allowHDR = true;
        return cam;
    }

    static void SetupLight()
    {
        Light light = Object.FindAnyObjectByType<Light>();
        GameObject go;
        if (light == null)
        {
            go = new GameObject("Directional Light");
            light = go.AddComponent<Light>();
        }
        else
        {
            go = light.gameObject;
        }

        light.type = LightType.Directional;
        light.intensity = 1.35f;
        light.color = new Color(1f, 0.92f, 0.84f);
        light.shadows = LightShadows.Soft;
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static void SetupVisualQuality(Camera cam)
    {
        UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        Volume volume = Object.FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            var volumeObject = new GameObject("BattlePostProcess");
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
        }

        VolumeProfile profile = volume.profile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;
        }

        if (!profile.TryGet(out Bloom bloom)) bloom = profile.Add<Bloom>(true);
        bloom.active = true;
        bloom.intensity.Override(0.42f);
        bloom.threshold.Override(0.95f);
        bloom.scatter.Override(0.62f);

        if (!profile.TryGet(out ColorAdjustments color)) color = profile.Add<ColorAdjustments>(true);
        color.active = true;
        color.postExposure.Override(0.12f);
        color.contrast.Override(14f);
        color.saturation.Override(8f);

        if (!profile.TryGet(out Vignette vignette)) vignette = profile.Add<Vignette>(true);
        vignette.active = true;
        vignette.intensity.Override(0.20f);
        vignette.smoothness.Override(0.55f);
    }

    /// <summary>
    /// 手の支点を1つ作り、そこに Fighter を仕込む。
    /// GameObject の原点がそのまま手首の回転中心になる。
    /// </summary>
    static Fighter BuildFighter(string name, int playerIndex, int facing, float x, float anchorHeight)
    {
        var go = new GameObject(name);
        go.transform.position = new Vector3(x, anchorHeight, 0f);
        go.AddComponent<Rigidbody>();

        var fighter = go.AddComponent<Fighter>();
        fighter.playerIndex = playerIndex;
        fighter.SetFacing(facing);

        return fighter;
    }

    static void ApplyColor(Renderer renderer, Color color)
    {
        var mat = new Material(renderer.sharedMaterial);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        renderer.sharedMaterial = mat;
    }
}
