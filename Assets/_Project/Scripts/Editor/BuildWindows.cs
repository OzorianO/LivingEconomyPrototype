using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildWindows
{
    [MenuItem("Living Economy/Build Windows")]
    public static void Run()
    {
        const string mainScene = "Assets/_Project/Scenes/MainSimulation.unity";
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length != 1 || scenes[0] != mainScene)
            throw new InvalidOperationException("Enable only MainSimulation in Build Settings before building.");
        string output = Path.GetFullPath("Builds/Windows/MedievalColony.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        string result = $"Windows build: {report.summary.result}; errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}; bytes={report.summary.totalSize}";
        foreach (var step in report.steps)
            foreach (var message in step.messages)
                if (message.type == UnityEngine.LogType.Warning || message.type == UnityEngine.LogType.Error)
                    result += $"\n{message.type}: {message.content}";
        File.WriteAllText("Builds/Windows/build-result.txt", result);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception(result);
        UnityEngine.Debug.Log(result);
    }
}
