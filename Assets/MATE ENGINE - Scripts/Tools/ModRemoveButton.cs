using UnityEngine;
using UnityEngine.UI;
using Steamworks;
using System.IO;
using System.Collections;
using System.Linq;

public class ModRemoveButton : MonoBehaviour
{
    public Button button;
    public string filePath;
    public ulong workshopId;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        ulong id = workshopId;
        if (id == 0UL) id = ResolveWorkshopIdForPath(filePath);
        if (id != 0UL && SteamManager.Initialized)
        {
            var h = SteamWorkshopHandler.Instance;
            if (h != null) h.UnsubscribeAndDelete(new PublishedFileId_t(id));
        }

        if (!string.IsNullOrEmpty(filePath))
        {
            try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
        }

        var modHandler = FindFirstObjectByType<MEModHandler>();
        if (modHandler != null) modHandler.SendMessage("LoadAllModsInFolder", SendMessageOptions.DontRequireReceiver);
        var dance = FindFirstObjectByType<CustomDancePlayer.AvatarDanceHandler>();
        if (dance != null) dance.RescanMods();
    }

    ulong ResolveWorkshopIdForPath(string localPath)
    {
        try
        {
            if (!SteamManager.Initialized) return 0UL;
            uint count = SteamUGC.GetNumSubscribedItems();
            if (count == 0) return 0UL;

            var ids = new PublishedFileId_t[count];
            SteamUGC.GetSubscribedItems(ids, count);

            string targetName = Path.GetFileName(localPath);
            for (int i = 0; i < ids.Length; i++)
            {
                if (!SteamUGC.GetItemInstallInfo(ids[i], out ulong _, out string installPath, 1024, out uint _)) continue;
                if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) continue;
                var top = Directory.GetFiles(installPath, "*", SearchOption.TopDirectoryOnly);
                for (int f = 0; f < top.Length; f++)
                {
                    if (string.Equals(Path.GetFileName(top[f]), targetName, System.StringComparison.OrdinalIgnoreCase))
                        return ids[i].m_PublishedFileId;
                }
            }
        }
        catch { }
        return 0UL;
    }
}
