using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SteamVersionObjects : MonoBehaviour
{
    public int steamAppId = 0;
    public int ttlDays = 14;
    public float retrySeconds = 5f;
    public float maxWaitSeconds = 180f;

    public List<GameObject> steamOnlyObjects = new List<GameObject>();
    public List<GameObject> notSteamObjects = new List<GameObject>();

    bool lastEntitled;

    void Start()
    {
        if (!SteamDRM.Initialized) SteamDRM.Initialize(steamAppId, ttlDays);
        lastEntitled = SteamDRM.IsEntitled;
        Apply(lastEntitled);
        StartCoroutine(ReinitLoop());
    }

    void Update()
    {
        var e = SteamDRM.IsEntitled;
        if (e != lastEntitled) { lastEntitled = e; Apply(e); }
    }

    IEnumerator ReinitLoop()
    {
        float t = 0f;
        while (!SteamDRM.IsEntitled && t < maxWaitSeconds)
        {
            SteamDRM.TryInitLive(steamAppId, ttlDays);
            yield return new WaitForSeconds(retrySeconds);
            t += retrySeconds;
        }
    }

    void Apply(bool isSteam)
    {
        for (int i = 0; i < steamOnlyObjects.Count; i++) if (steamOnlyObjects[i]) steamOnlyObjects[i].SetActive(isSteam);
        for (int i = 0; i < notSteamObjects.Count; i++) if (notSteamObjects[i]) notSteamObjects[i].SetActive(!isSteam);
    }
}
