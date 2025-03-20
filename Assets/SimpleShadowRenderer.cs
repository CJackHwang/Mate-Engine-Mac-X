using UnityEngine;

public class SimpleShadowRenderer : MonoBehaviour
{
    public Transform target; // The object that casts the shadow (your desktop pet)
    public float shadowSize = 1.5f; // Size of the shadow
    public float shadowOpacity = 0.5f; // Opacity of the shadow
    public Vector3 shadowOffset = new Vector3(0, 0.01f, 0); // Adjust position

    private GameObject shadowObject;
    private Material shadowMaterial;

    void Start()
    {
        // Create a shadow quad
        shadowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shadowObject.name = "FakeShadow";
        shadowObject.transform.SetParent(transform);
        shadowObject.transform.localRotation = Quaternion.Euler(90, 0, 0); // Face upwards

        // Remove collider (not needed)
        Destroy(shadowObject.GetComponent<Collider>());

        // Create shadow material
        shadowMaterial = new Material(Shader.Find("Unlit/Transparent"));
        shadowMaterial.color = new Color(0, 0, 0, shadowOpacity); // Black with transparency
        shadowObject.GetComponent<Renderer>().material = shadowMaterial;
    }

    void Update()
    {
        if (target != null)
        {
            // Position shadow at the pet's feet with an offset
            shadowObject.transform.position = new Vector3(target.position.x, shadowOffset.y, target.position.z);

            // Scale shadow based on desired size
            shadowObject.transform.localScale = new Vector3(shadowSize, shadowSize, 1);
        }
    }
}
