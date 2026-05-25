using UnityEngine;
using System.Collections;

/// <summary>
/// Mac startup: shows a load button if no VRM is saved.
/// Runs independently of scene hierarchy via RuntimeInitializeOnLoadMethod.
/// </summary>
public class MacStartupLoader : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnSceneLoaded()
    {
#if !UNITY_EDITOR
        var go = new GameObject("MacStartupLoader");
        go.AddComponent<MacStartupLoader>();
        DontDestroyOnLoad(go);
#endif
    }

    IEnumerator Start()
    {
        // Wait two frames for SaveLoadHandler and VRMLoader to initialise
        yield return new UnityEngine.WaitForSeconds(1f);

        string savedPath = SaveLoadHandler.Instance != null
            ? SaveLoadHandler.Instance.data.selectedModelPath
            : null;

        if (!string.IsNullOrEmpty(savedPath))
        {
            // Already have a saved path — VRMLoader will handle it
            Destroy(gameObject);
            yield break;
        }

        var loader = Object.FindFirstObjectByType<VRMLoader>();
        if (loader != null)
        {
            Debug.Log("[MacStartupLoader] No saved model, showing load button.");
            loader.ShowLoadButtonPublic();
        }
        else
        {
            Debug.LogWarning("[MacStartupLoader] VRMLoader not found in scene.");
        }

        Destroy(gameObject);
    }
}
