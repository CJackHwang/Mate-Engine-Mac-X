using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Utils;

public class SystemTray : MonoBehaviour
{
    [Serializable]
    public class TrayAction
    {
        public string label;
        public TrayActionType type;
        public GameObject handlerObject;
        public string toggleField;
        public string methodName;
    }
    public enum TrayActionType { Toggle, Button }

    [SerializeField] private Texture2D icon;
    [SerializeField] private string iconName;
    [SerializeField] public List<TrayAction> actions = new();

    void Awake()
    {
        TrayIcon.OnBuildMenu = BuildMenu;
        TrayIcon.Init("App", iconName, icon, BuildMenu());
    }

    private List<(string, Action)> BuildMenu()
    {
        var context = new List<(string, Action)>();
        foreach (var action in actions)
        {
            if (action.type == TrayActionType.Toggle)
            {
                bool state = GetToggleState(action);
                string label = (state ? "✔ " : "✖ ") + action.label;
                context.Add((label, () =>
                {
                    ToggleAction(action);
                }
                ));
            }
            else if (action.type == TrayActionType.Button)
            {
                context.Add((action.label, () => ButtonAction(action)));
            }
        }
        context.Add(("Quit MateEngine", QuitApp));
        return context;
    }

    private bool GetToggleState(TrayAction action)
    {
        if (action.handlerObject == null || string.IsNullOrEmpty(action.toggleField)) return false;
        var mono = action.handlerObject.GetComponent<MonoBehaviour>();
        if (mono == null) return false;
        var type = mono.GetType();
        var field = type.GetField(action.toggleField);
        if (field != null && field.FieldType == typeof(Toggle))
        {
            var toggle = field.GetValue(mono) as Toggle;
            if (toggle != null)
                return toggle.isOn;
        }
        return false;
    }

    private void ToggleAction(TrayAction action)
    {
        if (action.handlerObject == null || string.IsNullOrEmpty(action.toggleField)) return;
        var mono = action.handlerObject.GetComponent<MonoBehaviour>();
        if (mono == null) return;
        var type = mono.GetType();
        var field = type.GetField(action.toggleField);
        if (field != null && field.FieldType == typeof(Toggle))
        {
            var toggle = field.GetValue(mono) as Toggle;
            if (toggle != null)
            {
                toggle.isOn = !toggle.isOn;
            }
        }
    }

    private void ButtonAction(TrayAction action)
    {
        if (action.handlerObject == null || string.IsNullOrEmpty(action.methodName)) return;
        var mono = action.handlerObject.GetComponent<MonoBehaviour>();
        if (mono == null) return;
        var type = mono.GetType();
        var method = type.GetMethod(action.methodName);
        if (method != null)
            method.Invoke(mono, null);
    }

    private void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
