using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class TomodachiSwordReleaseBuilder
{
    private const string MenuPath = "友達ソード/Windows 64bit 製品版をビルド";
    private const string ExecutableName = "TomodachiSword.exe";
    private const string RequestFileName = "build.request";

    [InitializeOnLoadMethod]
    private static void BuildAutomaticallyWhenRequested()
    {
        string requestPath = GetRequestPath();
        if (!File.Exists(requestPath))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(requestPath))
            {
                return;
            }

            // Domain Reloadが発生しても二重ビルドしないよう、開始前に依頼を消します。
            File.Delete(requestPath);

            try
            {
                string executablePath = BuildWindows64();
                EditorUtility.RevealInFinder(executablePath);
                EditorUtility.DisplayDialog(
                    "友達ソード",
                    "Windows 64bit製品版のビルドが完了しました。\n\n" + executablePath,
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "友達ソード",
                    "製品版ビルドに失敗しました。Consoleを確認してください。",
                    "OK");
            }
        };
    }

    [MenuItem(MenuPath)]
    public static void BuildWindows64FromMenu()
    {
        string executablePath = BuildWindows64();
        EditorUtility.RevealInFinder(executablePath);
        EditorUtility.DisplayDialog(
            "友達ソード",
            "Windows 64bit製品版のビルドが完了しました。\n\n" + executablePath,
            "OK");
    }

    // Unityの -executeMethod から呼び出すための入口です。
    public static void BuildWindows64CommandLine()
    {
        try
        {
            BuildWindows64();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static string BuildWindows64()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("Build Settingsに有効なシーンがありません。");
        }

        string outputDirectory = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Builds",
            "Windows",
            "TomodachiSword");
        Directory.CreateDirectory(outputDirectory);

        string executablePath = Path.Combine(outputDirectory, ExecutableName);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = executablePath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.StrictMode | BuildOptions.CompressWithLz4HC
        };

        // Intel Iris Xe環境で安定した組み合わせを、今後の通常ビルドにも残す。
        // 描画品質は変えず、グラフィックスAPI・入力API・表示方式だけを固定する。
        PlayerSettings.SetGraphicsAPIs(
            BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D11 });
        PlayerSettings.windowsGamepadBackendHint =
            WindowsGamepadBackendHint.WindowsGamepadBackendHintXInput;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;

        Debug.Log($"[ReleaseBuild] Windows 64bit製品版を作成します: {executablePath}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"ビルドに失敗しました。result={summary.result}, errors={summary.totalErrors}, warnings={summary.totalWarnings}");
        }

        Debug.Log(
            $"[ReleaseBuild] 完了: {executablePath} " +
            $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime})");
        return executablePath;
    }

    private static string GetRequestPath()
    {
        return Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Builds",
            "Windows",
            "TomodachiSword",
            RequestFileName);
    }
}
