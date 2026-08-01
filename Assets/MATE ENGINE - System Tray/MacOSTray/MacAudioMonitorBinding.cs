#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System.Runtime.InteropServices;
using UnityEngine;

public static class MacAudioMonitorBinding
{
    [DllImport("MacAudioMonitor")] private static extern void MacAudio_Start();
    [DllImport("MacAudioMonitor")] private static extern void MacAudio_Stop();
    [DllImport("MacAudioMonitor")] private static extern int MacAudio_IsOutputActive();
    [DllImport("MacAudioMonitor")] private static extern int MacAudio_SystemCaptureAvailable();
    [DllImport("MacAudioMonitor")] private static extern int MacAudio_HasCapturePermission();
    [DllImport("MacAudioMonitor")] private static extern int MacAudio_GetDefaultDeviceName(byte[] buf, int bufLen);

    public static void Start()
    {
        try { MacAudio_Start(); }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] Start exception: " + e.Message); }
    }

    public static void Stop()
    {
        try { MacAudio_Stop(); }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] Stop exception: " + e.Message); }
    }

    // 1 = system audio above threshold, 0 = capture running but silent, -1 = capture unavailable.
    public static int OutputActivity()
    {
        try { return MacAudio_IsOutputActive(); }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] IsOutputActive exception: " + e.Message); return -1; }
    }

    // Whether the ScreenCaptureKit system-audio stream is actually running.
    public static bool IsSystemCaptureAvailable()
    {
        try { return MacAudio_SystemCaptureAvailable() != 0; }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] SystemCaptureAvailable exception: " + e.Message); return false; }
    }

    // Whether Screen Recording permission is granted (required for system-audio capture).
    public static bool HasCapturePermission()
    {
        try { return MacAudio_HasCapturePermission() != 0; }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] HasCapturePermission exception: " + e.Message); return false; }
    }

    public static string GetDefaultDeviceName()
    {
        try
        {
            byte[] buf = new byte[256];
            MacAudio_GetDefaultDeviceName(buf, buf.Length);
            return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }
        catch (System.Exception e) { return "error: " + e.Message; }
    }
}
#endif
