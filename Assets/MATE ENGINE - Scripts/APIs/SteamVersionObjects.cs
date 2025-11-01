using UnityEngine;
using System.Collections.Generic;

public class SteamVersionObjects : MonoBehaviour
{
    [Header("Enable These Only on Steam Version")]
    public List<GameObject> steamOnlyObjects = new List<GameObject>();

    [Header("Enable These Only on Non-Steam Version")]
    public List<GameObject> notSteamObjects = new List<GameObject>();

    private void Start()
    {
        if (!SteamChecker.IsSteamVersionInitialized)
            SteamChecker.Initialize();

        bool isSteam = SteamChecker.IsSteamVersion();

        foreach (var obj in steamOnlyObjects)
            if (obj != null)
                obj.SetActive(isSteam);

        foreach (var obj in notSteamObjects)
            if (obj != null)
                obj.SetActive(!isSteam);
    }
}
