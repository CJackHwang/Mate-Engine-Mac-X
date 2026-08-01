using UnityEngine;

public static class SettingsHandlerUtility
{
    public static void ReloadAllSettingsHandlers()
    {
        foreach (var handler in GameObject.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (!handler.isActiveAndEnabled) continue; 
            var type = handler.GetType();
            var loadMethod = type.GetMethod("LoadSettings");
            var applyMethod = type.GetMethod("ApplySettings");
            if (loadMethod != null) loadMethod.Invoke(handler, null);
            if (applyMethod != null) applyMethod.Invoke(handler, null);
        }
    }
}

/*
using UnityEngine;

public static class SettingsHandlerUtility
{
    public static void ReloadAllSettingsHandlers()
    {
        var handlers = GameObject.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include);
        foreach (var handler in handlers)
        {
            var type = handler.GetType();
            var loadMethod = type.GetMethod("LoadSettings");
            var applyMethod = type.GetMethod("ApplySettings");
            if (loadMethod != null) loadMethod.Invoke(handler, null);
            if (applyMethod != null) applyMethod.Invoke(handler, null);
        }
    }
}
*/