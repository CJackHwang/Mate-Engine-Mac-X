using UnityEngine;
using System.Collections;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System;

public class MemoryTrim : MonoBehaviour
{
    public bool enableAutoTrim = false;

    public void SetAutoTrimEnabled(bool enabled)
    {
        enableAutoTrim = enabled;
        CancelInvoke(nameof(StartupTrim));
        CancelInvoke(nameof(PeriodicTrim));
        if (enableAutoTrim)
        {
            TrimNow();
            Invoke(nameof(StartupTrim), 10f);
            InvokeRepeating(nameof(PeriodicTrim), 600f, 600f);
        }
    }

    void DelayedStartupTrim()
    {
        if (enableAutoTrim) TrimNow();
    }


    void Awake()
    {
        if (enableAutoTrim)
        {
            TrimNow();
            Invoke(nameof(StartupTrim), 10f);
            InvokeRepeating(nameof(PeriodicTrim), 600f, 600f);
            Invoke(nameof(DelayedStartupTrim), 15f);
        }
    }

    void OnDisable()
    {
        CancelInvoke(nameof(StartupTrim));
        CancelInvoke(nameof(PeriodicTrim));
    }

    public void TrimNow()
    {
        StartCoroutine(TrimRoutine());
    }

    void StartupTrim()
    {
        TrimNow();
    }

    void PeriodicTrim()
    {
        TrimNow();
    }

    IEnumerator TrimRoutine()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, true, true);
        AsyncOperation op = Resources.UnloadUnusedAssets();
        while (!op.isDone) yield return null;
        TrimWorkingSet();
    }

    static void TrimWorkingSet()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        EmptyWorkingSet(Process.GetCurrentProcess().Handle);
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("psapi.dll")]
    static extern bool EmptyWorkingSet(IntPtr hProcess);
#endif
}
