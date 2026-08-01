using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batch build helper: verifies the player compiles and links end-to-end.
// Usage: Unity -batchmode -quit -projectPath <path> -executeMethod BuildScript.BuildMacOS
public static class BuildScript
{
    public static void BuildMacOS()
    {
        string outPath = "Builds/VerifyBuild/VerifyBuild.app";
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/MATE ENGINE - Scenes/Mate Engine Main.unity" },
            locationPathName = outPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError("BUILD_FAILED: " + report.summary.result);
            foreach (var m in report.steps)
                foreach (var msg in m.messages)
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError("  step msg: " + msg.content);
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log("BUILD_SUCCEEDED: " + outPath);
            EditorApplication.Exit(0);
        }
    }
}
