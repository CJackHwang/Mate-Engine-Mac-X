using UnityEngine;

public class FixedPosition : MonoBehaviour
{
    private Vector3 fixedPosition;

    void Start()
    {
        // Store the initial position
        fixedPosition = transform.position;
    }

    void LateUpdate()
    {
        // Lock position but allow rotation and animation
        transform.position = fixedPosition;
    }
}
