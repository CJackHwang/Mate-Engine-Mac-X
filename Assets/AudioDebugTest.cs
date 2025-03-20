using UnityEngine;
using NAudio.CoreAudioApi;
using System.Collections.Generic;

public class AudioDebugTest : MonoBehaviour
{
    private MMDeviceEnumerator deviceEnumerator;
    private List<MMDevice> audioDevices = new List<MMDevice>();

    void Start()
    {
        deviceEnumerator = new MMDeviceEnumerator();
        UpdateAudioDevices();
    }

    void UpdateAudioDevices()
    {
        audioDevices.Clear();

        foreach (var device in deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            Debug.Log($"🎧 Detected Audio Device: {device.FriendlyName}");
            audioDevices.Add(device);
        }

        if (audioDevices.Count == 0)
        {
            Debug.LogError("❌ No active audio devices found! Check Windows sound settings.");
        }
    }
}
