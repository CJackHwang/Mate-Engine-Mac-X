using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LineRenderer))]
public class AdvancedPhysBone : MonoBehaviour
{
    [Header("Root Bone Settings")]
    public Transform rootBone; // The starting bone (e.g., hair root, tail base)

    [Header("Physics Settings")]
    [Range(0f, 1f)] public float stiffness = 0.5f; // Resistance to movement
    [Range(0f, 1f)] public float elasticity = 0.5f; // Tendency to return to original position
    public float gravity = -0.5f; // Gravity effect
    public float damping = 0.05f; // Damping to reduce oscillations

    [Header("Collision Settings")]
    public List<SphereCollider> collisionObjects = new List<SphereCollider>(); // Colliders for interaction

    private List<Transform> bones = new List<Transform>();
    private Dictionary<Transform, Vector3> boneVelocities = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, Vector3> initialLocalPositions = new Dictionary<Transform, Vector3>();

    void Start()
    {
        if (rootBone == null)
        {
            Debug.LogError("AdvancedPhysBone: No root bone assigned!");
            return;
        }

        InitializeBones(rootBone);
    }

    void FixedUpdate()
    {
        SimulatePhysics();
    }

    void OnDrawGizmos()
    {
        if (rootBone == null) return;

        Gizmos.color = Color.cyan;
        DrawBoneGizmos(rootBone);
    }

    private void InitializeBones(Transform currentBone)
    {
        foreach (Transform child in currentBone)
        {
            bones.Add(child);
            boneVelocities[child] = Vector3.zero;
            initialLocalPositions[child] = child.localPosition;
            InitializeBones(child); // Recursively add all child bones
        }
    }

    private void SimulatePhysics()
    {
        foreach (Transform bone in bones)
        {
            Vector3 currentPos = bone.position;
            Vector3 velocity = boneVelocities[bone];

            // Apply gravity
            velocity += Vector3.up * gravity * Time.fixedDeltaTime;

            // Apply damping
            velocity *= (1 - damping);

            // Calculate new position
            Vector3 newPosition = currentPos + velocity * Time.fixedDeltaTime;

            // Apply stiffness and elasticity
            Vector3 targetPosition = bone.parent.TransformPoint(initialLocalPositions[bone]);
            Vector3 force = (targetPosition - newPosition) * stiffness;
            velocity += force * elasticity;

            // Handle collisions
            foreach (SphereCollider col in collisionObjects)
            {
                if (col != null)
                {
                    Vector3 direction = newPosition - col.transform.position;
                    float distance = direction.magnitude;
                    float radius = col.radius;

                    if (distance < radius)
                    {
                        newPosition = col.transform.position + direction.normalized * radius;
                        velocity *= -0.3f; // Apply bounce effect
                    }
                }
            }

            // Update bone position and velocity
            bone.position = newPosition;
            boneVelocities[bone] = velocity;
        }
    }

    private void DrawBoneGizmos(Transform currentBone)
    {
        foreach (Transform child in currentBone)
        {
            Gizmos.DrawLine(currentBone.position, child.position);
            Gizmos.DrawSphere(child.position, 0.01f);
            DrawBoneGizmos(child); // Recursively draw for all child bones
        }
    }
}
