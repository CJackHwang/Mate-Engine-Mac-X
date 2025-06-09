using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

public class SystemTray : MonoBehaviour
{
    [SerializeField]
    private Texture2D icon;
    [SerializeField]
    private string iconName;

    private void QuitApp()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Awake()
    {
        var context = new List<(string, Action)>()
        {
            ("Quit", QuitApp)
        };
        TrayIcon.Init("App", iconName, icon, context);
    }
}
