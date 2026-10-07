using OrbitRush;
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a standalone Windows version of the game that runs without Unity installed.
///
///   In the Editor:  menu  Orbit Rush ▸ Build Windows (.exe)
///   Command line:   Unity.exe -batchmode -quit -projectPath "&lt;project&gt;" -executeMethod BuildGame.BuildWindows
///                   [-buildOutput "&lt;folder&gt;"]    (default: &lt;project&gt;/Builds/OrbitRush)
///
/// Scenes come from File ▸ Build Settings. The folder it produces (OrbitRush.exe + OrbitRush_Data + ...)
/// can be zipped and sent as-is.
/// </summary>
public static class BuildGame
{
    private const string ExeName = "OrbitRush.exe";

    [MenuItem("Orbit Rush/Build Windows (.exe)")]
    public static void BuildWindowsMenu()
    {
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "OrbitRush");
        int code = Build(folder);
        if (code == 0) EditorUtility.RevealInFinder(Path.Combine(folder, ExeName));
    }

    /// <summary>Entry point for -executeMethod. Exits the editor with 0 on success, 1 on failure.</summary>
    public static void BuildWindows()
    {
        string folder = GetArg("-buildOutput");
        if (string.IsNullOrEmpty(folder))
            folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds", "OrbitRush");

        int code = Build(folder);
        EditorApplication.Exit(code);
    }

    private static int Build(string outputFolder)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[BuildGame] No enabled scenes in File > Build Settings.");
            return 1;
        }

        PlayerSettings.companyName = "Orbit Rush";
        PlayerSettings.productName = "Orbit Rush";
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.resizableWindow = true;

        EnsureRuntimeShadersIncluded();

        Directory.CreateDirectory(outputFolder);
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(outputFolder, ExeName),
            target = BuildTarget.StandaloneWindows64,
            // Self-tests / screenshot tools only exist in development builds (pass -dev); release builds exclude them.
            options = Array.IndexOf(Environment.GetCommandLineArgs(), "-dev") >= 0 ? BuildOptions.Development : BuildOptions.None,
        };

        Debug.Log($"[BuildGame] Building {scenes.Length} scene(s) → {options.locationPathName}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildGame] SUCCESS — {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F0}s → {outputFolder}");
            return 0;
        }

        Debug.LogError($"[BuildGame] FAILED: {summary.result} ({summary.totalErrors} errors)");
        return 1;
    }

    /// <summary>
    /// The game creates many materials in code with Shader.Find(...). Unity only keeps a shader in a
    /// build if something references it, so Find() would return null at runtime (and the object would
    /// render magenta or throw). Force-include the ones the code uses.
    /// </summary>
    private static void EnsureRuntimeShadersIncluded()
    {
        string[] names =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Simple Lit",
            "Sprites/Default",
            "Skybox/Panoramic",
        };

        var graphicsSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("ProjectSettings/GraphicsSettings.asset");
        if (graphicsSettings == null) { Debug.LogWarning("[BuildGame] GraphicsSettings asset not found."); return; }

        var so = new SerializedObject(graphicsSettings);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (var name in names)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning($"[BuildGame] Shader not found: {name}"); continue; }

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
            if (present) continue;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    private static string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
