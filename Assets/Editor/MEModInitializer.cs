using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public static class MEModInitializer
{
    static MEModInitializer()
    {
        // Chibi Mode paths
        CreateFolderWithKeep(Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Chibi Mode/Sounds/Enter Sounds"));
        CreateFolderWithKeep(Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Chibi Mode/Sounds/Exit Sounds"));

        // Drag Mode paths
        CreateFolderWithKeep(Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Drag Mode/Sounds/Drag Sounds"));
        CreateFolderWithKeep(Path.Combine(Application.streamingAssetsPath, "Mods/ModLoader/Drag Mode/Sounds/Place Sounds"));
    }

    private static void CreateFolderWithKeep(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            Debug.Log("[MEModInitializer] Created folder: " + path);
        }

        string keepFile = Path.Combine(path, ".keep");
        if (!File.Exists(keepFile))
        {
            File.WriteAllText(keepFile, "This file ensures the folder is included in build.");
            Debug.Log("[MEModInitializer] Created .keep file in: " + path);
        }
    }
}
