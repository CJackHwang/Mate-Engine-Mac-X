using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Generic;

public class LocalizationSelectivePreload : MonoBehaviour
{
    public List<string> stringTableCollections = new List<string>();
    readonly List<AsyncOperationHandle> activeHandles = new List<AsyncOperationHandle>();
    Locale current;

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        current = LocalizationSettings.SelectedLocale;
        Preload(current);
    }

    void OnDisable()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;
        ReleaseAll();
    }

    void OnLocaleChanged(Locale locale)
    {
        if (locale == current) return;
        ReleaseAll();
        current = locale;
        Preload(locale);
    }

    void Preload(Locale locale)
    {
        foreach (var collection in stringTableCollections)
        {
            var h = LocalizationSettings.StringDatabase.GetTableAsync(collection, locale);
            activeHandles.Add(h);
        }
    }

    void ReleaseAll()
    {
        for (int i = 0; i < activeHandles.Count; i++)
            if (activeHandles[i].IsValid())
                Addressables.Release(activeHandles[i]);
        activeHandles.Clear();
    }
}
