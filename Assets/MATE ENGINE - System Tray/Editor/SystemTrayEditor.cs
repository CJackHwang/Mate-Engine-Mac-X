using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UI;

[CustomEditor(typeof(SystemTray))]
public class SystemTrayEditor : Editor
{
    private ReorderableList list;

    void OnEnable()
    {
        list = new ReorderableList(serializedObject,
            serializedObject.FindProperty("actions"),
            true, true, true, true);

        list.drawHeaderCallback = rect =>
        {
            EditorGUI.LabelField(rect, "Tray Actions");
        };

        list.elementHeightCallback = index =>
        {
            return EditorGUIUtility.singleLineHeight * 6 + 10;
        };

        list.drawElementCallback = (rect, index, active, focused) =>
        {
            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            var label = element.FindPropertyRelative("label");
            var type = element.FindPropertyRelative("type");
            var handlerObject = element.FindPropertyRelative("handlerObject");
            var toggleField = element.FindPropertyRelative("toggleField");
            var methodName = element.FindPropertyRelative("methodName");

            float y = rect.y + 2;
            float height = EditorGUIUtility.singleLineHeight;
            float spacing = 2;

            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, height), label, new GUIContent("Label"));
            y += height + spacing;
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, height), type, new GUIContent("Type"));
            y += height + spacing;
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, height), handlerObject, new GUIContent("Handler Object"));
            y += height + spacing;

            if (handlerObject.objectReferenceValue != null)
            {
                MonoBehaviour handler = ((GameObject)handlerObject.objectReferenceValue).GetComponent<MonoBehaviour>();
                if (handler != null)
                {
                    var handlerType = handler.GetType();
                    if ((SystemTray.TrayActionType)type.enumValueIndex == SystemTray.TrayActionType.Toggle)
                    {
                        var fields = new List<string>();
                        foreach (var f in handlerType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (f.FieldType == typeof(Toggle))
                                fields.Add(f.Name);
                        }
                        int selIdx = Mathf.Max(0, fields.IndexOf(toggleField.stringValue));
                        selIdx = EditorGUI.Popup(new Rect(rect.x, y, rect.width, height),
                            "Toggle Field", selIdx, fields.ToArray());
                        toggleField.stringValue = fields.Count > 0 ? fields[selIdx] : "";
                    }
                    else if ((SystemTray.TrayActionType)type.enumValueIndex == SystemTray.TrayActionType.Button)
                    {
                        var methods = new List<string>();
                        foreach (var m in handlerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (m.GetParameters().Length == 0 && m.ReturnType == typeof(void))
                                methods.Add(m.Name);
                        }
                        int selIdx = Mathf.Max(0, methods.IndexOf(methodName.stringValue));
                        selIdx = EditorGUI.Popup(new Rect(rect.x, y, rect.width, height),
                            "Method", selIdx, methods.ToArray());
                        methodName.stringValue = methods.Count > 0 ? methods[selIdx] : "";
                    }
                }
            }
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var iconProp = serializedObject.FindProperty("icon");
        var iconNameProp = serializedObject.FindProperty("iconName");
        EditorGUILayout.PropertyField(iconProp);
        EditorGUILayout.PropertyField(iconNameProp);

        list.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }
}
