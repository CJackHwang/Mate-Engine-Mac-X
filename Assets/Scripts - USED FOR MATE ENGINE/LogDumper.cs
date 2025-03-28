using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class LogDumper : MonoBehaviour
{
    [Header("Enable to Block Sentis Shader Warnings")]
    public bool blockSentisWarnings = true;

#if UNITY_EDITOR
    private static bool initialized = false;

    private void OnEnable()
    {
        if (!initialized)
        {
            Application.logMessageReceivedThreaded += FilterSentisLogs;
            initialized = true;
        }
    }

    private void OnDisable()
    {
        Application.logMessageReceivedThreaded -= FilterSentisLogs;
        initialized = false;
    }

    private void OnDestroy()
    {
        Application.logMessageReceivedThreaded -= FilterSentisLogs;
        initialized = false;
    }

    private void FilterSentisLogs(string condition, string stackTrace, LogType type)
    {
        if (!blockSentisWarnings) return;

        if (type == LogType.Warning && condition.Contains("Shader warning") && condition.Contains("Sentis"))
        {
            // Suppress Sentis shader warning in Editor console
            return;
        }

        // Allow normal logs
        Debug.unityLogger.Log(type, condition);
    }
#endif
}
