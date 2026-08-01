#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public static class MacSystemBridge
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MacMenuActionCallback(int actionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MacMenuRebuildCallback();

    [DllImport("MacSystem")]
    private static extern void MacSys_SetMenuCallbacks(MacMenuActionCallback action, MacMenuRebuildCallback rebuild);

    [DllImport("MacSystem")]
    public static extern void MacSys_CreateStatusItem([MarshalAs(UnmanagedType.LPUTF8Str)] string tooltip);

    [DllImport("MacSystem")]
    public static extern void MacSys_SetStatusItemIcon(byte[] png, int pngLength);

    [DllImport("MacSystem")]
    public static extern void MacSys_RemoveStatusItem();

    [DllImport("MacSystem")]
    public static extern void MacSys_ResetMenu();

    [DllImport("MacSystem")]
    public static extern void MacSys_AddMenuItem([MarshalAs(UnmanagedType.LPUTF8Str)] string title, int actionId);

    [DllImport("MacSystem")]
    public static extern void MacSys_AddSeparator();

    [DllImport("MacSystem")]
    public static extern void MacSys_SetDockIconVisible(int visible);

    [DllImport("MacSystem")]
    public static extern int MacSys_IsDockIconVisible();

    [DllImport("MacSystem")]
    public static extern void MacSys_InstallInputMonitors();

    [DllImport("MacSystem")]
    public static extern void MacSys_UninstallInputMonitors();

    [DllImport("MacSystem")]
    public static extern double MacSys_GetLastInputAge();

    [DllImport("MacSystem")]
    public static extern int MacSys_IsAnyKeyPressed();

    [DllImport("MacSystem")]
    public static extern int MacSys_ConsumeGlobalInputActivity();

    [DllImport("MacSystem")]
    public static extern int MacSys_GetScreenCount();

    [DllImport("MacSystem")]
    public static extern void MacSys_GetScreenRect(int index, out int x, out int y, out int w, out int h);

    [DllImport("MacSystem")]
    public static extern void MacSys_GetMainScreenRect(out int x, out int y, out int w, out int h);

    [DllImport("MacSystem")]
    public static extern void MacSys_GetScreenVisibleRect(int index, out int x, out int y, out int w, out int h);

    [DllImport("MacSystem")]
    public static extern void MacSys_GetVirtualScreenRect(out int x, out int y, out int w, out int h);

    [DllImport("MacSystem")]
    public static extern void MacSys_GetCursorPos(out float x, out float y);

    [DllImport("MacSystem")]
    public static extern float MacSys_GetMainDisplayHeight();

    [DllImport("MacSystem")]
    public static extern int MacSys_GetRunningAppCount();

    [DllImport("MacSystem")]
    public static extern int MacSys_GetRunningAppName(int index, byte[] buffer, int bufferLength);

    [DllImport("MacSystem")]
    public static extern int MacSys_IsAppActive();

    [DllImport("MacSystem")]
    public static extern long MacSys_GetSpaceChangeTick();

    [DllImport("MacSystem")]
    public static extern int MacSys_IsWindowOccludedAtCursor();

    [DllImport("MacSystem")]
    public static extern int MacSys_IsScreenCaptureAuthorized();

    [DllImport("MacSystem")]
    public static extern void MacSys_RequestScreenCaptureAuthorization();

    [DllImport("MacSystem")]
    public static extern int MacSys_CaptureDesktop(int targetW, int targetH, [In, Out] byte[] buffer);

    [DllImport("MacSystem")]
    public static extern void MacSys_SetLoginItemEnabled(int enable);

    [DllImport("MacSystem")]
    public static extern int MacSys_IsLoginItemEnabled();

    [DllImport("MacSystem")]
    public static extern ulong MacSys_RelieveMemory();

    private static MacMenuActionCallback _menuActionCallback;
    private static MacMenuRebuildCallback _menuRebuildCallback;
    private static readonly object _menuQueueLock = new object();
    private static readonly Queue<Action> _menuQueue = new Queue<Action>();
    private static MacSystemPump _pump;

    public static Action<int> MenuAction;
    public static Action MenuRebuild;

    private sealed class MacSystemPump : MonoBehaviour
    {
        private void Update()
        {
            PumpCallbacks();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInstall()
    {
        EnsurePump();
        InstallInputMonitors();
    }

    private static void EnsurePump()
    {
        if (_pump != null) return;
        var go = new GameObject("MacSystemBridgePump");
        go.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DontDestroyOnLoad(go);
        _pump = go.AddComponent<MacSystemPump>();
    }

    public static void InstallInputMonitors()
    {
        try { MacSys_InstallInputMonitors(); }
        catch (Exception e) { Debug.LogWarning("[MacSystem] input monitor install failed: " + e.Message); }
    }

    public static void SetMenuCallbacks(Action<int> action, Action rebuild)
    {
        MenuAction = action;
        MenuRebuild = rebuild;
        _menuActionCallback = OnMenuAction;
        _menuRebuildCallback = OnMenuRebuild;
        MacSys_SetMenuCallbacks(_menuActionCallback, _menuRebuildCallback);
    }

    private static void OnMenuAction(int actionId)
    {
        lock (_menuQueueLock)
        {
            _menuQueue.Enqueue(() => MenuAction?.Invoke(actionId));
        }
    }

    private static void OnMenuRebuild()
    {
        lock (_menuQueueLock)
        {
            _menuQueue.Enqueue(() => MenuRebuild?.Invoke());
        }
    }

    public static void PumpCallbacks()
    {
        while (true)
        {
            Action next;
            lock (_menuQueueLock)
            {
                if (_menuQueue.Count == 0) return;
                next = _menuQueue.Dequeue();
            }

            try
            {
                next();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MacSystem] menu callback error: " + e.Message);
            }
        }
    }

    public static bool IsGlobalInputActive()
    {
        return IsGlobalUserInputActive();
    }

    public static bool IsGlobalUserInputActive()
    {
        try { return MacSys_GetLastInputAge() < 1.0; }
        catch (Exception) { return false; }
    }

    public static bool IsAnyKeyPressed()
    {
        try { return MacSys_IsAnyKeyPressed() != 0; }
        catch (Exception) { return false; }
    }

    public static bool ConsumeGlobalInputActivity()
    {
        try { return MacSys_ConsumeGlobalInputActivity() != 0; }
        catch (Exception) { return false; }
    }

    private static long _lastSpaceChangeTick;
    public static bool ConsumeSpaceChange()
    {
        try
        {
            long tick = MacSys_GetSpaceChangeTick();
            if (tick == _lastSpaceChangeTick) return false;
            _lastSpaceChangeTick = tick;
            return true;
        }
        catch (Exception) { return false; }
    }

    public static bool IsScreenCaptureAuthorized()
    {
        try { return MacSys_IsScreenCaptureAuthorized() != 0; }
        catch (Exception) { return false; }
    }

    public static void RequestScreenCaptureAuthorization()
    {
        try { MacSys_RequestScreenCaptureAuthorization(); }
        catch (Exception e) { Debug.LogWarning("[MacSystem] screen capture auth request failed: " + e.Message); }
    }

    public static bool CaptureDesktop(int width, int height, byte[] buffer)
    {
        try { return MacSys_CaptureDesktop(width, height, buffer) != 0; }
        catch (Exception e) { Debug.LogWarning("[MacSystem] desktop capture failed: " + e.Message); return false; }
    }

    public static bool SetLoginItemEnabled(bool enable)
    {
        try { MacSys_SetLoginItemEnabled(enable ? 1 : 0); return true; }
        catch (Exception e) { Debug.LogWarning("[MacSystem] login item update failed: " + e.Message); return false; }
    }

    public static bool IsLoginItemEnabled()
    {
        try { return MacSys_IsLoginItemEnabled() != 0; }
        catch (Exception) { return false; }
    }

    public static List<string> GetRunningAppNames()
    {
        var names = new List<string>();
        try
        {
            int count = MacSys_GetRunningAppCount();
            byte[] buffer = new byte[512];
            for (int i = 0; i < count; i++)
            {
                if (MacSys_GetRunningAppName(i, buffer, buffer.Length) != 0)
                    continue;
                string name = System.Text.Encoding.UTF8.GetString(buffer).TrimEnd('\0');
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MacSystem] running app enumeration failed: " + e.Message);
        }
        return names;
    }
}
#endif
