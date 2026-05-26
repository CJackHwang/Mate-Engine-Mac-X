#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
using System.Runtime.InteropServices;
using UnityEngine;

public static class MacAudioMonitorBinding
{
    [DllImport("MacAudioMonitor")] private static extern void MacAudio_Start();
    [DllImport("MacAudioMonitor")] private static extern void MacAudio_Stop();
    [DllImport("MacAudioMonitor")] private static extern int MacAudio_IsOutputActive();
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

    public static bool IsOutputActive()
    {
        try { return MacAudio_IsOutputActive() != 0; }
        catch (System.Exception e) { Debug.LogError("[MacAudioMonitor] IsOutputActive exception: " + e.Message); return false; }
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
