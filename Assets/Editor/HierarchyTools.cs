// File: HierarchyTools.cs
// Place this script inside a folder named "Editor"

using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public static class HierarchyTools
{
    static HierarchyTools()
    {
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
    }

    private static void OnHierarchyGUI(EntityId entityId, Rect selectionRect)
    {
        GameObject obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
        if (obj == null) return;

        string name = obj.name;
        if (name.StartsWith("="))
        {
            // Extend selectionRect to full width
            Rect fullRect = new Rect(0, selectionRect.y, Screen.width, selectionRect.height);

            // Draw dark grey background
            EditorGUI.DrawRect(fullRect, new Color(0.2f, 0.2f, 0.2f));

            // Prepare centered label
            string cleanName = name.Substring(1).Trim();
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            // Draw centered text inside full width
            EditorGUI.LabelField(fullRect, cleanName, style);
        }
    }

}
