using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using UnityEditorInternal;
using System.Collections.Generic;

[CustomEditor(typeof(CustomAvatarSettings))]
public class CustomAvatarSettingsEditor : Editor
{
    private ReorderableList reorderableList;
    private List<bool> foldouts = new List<bool>();

    private void OnEnable()
    {
        var script = (CustomAvatarSettings)target;
        while (foldouts.Count < script.parameters.Count) foldouts.Add(true);
        while (foldouts.Count > script.parameters.Count) foldouts.RemoveAt(foldouts.Count - 1);

        reorderableList = new ReorderableList(
            script.parameters,
            typeof(CustomAvatarSettings.CustomParam),
            true, true, true, true
        );

        reorderableList.drawHeaderCallback = (Rect rect) => {
            EditorGUI.LabelField(rect, "Save Entries");
        };

        reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
        {
            var param = script.parameters[index];
            float y = rect.y + 2;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = 2;

            float foldoutOffset = 24f; 
            var foldRect = new Rect(rect.x + foldoutOffset, y, 16, lineHeight);
            var labelRect = new Rect(rect.x + foldoutOffset + 18, y, rect.width - (foldoutOffset + 18), lineHeight);

            foldouts[index] = EditorGUI.Foldout(foldRect, foldouts[index], GUIContent.none, true);

            if (foldouts[index])
                param.label = EditorGUI.TextField(labelRect, param.label);
            else
                EditorGUI.LabelField(labelRect, param.label);

            y += lineHeight + spacing;

            if (foldouts[index])
            {
                param.type = (CustomAvatarSettings.ParamType)EditorGUI.EnumPopup(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Type", param.type);
                y += lineHeight + spacing;

                string[] componentTypes = GameObject.FindObjectsOfType<MonoBehaviour>(true)
                    .Select(c => c.GetType().Name).Distinct().OrderBy(x => x).ToArray();
                int compIdx = Mathf.Max(0, Array.IndexOf(componentTypes, param.componentType));
                compIdx = EditorGUI.Popup(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Component Type", compIdx, componentTypes);
                param.componentType = componentTypes.Length > 0 ? componentTypes[compIdx] : "";
                y += lineHeight + spacing;

                if (param.type == CustomAvatarSettings.ParamType.Slider || param.type == CustomAvatarSettings.ParamType.Toggle || param.type == CustomAvatarSettings.ParamType.Dropdown)
                {
                    var comps = GameObject.FindObjectsOfType<MonoBehaviour>(true)
                        .Where(c => c.GetType().Name == param.componentType);
                    var fields = comps.SelectMany(c => c.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        .Where(f =>
                            (param.type == CustomAvatarSettings.ParamType.Slider && (f.FieldType == typeof(float) || f.FieldType == typeof(int))) ||
                            (param.type == CustomAvatarSettings.ParamType.Toggle && f.FieldType == typeof(bool)) ||
                            (param.type == CustomAvatarSettings.ParamType.Dropdown && (f.FieldType == typeof(int) || f.FieldType == typeof(string)))
                        )
                        .Select(f => f.Name).Distinct().ToArray();
                    int fieldIdx = Mathf.Max(0, Array.IndexOf(fields, param.field));
                    fieldIdx = EditorGUI.Popup(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Field", fieldIdx, fields);
                    param.field = fields.Length > 0 ? fields[fieldIdx] : "";
                    y += lineHeight + spacing;
                }

                if (param.type == CustomAvatarSettings.ParamType.Slider)
                {
                    param.min = EditorGUI.FloatField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Min", param.min);
                    y += lineHeight + spacing;
                    param.max = EditorGUI.FloatField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Max", param.max);
                    y += lineHeight + spacing;
                    param.defaultValue = EditorGUI.FloatField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Default Value", param.defaultValue);
                    y += lineHeight + spacing;
                    param.uiObject = (GameObject)EditorGUI.ObjectField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "UI Object", param.uiObject, typeof(GameObject), true);
                    y += lineHeight + spacing;
                }
                if (param.type == CustomAvatarSettings.ParamType.Toggle)
                {
                    param.defaultToggle = EditorGUI.Toggle(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Default Value", param.defaultToggle);
                    y += lineHeight + spacing;
                    param.uiObject = (GameObject)EditorGUI.ObjectField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "UI Object", param.uiObject, typeof(GameObject), true);
                    y += lineHeight + spacing;
                }
                if (param.type == CustomAvatarSettings.ParamType.Dropdown)
                {
                    int optionCount = Mathf.Max(1, EditorGUI.IntField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Option Count", param.options.Count > 0 ? param.options.Count : 1));
                    y += lineHeight + spacing;
                    while (param.options.Count < optionCount) param.options.Add("Option " + param.options.Count);
                    while (param.options.Count > optionCount) param.options.RemoveAt(param.options.Count - 1);
                    for (int opt = 0; opt < param.options.Count; opt++)
                    {
                        param.options[opt] = EditorGUI.TextField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Option " + opt, param.options[opt]);
                        y += lineHeight + spacing;
                    }
                    param.defaultDropdown = EditorGUI.IntSlider(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "Default Index", param.defaultDropdown, 0, param.options.Count - 1);
                    y += lineHeight + spacing;
                    param.uiObject = (GameObject)EditorGUI.ObjectField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "UI Object", param.uiObject, typeof(GameObject), true);
                    y += lineHeight + spacing;
                }
                if (param.type == CustomAvatarSettings.ParamType.Button)
                {
                    param.uiObject = (GameObject)EditorGUI.ObjectField(new Rect(rect.x + foldoutOffset, y, rect.width - foldoutOffset, lineHeight), "UI Object", param.uiObject, typeof(GameObject), true);
                    y += lineHeight + spacing;
                }
            }
        };

        reorderableList.elementHeightCallback = (index) =>
        {
            var param = script.parameters[index];
            float h = EditorGUIUtility.singleLineHeight + 2; 
            if (foldouts[index])
            {
                h += EditorGUIUtility.singleLineHeight + 2; 
                h += EditorGUIUtility.singleLineHeight + 2;
                if (param.type == CustomAvatarSettings.ParamType.Slider || param.type == CustomAvatarSettings.ParamType.Toggle || param.type == CustomAvatarSettings.ParamType.Dropdown)
                    h += EditorGUIUtility.singleLineHeight + 2; 
                if (param.type == CustomAvatarSettings.ParamType.Slider)
                    h += 4 * (EditorGUIUtility.singleLineHeight + 2);
                if (param.type == CustomAvatarSettings.ParamType.Toggle)
                    h += 2 * (EditorGUIUtility.singleLineHeight + 2);
                if (param.type == CustomAvatarSettings.ParamType.Dropdown)
                    h += (Mathf.Max(1, param.options.Count) + 3) * (EditorGUIUtility.singleLineHeight + 2);
                if (param.type == CustomAvatarSettings.ParamType.Button)
                    h += EditorGUIUtility.singleLineHeight + 2;
            }
            return h;
        };
    }

    public override void OnInspectorGUI()
    {
        var script = (CustomAvatarSettings)target;
        while (foldouts.Count < script.parameters.Count) foldouts.Add(true);
        while (foldouts.Count > script.parameters.Count) foldouts.RemoveAt(foldouts.Count - 1);

        serializedObject.Update();
        reorderableList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }
}
