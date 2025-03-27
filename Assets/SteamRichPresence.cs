using Steamworks;
using UnityEngine;

public class SteamRichPresence : MonoBehaviour
{
    [Header("Rich Presence Settings")]
    [Tooltip("Steam Rich Presence key (usually 'status')")]
    public string presenceKey = "status";

    [Tooltip("The status text that appears in your Steam friends list")]
    [TextArea]
    public string presenceMessage = "Hanging out with MateEngine";

    [Tooltip("Clear rich presence when the app closes")]
    public bool clearOnExit = true;

    void Start()
    {
        if (SteamManager.Initialized)
        {
            SteamFriends.SetRichPresence(presenceKey, presenceMessage);
            Debug.Log($"[Steam] Rich Presence set: {presenceKey} = \"{presenceMessage}\"");
        }
        else
        {
            Debug.LogWarning("[Steam] Not initialized. Cannot set Rich Presence.");
        }
    }

    void OnApplicationQuit()
    {
        if (SteamManager.Initialized && clearOnExit)
        {
            SteamFriends.ClearRichPresence();
            Debug.Log("[Steam] Rich Presence cleared.");
        }
    }
}
