using UnityEngine;

namespace uWindowCapture
{

[RequireComponent(typeof(Renderer))]
public class UwcCursorTexture : MonoBehaviour 
{
    Renderer renderer_;
    Material material_;

    UwcCursor cursor
    {
        get { return UwcManager.cursor; }
    }

    void Awake()
    {
        renderer_ = GetComponent<Renderer>();
        material_ = renderer_.material; // clone
        cursor.onTextureChanged.AddListener(OnTextureChanged);
    }

    void Update()
    {
#if UNITY_STANDALONE_WIN
        cursor.CreateTextureIfNeeded();
        cursor.RequestCapture();
#endif
    }

    void OnTextureChanged()
    {
        material_.mainTexture = cursor.texture;
    }
}

}