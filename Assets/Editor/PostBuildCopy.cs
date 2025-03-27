using UnityEditor;
using UnityEditor.Callbacks;
using System.IO;

public class PostBuildCopy
{
    [PostProcessBuild]
    public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
    {
        string buildDir = Path.GetDirectoryName(pathToBuiltProject);

        // Source paths inside the Unity project
        string projectRoot = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Plugins");

        string steamDllSource = Path.Combine(projectRoot, "steam_api64.dll");
        string steamAppIdSource = Path.Combine(projectRoot, "steam_appid.txt");

        // Destination paths (final build folder)
        string steamDllDest = Path.Combine(buildDir, "steam_api64.dll");
        string steamAppIdDest = Path.Combine(buildDir, "steam_appid.txt");

        // Copy steam_api64.dll
        if (File.Exists(steamDllSource))
        {
            File.Copy(steamDllSource, steamDllDest, true);
            UnityEngine.Debug.Log("Copied steam_api64.dll to build folder.");
        }
        else
        {
            UnityEngine.Debug.LogError("steam_api64.dll not found in Plugins folder.");
        }

        // Copy steam_appid.txt
        if (File.Exists(steamAppIdSource))
        {
            File.Copy(steamAppIdSource, steamAppIdDest, true);
            UnityEngine.Debug.Log("Copied steam_appid.txt to build folder.");
        }
        else
        {
            UnityEngine.Debug.LogError("steam_appid.txt not found in Plugins folder.");
        }
    }
}
