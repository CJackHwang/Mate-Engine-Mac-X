using UnityEngine;
using UnityEditor;

public class FindBigTextures
{
    [MenuItem("Tools/Find Big Textures")]
    static void Find()
    {
        int minWidth = 1024;   // Mindestbreite
        int minHeight = 1024;  // Mindesthöhe

        var guids = AssetDatabase.FindAssets("t:Texture2D");
        int found = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null && (tex.width >= minWidth || tex.height >= minHeight))
            {
                found++;
                Debug.LogFormat(tex, "{0} - {1}x{2}", path, tex.width, tex.height);
            }
        }
        if (found == 0)
        {
            Debug.Log("Keine zu großen Texturen gefunden!");
        }
    }
}
