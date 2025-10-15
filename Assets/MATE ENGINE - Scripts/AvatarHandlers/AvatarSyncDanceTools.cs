using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace CustomDancePlayer
{
    public class AvatarSyncDanceTools : MonoBehaviour
    {
        public Toggle syncToggle;

        FileStream lockStream;
        string busPath;
        string busTmpPath;
        bool isMain;

        void Awake()
        {
            isMain = GetInstanceIndex() == 0;
            string fileName = "avatar_dance_play_bus.json";

            var ads = FindFirstObjectByType<AvatarDanceSync>();
            if (ads != null)
            {
                var fi = typeof(AvatarDanceSync).GetField("fileName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(ads) as string;
                    if (!string.IsNullOrEmpty(val)) fileName = val;
                }
            }

            var dir = Path.Combine(Application.persistentDataPath, "Sync");
            try { Directory.CreateDirectory(dir); } catch { }
            busPath = Path.Combine(dir, fileName);
            busTmpPath = busPath + ".tmp";
        }

        void OnEnable()
        {
            if (syncToggle != null) syncToggle.onValueChanged.AddListener(OnToggleChanged);
            ApplyState();
        }

        void OnDisable()
        {
            if (syncToggle != null) syncToggle.onValueChanged.RemoveListener(OnToggleChanged);
            ReleaseLock();
        }

        void OnDestroy()
        {
            ReleaseLock();
        }

        void OnToggleChanged(bool _)
        {
            ApplyState();
        }

        void ApplyState()
        {
            if (!isMain) return;
            bool allow = syncToggle == null ? true : syncToggle.isOn;
            if (allow) ReleaseLock();
            else AcquireLock();
        }

        void AcquireLock()
        {
            ReleaseLock();
            try
            {
                lockStream = new FileStream(busPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                lockStream = null;
            }
        }

        void ReleaseLock()
        {
            if (lockStream != null)
            {
                try { lockStream.Dispose(); } catch { }
                lockStream = null;
            }
            TryCleanTmp();
        }

        void TryCleanTmp()
        {
            try { if (File.Exists(busTmpPath)) File.Delete(busTmpPath); } catch { }
        }

        int GetInstanceIndex()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "--instance", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int v))
                    return Math.Max(0, v);
            return 0;
        }
    }
}
