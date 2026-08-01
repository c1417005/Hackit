using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// シーンの組み立て役。空の GameObject に貼って Play するだけで一通り動く。
///
/// 地面・カメラ・ファイター2体を作り、SwordRepository / DuelManager / 選択画面 / HUD を
/// 生成して繋ぐ。プレハブもシーン配置も要らないので、複数人で触っても衝突しない。
///
/// 本番のシーンを作り込む段階になったら、ここでやっていることを
/// そのままヒエラルキー上に置き換えれば良い。
/// </summary>
public class BattleTestBootstrap : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("中央から手の支点までの距離。広げすぎると剣が相手に届かない")]
    public float spawnDistance = 1.15f;

    [Tooltip("手の支点の高さ")]
    public float anchorHeight = 3.4f;

    // 手が y=3.4。斬撃中の刃先が上下へ振れても画面に入る位置。
    public Vector3 cameraPosition = new Vector3(0f, 2.7f, -6.2f);

    [Header("テスト用の結果表示（リザルト画面ができたら不要）")]
    public bool showDebugHud = true;

    [Tooltip("選択画面を飛ばして、いきなり先頭2本で対戦を始める（戦闘だけ試したい時用）")]
    public bool skipSelect;

    Fighter _p1;
    Fighter _p2;
    DuelManager _duel;

    void Start()
    {
        // 旧振り子版のシーンには0.5がSerialize済みなので、新しい斬撃の間合いを最低値として保証する。
        spawnDistance = Mathf.Max(spawnDistance, 1.15f);

        BuildGround();
        BuildBackdrop();
        Camera cam = SetupCamera();
        SetupLight();
        SetupVisualQuality(cam);

        _p1 = BuildFighter("Player1", 0, +1, -spawnDistance);
        _p2 = BuildFighter("Player2", 1, -1, +spawnDistance);

        var flowGo = new GameObject("DuelFlow");
        var repository = flowGo.AddComponent<SwordRepository>();

        _duel = flowGo.AddComponent<DuelManager>();
        _duel.repository = repository;
        _duel.player1 = _p1;
        _duel.player2 = _p2;
        _duel.spawnDistance = spawnDistance;
        _duel.anchorHeight = anchorHeight;

        var battleCamera = cam.gameObject.GetComponent<BattleCamera>();
        if (battleCamera == null) battleCamera = cam.gameObject.AddComponent<BattleCamera>();
        battleCamera.player1 = _p1;
        battleCamera.player2 = _p2;

        SwordSelectUI.Create(_duel);
        BattleHud.Create(_p1, _p2).Bind(_duel);

        if (skipSelect)
        {
            _duel.OnPhaseChanged += StartImmediatelyOnce;
        }
    }

    /// <summary>選択画面に入った瞬間に対戦へ飛ばす。戦闘だけ確認したい時用。</summary>
    void StartImmediatelyOnce(DuelManager.Phase phase)
    {
        if (phase != DuelManager.Phase.Select) return;
        _duel.OnPhaseChanged -= StartImmediatelyOnce;
        _duel.StartBattle();
    }

    void Update()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;

        // 決着後、Rか決定ボタンで選択画面に戻る
        if (_duel != null && _duel.CanLeaveResult && kb.rKey.wasPressedThisFrame)
        {
            _duel.EnterSelect();
        }
    }

    // ---------- シーン組み立て ----------

    void BuildGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(24f, 1f, 4f);
        ApplyColor(ground.GetComponent<Renderer>(), new Color(0.28f, 0.30f, 0.34f));
    }

    void BuildBackdrop()
    {
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ArenaBackdrop";
        DestroyImmediate(wall.GetComponent<Collider>());
        wall.transform.position = new Vector3(0f, 2.7f, 2.2f);
        wall.transform.localScale = new Vector3(13f, 7.5f, 0.2f);
        ApplyColor(wall.GetComponent<Renderer>(), new Color(0.025f, 0.035f, 0.075f));

        BuildGlowPillar("P1Glow", -4.7f, new Color(0.08f, 0.42f, 1f));
        BuildGlowPillar("P2Glow", 4.7f, new Color(1f, 0.16f, 0.08f));
        BuildGlowPillar("CenterGlow", 0f, new Color(1f, 0.68f, 0.12f), 0.035f);
    }

    void BuildGlowPillar(string name, float x, Color color, float width = 0.10f)
    {
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pillar.name = name;
        DestroyImmediate(pillar.GetComponent<Collider>());
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

    Camera SetupCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }

        // 3D空間だがカメラは横固定（実質2.5D）
        cam.transform.position = cameraPosition;
        cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 45f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.012f, 0.018f, 0.045f);
        cam.allowHDR = true;
        return cam;
    }

    void SetupLight()
    {
        Light light = FindAnyObjectByType<Light>();
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

    void SetupVisualQuality(Camera cam)
    {
        UniversalAdditionalCameraData cameraData = cam.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        Volume volume = FindAnyObjectByType<Volume>();
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
    Fighter BuildFighter(string name, int playerIndex, int facing, float x)
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

    // ---------- 仮のリザルト表示（未実装4でちゃんとしたものに置き換える） ----------

    void OnGUI()
    {
        if (!showDebugHud || _duel == null) return;

        if (_duel.Current == DuelManager.Phase.Battle)
        {
            var help = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            GUI.Label(new Rect(20, 120, 900, 60),
                "□ = 縦斬り   △ = 横斬り   L1 = ガード\n" +
                "キーボード: 1P = F / R / G   2P = . / , / /",
                help);
            return;
        }

        if (_duel.Current != DuelManager.Phase.Result || _duel.Winner == null) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
        };
        string label = (_duel.Winner.playerIndex == 0 ? "1P" : "2P") + " WIN";
        GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 80), label, style);

        if (_duel.CanLeaveResult)
        {
            var sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.32f + 80, Screen.width, 40), "R キーで剣えらびに戻る", sub);
        }
    }
}
