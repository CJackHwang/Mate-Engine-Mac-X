using UnityEngine;
using System;
using System.IO;

public class MateScreenshotHandler : MonoBehaviour
{
    public Camera targetCamera;
    public int msaa = 4;

    public void TakeScreenshot()
    {
        Camera cam = targetCamera != null ? targetCamera : (Camera.main != null ? Camera.main : FindObjectOfType<Camera>());
        if (cam == null) return;

        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        string dir = Path.Combine(pictures, "MateScreenshots");
        Directory.CreateDirectory(dir);

        int w = Mathf.Max(1, Screen.width * 2);
        int h = Mathf.Max(1, Screen.height * 2);
        int aa = Mathf.Clamp(msaa, 1, 8);

        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = aa;

        var prevActive = RenderTexture.active;
        var prevTarget = cam.targetTexture;
        var prevFlags = cam.clearFlags;
        var prevBg = cam.backgroundColor;

        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

        RenderTexture.active = rt;
        cam.Render();

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        string path = Path.Combine(dir, "MateScreenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        File.WriteAllBytes(path, tex.EncodeToPNG());

        cam.targetTexture = prevTarget;
        cam.clearFlags = prevFlags;
        cam.backgroundColor = prevBg;
        RenderTexture.active = prevActive;

        rt.Release();
        Destroy(rt);
        Destroy(tex);
    }
}
