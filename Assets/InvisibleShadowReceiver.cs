using UnityEngine;
using UnityEngine.Rendering;

public class InvisibleShadowReceiver : MonoBehaviour
{
    void Start()
    {
        // Get Renderer
        Renderer renderer = GetComponent<Renderer>();

        if (renderer == null)
        {
            Debug.LogError("No Renderer found! Attach this script to a plane with a Renderer.");
            return;
        }

        // Apply custom shader for invisible shadow receiving
        Material shadowMaterial = new Material(Shader.Find("Custom/InvisibleShadow"));
        renderer.material = shadowMaterial;

        // Enable shadow receiving, disable shadow casting
        renderer.receiveShadows = true;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
